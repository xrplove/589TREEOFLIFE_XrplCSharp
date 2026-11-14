using System;
using System.Security.Cryptography;

//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-keypairs/src/Sha512.ts

namespace Xrpl.Keypairs.Utils
{
    /// <summary>
    /// Incremental SHA-512 with the truncations XRPL uses: SHA-512Half (the first 32 bytes) and
    /// SHA-512Quarter (the first 16). An instance holds a platform hash handle: dispose it.
    /// </summary>
    public class Sha512 : IDisposable
    {
        private readonly IncrementalHash _digest;

        /// <summary>Starts an empty hash.</summary>
        public Sha512()
        {
            _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        }

        /// <summary>Starts a hash over <paramref name="start"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="start"/> is null.</exception>
        public Sha512(byte[] start)
        {
            if (start == null)
                throw new ArgumentNullException(nameof(start));
            _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            Add(start);
        }

        /// <summary>Appends bytes.</summary>
        public Sha512 Add(byte[] bytes)
        {
            _digest.AppendData(bytes);
            return this;
        }

        /// <summary>Appends a 32-bit value, big-endian.</summary>
        public Sha512 AddU32(uint i)
        {
            Span<byte> bytes = stackalloc byte[4];
            bytes[0] = (byte)(i >> 24);
            bytes[1] = (byte)(i >> 16);
            bytes[2] = (byte)(i >> 8);
            bytes[3] = (byte)i;
            _digest.AppendData(bytes);
            return this;
        }

        // On the Ed25519 path the truncated hash is the secret key, so the full 64-byte digest
        // stays on the stack and is cleared; only the requested prefix reaches the heap.
        private byte[] FinishTaking(int size)
        {
            Span<byte> finished = stackalloc byte[64];
            try
            {
                _digest.TryGetHashAndReset(finished, out _);
                return finished.Slice(0, size).ToArray();
            }
            finally
            {
                finished.Clear();
            }
        }

        /// <summary>Returns the 64-byte hash and resets the instance to empty.</summary>
        public byte[] Finish() => _digest.GetHashAndReset();

        /// <summary>Returns the first 16 bytes of the hash and resets the instance.</summary>
        public byte[] Finish128()
        {
            return FinishTaking(16);
        }

        /// <summary>Returns the first 32 bytes of the hash and resets the instance.</summary>
        public byte[] Finish256()
        {
            return FinishTaking(32);
        }

        /// <summary>Releases the hash handle.</summary>
        public void Dispose() => _digest.Dispose();

        /// <summary>SHA-512Half: the first 32 bytes of the SHA-512 of <paramref name="input"/>.</summary>
        public static byte[] Half(byte[] input) => Truncated(input, 32);

        /// <summary>SHA-512Quarter: the first 16 bytes of the SHA-512 of <paramref name="input"/>.</summary>
        public static byte[] Quarter(byte[] input) => Truncated(input, 16);

        private static byte[] Truncated(byte[] input, int size)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            using SHA512 sha512 = SHA512.Create();
            Span<byte> hash = stackalloc byte[64];
            try
            {
                sha512.TryComputeHash(input, hash, out _);
                return hash.Slice(0, size).ToArray();
            }
            finally
            {
                hash.Clear();
            }
        }
    }
}
