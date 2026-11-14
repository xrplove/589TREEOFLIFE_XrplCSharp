using System;

using static Xrpl.AddressCodec.Utils;

namespace Xrpl.Keypairs.K256
{
    /// <summary>A secp256k1 key pair whose private key is a 32-byte scalar.</summary>
    public class K256KeyPair : IXrplKeyPair
    {
        private readonly byte[] _privKey;
        private bool _isNodeKey;

        /// <summary>Wraps a private key.</summary>
        /// <param name="priv">
        /// The scalar, unsigned big-endian: 32 bytes, fewer when leading zeros were dropped, or more
        /// with leading zeros (the 33-byte XRPL form with the 00 prefix).
        /// </param>
        /// <param name="nodeKey">Whether this is the root key of a family, as a validator uses.</param>
        /// <exception cref="ArgumentNullException"><paramref name="priv"/> is null.</exception>
        /// <exception cref="ArgumentException">The key is zero or not below the curve order.</exception>
        public K256KeyPair(byte[] priv, bool nodeKey = false)
        {
            if (priv == null)
                throw new ArgumentNullException(nameof(priv));
            _privKey = K256SecretKey.ToScalar(priv);
            _isNodeKey = nodeKey;
        }

        /// <summary>The 33-byte compressed public key, as upper-case hex.</summary>
        public string Id()
        {
            return K256SecretKey.PublicKey(_privKey).ToHex();
        }

        /// <summary>The private key in the XRPL form: 00 followed by the 32-byte scalar, as upper-case hex.</summary>
        public string Pk()
        {
            return $"00{_privKey.ToHex()}";
        }

        /// <summary>Signs the SHA-512Half of <paramref name="message"/>, with a canonical (low) S.</summary>
        /// <param name="message">The message bytes.</param>
        /// <param name="privateKey">
        /// The private key, unsigned big-endian: the 33-byte form with the 00 prefix or the raw 32 bytes.
        /// It is read as unsigned, so a raw key whose first byte is 0x80 or above is not taken as negative.
        /// </param>
        /// <returns>The DER-encoded signature (RFC 6979 nonce).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="privateKey"/> is null.</exception>
        /// <exception cref="ArgumentException">The key is zero or not below the curve order.</exception>
        public static byte[] Sign(byte[] message, byte[] privateKey)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (privateKey == null)
                throw new ArgumentNullException(nameof(privateKey));
            return K256SecretKey.Sign(message, privateKey);
        }

        /// <summary>
        /// Verifies a DER signature over the SHA-512Half of <paramref name="message"/>. A signature
        /// with a high S verifies; whether it is canonical is <see cref="EcdsaSignature.IsStrictlyCanonical"/>.
        /// </summary>
        /// <param name="signature">The DER-encoded signature; anything else does not verify.</param>
        /// <param name="message">The message bytes.</param>
        /// <param name="publicKey">The public key, compressed (33 bytes) or uncompressed (65 bytes).</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="publicKey"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="publicKey"/> is not a point on the curve.</exception>
        public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (publicKey == null)
                throw new ArgumentNullException(nameof(publicKey));
            return K256SecretKey.Verify(signature, message, publicKey);
        }
    }
}
