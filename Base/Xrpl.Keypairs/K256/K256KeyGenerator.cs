using System;

namespace Xrpl.Keypairs.K256
{
    /// <summary>
    /// Derives secp256k1 key families from a seed, as rippled does. Keys are 32-byte big-endian
    /// scalars and public keys 33-byte compressed points. A null array throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    public class K256KeyGenerator
    {
        // See https://wiki.ripple.com/Account_Family
        /// <summary>Derives key <paramref name="keyIndex"/> of the seed's family; -1 is the root key a validator uses.</summary>
        /// <param name="seedBytes">The seed bytes, 16 for an XRPL seed.</param>
        /// <param name="keyIndex">The account key index, or -1 for the root key.</param>
        public static K256KeyPair From128Seed(byte[] seedBytes, int keyIndex)
        {
            if (seedBytes == null)
                throw new ArgumentNullException(nameof(seedBytes));
            byte[] privateKey = new byte[32];
            try
            {
                K256SecretKey.DeriveFamilyKey(seedBytes, keyIndex, privateKey);
                return new K256KeyPair(privateKey, keyIndex == -1);
            }
            finally
            {
                Array.Clear(privateKey, 0, privateKey.Length);
            }
        }

        /// <summary>The private generator (root private key) of a seed: a 32-byte scalar.</summary>
        public static byte[] ComputePrivateGen(byte[] seedBytes)
        {
            return ComputeScalar(seedBytes, null);
        }

        /// <summary>Account key <paramref name="accountNumber"/> of a family: a 32-byte scalar.</summary>
        /// <param name="privateGen">The 32-byte private generator.</param>
        /// <param name="accountNumber">The account key index.</param>
        public static byte[] ComputeSecretKey(byte[] privateGen, uint accountNumber)
        {
            if (privateGen == null)
                throw new ArgumentNullException(nameof(privateGen));
            byte[] publicGen = ComputePublicGenerator(privateGen);
            byte[] tweak = ComputeScalar(publicGen, accountNumber);
            try
            {
                return K256SecretKey.AddScalar(privateGen, tweak);
            }
            finally
            {
                Array.Clear(tweak, 0, tweak.Length);
            }
        }

        /// <summary>
        /// The public generator (public root key) of a private generator: a 33-byte compressed point.
        /// </summary>
        /// <param name="privateGen"> secret scalar</param>
        public static byte[] ComputePublicGenerator(byte[] privateGen)
        {
            return ComputePublicKey(privateGen);
        }

        /// <summary>
        /// The public key of account key <paramref name="accountNumber"/>, derived from the public
        /// generator alone: public generator + scalar·G.
        /// </summary>
        /// <returns>The 33-byte compressed public key.</returns>
        public static byte[] ComputePublicKey(byte[] publicGenBytes, uint accountNumber)
        {
            if (publicGenBytes == null)
                throw new ArgumentNullException(nameof(publicGenBytes));
            byte[] scalar = ComputeScalar(publicGenBytes, accountNumber);
            return K256SecretKey.AddToPublicKey(publicGenBytes, scalar);
        }

        /// <summary>
        /// The first SHA-512Half of <paramref name="seedBytes"/> || [<paramref name="discriminator"/>] || i,
        /// for i from 0, that lies in [1, n-1].
        /// </summary>
        /// <param name="seedBytes"> - a bytes sequence of arbitrary length which will be hashed </param>
        /// <param name="discriminator"> - nullable optional uint32 to hash </param>
        /// <returns>A 32-byte scalar suitable as a private key.</returns>
        public static byte[] ComputeScalar(byte[] seedBytes, uint? discriminator)
        {
            if (seedBytes == null)
                throw new ArgumentNullException(nameof(seedBytes));
            return K256SecretKey.ComputeScalar(seedBytes, discriminator);
        }

        /// <summary> Compute Public Key </summary>
        /// <param name="secretKey">The 32-byte private key.</param>
        /// <returns>The 33-byte compressed public key.</returns>
        public static byte[] ComputePublicKey(byte[] secretKey)
        {
            if (secretKey == null)
                throw new ArgumentNullException(nameof(secretKey));
            return K256SecretKey.PublicKey(secretKey);
        }
    }
}
