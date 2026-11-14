using System;
using System.Security.Cryptography;

using NBitcoin.Secp256k1;

namespace Xrpl.Keypairs.K256
{
    /// <summary>
    /// The secp256k1 operations of the SDK, run on NBitcoin.Secp256k1 (a port of libsecp256k1).
    /// </summary>
    /// <remarks>
    /// The scalar and the RFC 6979 nonce are value-type <see cref="Scalar"/>s, never an immutable
    /// heap big integer that cannot be cleared; a leaked nonce gives the key away as surely as the
    /// key itself. The nonce and the key copy the signer makes live on the stack and are cleared
    /// before it returns, and an <see cref="ECPrivKey"/> clears its scalar on dispose. The hash
    /// implementations behind RFC 6979 and the key derivation keep state of their own.
    /// </remarks>
    internal static class K256SecretKey
    {
        private const int ScalarLength = 32;

        /// <summary>
        /// Signs the SHA-512Half of <paramref name="message"/> with an RFC 6979 nonce and a low S:
        /// byte for byte the signature the SDK made with BouncyCastle up to 11.26.
        /// </summary>
        /// <param name="message">The message bytes.</param>
        /// <param name="privateKey">
        /// The key as an unsigned big-endian number: the 33-byte XRPL form with the 00 prefix, the
        /// raw 32 bytes, or fewer bytes when leading zeros were dropped.
        /// </param>
        /// <returns>The DER-encoded signature.</returns>
        /// <exception cref="ArgumentException">The key is zero, not below the curve order, or longer than 32 significant bytes.</exception>
        internal static byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> privateKey)
        {
            byte[] digest = Utils.Sha512.Half(message.ToArray());
            using ECPrivKey key = CreateKey(privateKey);
            SecpECDSASignature signature = key.SignECDSARFC6979(digest);
            Span<byte> der = stackalloc byte[72];
            signature.WriteDerToSpan(der, out int length);
            return der.Slice(0, length).ToArray();
        }

        /// <summary>Derives the 33-byte compressed public key.</summary>
        /// <param name="privateKey">The key, in any form <see cref="Sign"/> takes.</param>
        /// <exception cref="ArgumentException">The key is not a valid scalar.</exception>
        internal static byte[] PublicKey(ReadOnlySpan<byte> privateKey)
        {
            using ECPrivKey key = CreateKey(privateKey);
            return CompressedPublicKey(key);
        }

        /// <summary>
        /// Derives a key of the family a seed defines, as rippled does: the private generator
        /// (root key) is the first SHA-512Half of seed || i below the curve order, and account key
        /// <paramref name="keyIndex"/> adds to it the first SHA-512Half of
        /// public generator || keyIndex || i below the order.
        /// </summary>
        /// <param name="seed">The seed bytes, 16 for an XRPL seed.</param>
        /// <param name="keyIndex">The account key index, or -1 for the root key a validator uses.</param>
        /// <param name="privateKey">Receives the 32-byte scalar.</param>
        /// <returns>The 33-byte compressed public key.</returns>
        internal static byte[] DeriveFamilyKey(ReadOnlySpan<byte> seed, int keyIndex, Span<byte> privateKey)
        {
            Span<byte> scalar = stackalloc byte[ScalarLength];
            Span<byte> publicGenerator = stackalloc byte[33];
            try
            {
                ComputeScalar(seed, null, scalar);
                using ECPrivKey privateGenerator = ECPrivKey.Create(scalar);
                if (keyIndex == -1)
                {
                    privateGenerator.WriteToSpan(privateKey);
                    return CompressedPublicKey(privateGenerator);
                }

                privateGenerator.CreatePubKey().WriteToSpan(true, publicGenerator, out _);
                ComputeScalar(publicGenerator, (uint)keyIndex, scalar);
                // A sum of zero would need the tweak to be the generator's negation, about 2^-128.
                using ECPrivKey accountKey = privateGenerator.TweakAdd(scalar);
                accountKey.WriteToSpan(privateKey);
                return CompressedPublicKey(accountKey);
            }
            finally
            {
                scalar.Clear();
            }
        }

        /// <summary>
        /// Adds <paramref name="tweak"/> to the private key modulo the curve order.
        /// </summary>
        /// <exception cref="ArgumentException">A key or the tweak is not a valid scalar, or the sum is zero.</exception>
        internal static byte[] AddScalar(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> tweak)
        {
            Span<byte> tweak32 = stackalloc byte[ScalarLength];
            try
            {
                if (!TryWriteScalar(tweak, tweak32))
                    throw new ArgumentException("The tweak is longer than 32 significant bytes.", nameof(tweak));
                using ECPrivKey key = CreateKey(privateKey);
                using ECPrivKey sum = key.TweakAdd(tweak32);
                byte[] result = new byte[ScalarLength];
                sum.WriteToSpan(result);
                return result;
            }
            finally
            {
                tweak32.Clear();
            }
        }

        /// <summary>
        /// Adds <paramref name="tweak"/>·G to a public key: the public key of the private key
        /// <see cref="AddScalar"/> makes.
        /// </summary>
        /// <param name="publicKey">A compressed or uncompressed public key.</param>
        /// <param name="tweak">The 32-byte scalar.</param>
        /// <returns>The 33-byte compressed result.</returns>
        /// <exception cref="ArgumentException">The public key is not a point on the curve, or the tweak is not below the order.</exception>
        internal static byte[] AddToPublicKey(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> tweak)
        {
            ECPubKey point = ParsePublicKey(publicKey);
            if (tweak.Length != ScalarLength || !point.TryAddTweak(tweak, out ECPubKey sum))
                throw new ArgumentException("The tweak is not a 32-byte scalar below the curve order, or the sum is the point at infinity.", nameof(tweak));
            return sum.ToBytes(true);
        }

        /// <summary>
        /// The first SHA-512Half of <paramref name="input"/> || [<paramref name="discriminator"/>] || i,
        /// for i from 0, that is a valid private key.
        /// </summary>
        internal static byte[] ComputeScalar(ReadOnlySpan<byte> input, uint? discriminator)
        {
            byte[] scalar = new byte[ScalarLength];
            ComputeScalar(input, discriminator, scalar);
            return scalar;
        }

        /// <summary>
        /// Verifies a DER signature over the SHA-512Half of <paramref name="message"/>. A high S is
        /// accepted, as BouncyCastle's verifier accepted it; libsecp256k1 itself refuses one, so the
        /// signature is normalized first.
        /// </summary>
        /// <exception cref="ArgumentException">The public key is not a point on the curve.</exception>
        internal static bool Verify(byte[] signature, byte[] message, ReadOnlySpan<byte> publicKey)
        {
            ECPubKey point = ParsePublicKey(publicKey);
            EcdsaSignature decoded = EcdsaSignature.DecodeFromDer(signature);
            if (decoded == null || decoded.R.IsZero || decoded.S.IsZero)
                return false;

            Span<byte> compact = stackalloc byte[2 * ScalarLength];
            if (!TryWriteComponent(decoded.R, compact.Slice(0, ScalarLength))
                || !TryWriteComponent(decoded.S, compact.Slice(ScalarLength))
                || !SecpECDSASignature.TryCreateFromCompact(compact, out SecpECDSASignature parsed))
            {
                return false;
            }

            parsed.TryNormalize(out SecpECDSASignature lowS);
            return point.SigVerify(lowS, Utils.Sha512.Half(message));
        }

        private static bool TryWriteComponent(System.Numerics.BigInteger value, Span<byte> destination)
        {
            int length = value.GetByteCount(isUnsigned: true);
            if (length > destination.Length)
                return false;
            destination.Clear();
            return value.TryWriteBytes(destination.Slice(destination.Length - length), out _, isUnsigned: true, isBigEndian: true);
        }

        private static ECPubKey ParsePublicKey(ReadOnlySpan<byte> publicKey)
        {
            if (!ECPubKey.TryCreate(publicKey, null, out _, out ECPubKey point))
                throw new ArgumentException("The public key is not a valid secp256k1 point encoding.", nameof(publicKey));
            return point;
        }

        /// <summary>The key as a 32-byte scalar, checked to lie in [1, n-1].</summary>
        /// <param name="privateKey">The key, in any form <see cref="Sign"/> takes.</param>
        /// <exception cref="ArgumentException">The key is not a valid scalar.</exception>
        internal static byte[] ToScalar(ReadOnlySpan<byte> privateKey)
        {
            using ECPrivKey key = CreateKey(privateKey);
            byte[] scalar = new byte[ScalarLength];
            key.WriteToSpan(scalar);
            return scalar;
        }

        private static ECPrivKey CreateKey(ReadOnlySpan<byte> privateKey)
        {
            Span<byte> scalar = stackalloc byte[ScalarLength];
            try
            {
                if (!TryWriteScalar(privateKey, scalar) || !ECPrivKey.TryCreate(scalar, out ECPrivKey key))
                    throw new ArgumentException("The secp256k1 private key is not a valid scalar: zero, or not below the curve order.", nameof(privateKey));
                return key;
            }
            finally
            {
                scalar.Clear();
            }
        }

        // Up to 11.26 the key was read as an unsigned big integer, so any length worked as long as
        // the value fitted; keep accepting the same inputs.
        private static bool TryWriteScalar(ReadOnlySpan<byte> privateKey, Span<byte> scalar)
        {
            while (privateKey.Length > ScalarLength)
            {
                if (privateKey[0] != 0)
                    return false;
                privateKey = privateKey.Slice(1);
            }
            scalar.Clear();
            privateKey.CopyTo(scalar.Slice(ScalarLength - privateKey.Length));
            return true;
        }

        private static byte[] CompressedPublicKey(ECPrivKey key)
        {
            byte[] publicKey = new byte[33];
            key.CreatePubKey().WriteToSpan(true, publicKey, out _);
            return publicKey;
        }

        private static void ComputeScalar(ReadOnlySpan<byte> input, uint? discriminator, Span<byte> scalar)
        {
            // The input is public and of any length, so it is streamed into the hash, never
            // copied to the stack; only the fixed-size suffix and the hash live there.
            Span<byte> counter = stackalloc byte[4];
            Span<byte> hash = stackalloc byte[64];
            using IncrementalHash sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            try
            {
                for (uint i = 0; ; i++)
                {
                    sha512.AppendData(input);
                    if (discriminator.HasValue)
                    {
                        WriteUInt32BigEndian(counter, discriminator.Value);
                        sha512.AppendData(counter);
                    }
                    WriteUInt32BigEndian(counter, i);
                    sha512.AppendData(counter);
                    sha512.TryGetHashAndReset(hash, out _);
                    Scalar candidate = new Scalar(hash.Slice(0, ScalarLength), out int overflow);
                    bool valid = overflow == 0 && !candidate.IsZero;
                    Scalar.Clear(ref candidate);
                    if (valid)
                    {
                        hash.Slice(0, ScalarLength).CopyTo(scalar);
                        return;
                    }
                }
            }
            finally
            {
                hash.Clear();
            }
        }

        private static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            destination[0] = (byte)(value >> 24);
            destination[1] = (byte)(value >> 16);
            destination[2] = (byte)(value >> 8);
            destination[3] = (byte)value;
        }
    }
}
