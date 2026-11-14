using System;
using System.Security.Cryptography;
using System.Text;


using Xrpl.AddressCodec;
using Xrpl.Keypairs.Ed25519;
using Xrpl.Keypairs.K256;

using Sha512 = Xrpl.Keypairs.Utils.Sha512;

using static Xrpl.AddressCodec.Utils;
using static Xrpl.AddressCodec.XrplCodec;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-keypairs/src/index.ts

namespace Xrpl.Keypairs
{
    public static class XrplKeypairs
    {

        /// <summary> Generate random seed bytes for new account </summary>
        /// <returns>byte seed array</returns>
        public static byte[] FromRandom()
        {
            using var rng = RandomNumberGenerator.Create();
            var seed = new byte[16];
            rng.GetBytes(seed);
            return seed;
        }

        /// <summary>
        /// generate string seed from bytes array, if null - random
        /// </summary>
        /// <param name="entropy">seed byte array</param>
        /// <param name="algorithm">seed algorithm, base - secp256k1</param>
        /// <returns></returns>
        public static string GenerateSeed(byte[]? entropy = null, string algorithm = "secp256k1")
        {
            //assert.ok(
            //    !options.entropy || options.entropy.length >= 16,
            //    'entropy too short',
            //  )
            byte[] fentropy = entropy ?? FromRandom();
            return XrplCodec.EncodeSeed(fentropy, algorithm);
        }

        internal static byte[] Hash(string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            return bytes.Sha512HashHalf();
        }

        public static IXrplKeyPair DeriveKeypair(string seed, string? algorithm = null, bool validator = false, int index = 0)
        {
            DecodedSeed decoded = XrplCodec.DecodeSeed(seed);
            // As in xrpl.js: an explicit algorithm overrides the seed's own prefix, and anything but ed25519 is secp256k1.
            string proposedAlgorithm = algorithm ?? decoded.Type;
            if (proposedAlgorithm == "ed25519")
            {
                IXrplKeyPair edkp = EdKeyPair.From128Seed(decoded.Bytes);
                byte[] edMsg = Hash("This test message should verify.");
                string edSig = Sign(edMsg, edkp.Pk());
                if (Verify(edMsg, edSig, edkp.Id()) != true)
                {
                    throw new KeypairException("derived keypair did not generate verifiable signature");
                }
                return edkp;
            }
            IXrplKeyPair scpk = K256KeyGenerator.From128Seed(decoded.Bytes, validator ? -1 : (int)index);
            byte[] scpkMsg = Hash("This test message should verify.");
            string scpkSig = Sign(scpkMsg, scpk.Pk());
            if (Verify(scpkMsg, scpkSig, scpk.Id()) != true)
            {
                throw new KeypairException("derived keypair did not generate verifiable signature");
            }
            return scpk;
        }

        /// <summary> get seed algorithm from seed key </summary>
        /// <param name="key">seed key</param>
        /// <returns></returns>
        public static string GetAlgorithmFromKey(string key)
        {
            byte[] data = key.FromHex();
            return data.Length == 33 && data[0] == 0xED ? "ed25519" : "secp256k1";
        }

        /// <summary> Sing message </summary>
        /// <param name="message">byte array</param>
        /// <param name="privateKey">private key</param>
        /// <returns></returns>
        public static string Sign(byte[] message, string privateKey)
        {
            string algorithm = GetAlgorithmFromKey(privateKey);
            if (algorithm == "ed25519")
            {
                byte[] pk = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed(privateKey[2..66].FromHex());
                return EdKeyPair.Sign(message, pk).ToHex();
            }
            return K256KeyPair.Sign(message, privateKey.FromHex()).ToHex();
        }

        /// <summary>
        /// Signs <paramref name="message"/> with a private key held as bytes, so the caller can wipe it
        /// after use; produces the same signature as <see cref="Sign(byte[], string)"/>.
        /// </summary>
        /// <remarks>
        /// Every buffer this method allocates for key material, the Ed25519 expanded key included, is
        /// cleared before it returns. secp256k1 signs on NBitcoin.Secp256k1, a port of libsecp256k1:
        /// the scalar and the RFC 6979 nonce are value types, cleared once the signature is made,
        /// never a heap <c>BigInteger</c>. A secp256k1 scalar of zero is refused, so a buffer the
        /// caller has already cleared (33 zero bytes, whose 0x00 prefix reads as secp256k1) never
        /// signs. What stays outside the SDK's reach is the state the hash implementations keep,
        /// including the HMAC-SHA256 that RFC 6979 runs.
        /// </remarks>
        /// <param name="message">The message bytes; for secp256k1 the SHA-512Half of it is signed.</param>
        /// <param name="privateKey">
        /// The 33-byte XRPL form of the key: <c>0xED</c> followed by the 32-byte Ed25519 seed, or
        /// <c>0x00</c> followed by the 32-byte secp256k1 scalar.
        /// </param>
        /// <returns>The signature: 64 bytes for Ed25519, DER for secp256k1.</returns>
        /// <exception cref="ArgumentException">The key is not 33 bytes, has an unknown prefix, or is a secp256k1 scalar of zero or not below the curve order.</exception>
        public static byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> privateKey)
        {
            bool ed25519 = IsEd25519PrivateKey(privateKey);
            byte[] messageBytes = message.ToArray();
            if (ed25519)
            {
                byte[] seed = privateKey.Slice(1).ToArray();
                byte[] expanded = Array.Empty<byte>();
                try
                {
                    expanded = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed(seed);
                    return EdKeyPair.Sign(messageBytes, expanded);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(seed);
                    CryptographicOperations.ZeroMemory(expanded);
                }
            }

            return K256SecretKey.Sign(messageBytes, privateKey.Slice(1));
        }

        /// <summary>
        /// Derives the public key of a private key held as bytes.
        /// </summary>
        /// <param name="privateKey">The 33-byte XRPL form of the key, as <see cref="Sign(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> takes it.</param>
        /// <returns>The 33-byte public key: <c>0xED</c> and the Ed25519 key, or a compressed secp256k1 point.</returns>
        /// <exception cref="ArgumentException">The key is not 33 bytes, has an unknown prefix, or is a secp256k1 scalar of zero or not below the curve order.</exception>
        public static byte[] DerivePublicKey(ReadOnlySpan<byte> privateKey)
        {
            if (!IsEd25519PrivateKey(privateKey))
                return K256SecretKey.PublicKey(privateKey.Slice(1));

            byte[] keyBytes = privateKey.Slice(1).ToArray();
            try
            {
                return Ed25519PublicKey(keyBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }

        /// <summary>
        /// Derives an account's key pair from 16 bytes of entropy without encoding a seed string:
        /// the keys <see cref="DeriveKeypair"/> derives from the seed that
        /// <see cref="GenerateSeed"/> makes of the same entropy.
        /// </summary>
        /// <param name="entropy">The 16 bytes of seed entropy.</param>
        /// <param name="algorithm"><c>ed25519</c> or <c>secp256k1</c>.</param>
        /// <returns>
        /// The 33-byte private key in the XRPL form <see cref="Sign(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
        /// takes, and the 33-byte public key. The caller owns the private key array and should clear it.
        /// </returns>
        /// <exception cref="ArgumentException">The entropy is not 16 bytes, or the algorithm is unknown.</exception>
        public static (byte[] PrivateKey, byte[] PublicKey) DeriveKeyBytes(ReadOnlySpan<byte> entropy, string algorithm)
        {
            if (entropy.Length != 16)
                throw new ArgumentException("Seed entropy must be 16 bytes.", nameof(entropy));

            byte[] seed = entropy.ToArray();
            try
            {
                if (string.Equals(algorithm, "ed25519", StringComparison.Ordinal))
                {
                    byte[] edSecret = Sha512.Half(seed);
                    try
                    {
                        byte[] privateKey = new byte[33];
                        privateKey[0] = 0xED;
                        Buffer.BlockCopy(edSecret, 0, privateKey, 1, 32);
                        return (privateKey, Ed25519PublicKey(edSecret));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(edSecret);
                    }
                }

                if (string.Equals(algorithm, "secp256k1", StringComparison.Ordinal))
                {
                    // Account key 0 of the seed's family, as DeriveKeypair derives it for a non-validator.
                    byte[] privateKey = new byte[33];
                    byte[] publicKey = K256SecretKey.DeriveFamilyKey(seed, 0, privateKey.AsSpan(1));
                    return (privateKey, publicKey);
                }

                throw new ArgumentException($"Unknown algorithm '{algorithm}'; expected ed25519 or secp256k1.", nameof(algorithm));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(seed);
            }
        }

        private static bool IsEd25519PrivateKey(ReadOnlySpan<byte> privateKey)
        {
            if (privateKey.Length != 33)
                throw new ArgumentException("The private key must be 33 bytes: 0xED or 0x00 followed by 32 key bytes.", nameof(privateKey));
            return privateKey[0] switch
            {
                0xED => true,
                0x00 => false,
                _ => throw new ArgumentException("The private key must start with 0xED (Ed25519) or 0x00 (secp256k1).", nameof(privateKey)),
            };
        }

        private static byte[] Ed25519PublicKey(byte[] seed)
        {
            byte[] expanded = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed(seed);
            try
            {
                byte[] publicKey = new byte[33];
                publicKey[0] = 0xED;
                Buffer.BlockCopy(expanded, 32, publicKey, 1, 32);
                return publicKey;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expanded);
            }
        }

        /// <summary> Sing message </summary>
        /// <param name="HexMessage">Hex Message</param>
        /// <param name="privateKey">private key</param>
        /// <returns></returns>
        public static string Sign(string HexMessage, string privateKey) => Sign(HexMessage.FromHex(), privateKey);

        public static bool Verify(byte[] message, string signature, string publicKey)
        {
            string algorithm = GetAlgorithmFromKey(publicKey);
            return algorithm == "ed25519"
                ? EdKeyPair.Verify(signature.FromHex(), message, publicKey[2..66].FromHex())
                : K256KeyPair.Verify(signature.FromHex(), message, publicKey.FromHex());
        }

        public static bool Verify(string HexMessage, string signature, string publicKey) => Verify(HexMessage.FromHex(), signature, publicKey);

        public static string DeriveAddressFromBytes(byte[] publicKeyBytes)
            => XrplCodec.EncodeAccountID(Utils.HashUtils.PublicKeyHash(publicKeyBytes));

        public static string DeriveAddress(string publicKey)
            => XrplKeypairs.DeriveAddressFromBytes(publicKey.FromHex());

        /// <summary>
        /// Derives the public key of a private key given as hex: <c>ED</c> and 32 bytes for
        /// Ed25519, otherwise a secp256k1 scalar (the 33-byte <c>00</c>-prefixed form or the raw
        /// 32 bytes of a BIP-39 key).
        /// </summary>
        /// <param name="privateKey">The private key, hex.</param>
        /// <returns>The 33-byte public key, upper-case hex.</returns>
        /// <exception cref="FormatException"><paramref name="privateKey"/> is not hex.</exception>
        /// <exception cref="ArgumentException">A secp256k1 key is zero, not below the curve order, or longer than 32 significant bytes.</exception>
        public static string DerivePublicKeyFromPrivateKey(string privateKey)
        {
            byte[] keyBytes = privateKey.FromHex();

            // ED25519: "ED" prefix (33 bytes, first byte = 0xED)
            if (keyBytes.Length == 33 && keyBytes[0] == 0xED)
            {
                byte[] expanded = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed(keyBytes[1..]);
                return "ED" + expanded[32..64].ToHex();
            }

            // secp256k1, read as an unsigned big-endian scalar: the XRPL form with its "00" prefix
            // (33 bytes) and the raw 32 bytes of a BIP-39 key are the same number.
            return K256SecretKey.PublicKey(keyBytes).ToHex();
        }
    }
}