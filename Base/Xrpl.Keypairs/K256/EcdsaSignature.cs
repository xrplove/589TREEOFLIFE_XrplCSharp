using System;
using System.Numerics;

//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-keypairs/src/secp256k1.ts

namespace Xrpl.Keypairs.K256
{
    /// <summary>An ECDSA signature and its DER encoding.</summary>
    public class EcdsaSignature
    {
        /// <summary>
        /// The two components of the signature, non-negative. </summary>
        public BigInteger R, S;

        /// <summary>
        /// Constructs a signature with the given components. </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="r"/> or <paramref name="s"/> is negative.</exception>
        public EcdsaSignature(BigInteger r, BigInteger s)
        {
            if (r.Sign < 0)
                throw new ArgumentOutOfRangeException(nameof(r), "A signature component cannot be negative.");
            if (s.Sign < 0)
                throw new ArgumentOutOfRangeException(nameof(s), "A signature component cannot be negative.");
            this.R = r;
            this.S = s;
        }

        /// <summary>
        /// Whether <paramref name="sig"/> is a canonical DER signature with a low S, as rippled
        /// requires under <c>tfFullyCanonicalSig</c>.
        /// </summary>
        public static bool IsStrictlyCanonical(byte[] sig) => CheckIsCanonical(sig, true);

        /// <summary>
        /// Whether <paramref name="sig"/> is a canonical DER signature: minimal positive R and S
        /// below the curve order, and with <paramref name="strict"/> also S at most n/2.
        /// </summary>
        public static bool CheckIsCanonical(byte[] sig, bool strict)
        {
            // Make sure signature is canonical
            // To protect against signature morphing attacks

            // Signature should be:
            // <30> <len> [ <02> <lenR> <R> ] [ <02> <lenS> <S> ]
            // where
            // 6 <= len <= 70
            // 1 <= lenR <= 33
            // 1 <= lenS <= 33

            int sigLen = sig.Length;

            if (sigLen is < 8 or > 72)
            {
                return false;
            }

            if ((sig[0] != 0x30) || (sig[1] != (sigLen - 2)))
            {
                return false;
            }

            // Find R and check its length
            int rPos = 4, rLen = sig[rPos - 1];

            if (rLen is < 1 or > 33 || ((rLen + 7) > sigLen))
            {
                return false;
            }

            // Find S and check its length
            int sPos = rLen + 6, sLen = sig[sPos - 1];
            if (sLen is < 1 or > 33 || ((rLen + sLen + 6) != sigLen))
            {
                return false;
            }

            if ((sig[rPos - 2] != 0x02) || (sig[sPos - 2] != 0x02))
            {
                return false; // R or S have wrong type
            }

            if ((sig[rPos] & 0x80) != 0)
            {
                return false; // R is negative
            }

            if ((sig[rPos] == 0) && rLen == 1)
            {
                return false; // R is zero
            }

            if ((sig[rPos] == 0) && ((sig[rPos + 1] & 0x80) == 0))
            {
                return false; // R is padded
            }

            if ((sig[sPos] & 0x80) != 0)
            {
                return false; // S is negative
            }

            if ((sig[sPos] == 0) && sLen == 1)
            {
                return false; // S is zero
            }

            if ((sig[sPos] == 0) && ((sig[sPos + 1] & 0x80) == 0))
            {
                return false; // S is padded
            }

            BigInteger r = Unsigned(sig.AsSpan(rPos, rLen));
            BigInteger s = Unsigned(sig.AsSpan(sPos, sLen));

            BigInteger order = Secp256K1.Order();

            if (r >= order || s >= order)
            {
                return false; // R or S greater than modulus
            }

            return !strict || order - s >= s;
        }

        /// <summary>
        /// The DER encoding of the signature: a SEQUENCE of two minimal INTEGERs, as OpenSSL and
        /// rippled read it.
        /// </summary>
        public byte[] EncodeToDer()
        {
            byte[] r = R.ToByteArray(isUnsigned: false, isBigEndian: true);
            byte[] s = S.ToByteArray(isUnsigned: false, isBigEndian: true);
            int contentLength = 1 + LengthOfLength(r.Length) + r.Length + 1 + LengthOfLength(s.Length) + s.Length;
            int lengthBytes = LengthOfLength(contentLength);

            byte[] der = new byte[1 + lengthBytes + contentLength];
            int position = 0;
            der[position++] = 0x30;
            position = WriteLength(der, position, contentLength);
            position = WriteInteger(der, position, r);
            WriteInteger(der, position, s);
            return der;
        }

        /// <summary>
        /// Decodes a DER signature; returns null when the bytes are not exactly one DER SEQUENCE of
        /// two INTEGERs in minimal encoding.
        /// </summary>
        /// <remarks>
        /// Strict DER, as rippled requires: trailing bytes or a non-minimal length or integer would
        /// otherwise decode to a signature that verifies. An INTEGER whose first byte has the high
        /// bit set is read as unsigned: OpenSSL reads these values that way, though DER says they
        /// are negative (see http://r6.ca/blog/20111119T211504Z.html).
        /// </remarks>
        public static EcdsaSignature DecodeFromDer(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            ReadOnlySpan<byte> der = bytes;
            int position = 0;
            if (der.Length < 1 || der[position++] != 0x30)
                return null;
            if (!TryReadLength(der, ref position, out int sequenceLength) || sequenceLength != der.Length - position)
                return null;
            if (!TryReadInteger(der, ref position, out BigInteger r) || !TryReadInteger(der, ref position, out BigInteger s))
                return null;
            if (position != der.Length)
                return null;
            return new EcdsaSignature(r, s);
        }

        private static BigInteger Unsigned(ReadOnlySpan<byte> bigEndian) =>
            new BigInteger(bigEndian, isUnsigned: true, isBigEndian: true);

        private static bool TryReadInteger(ReadOnlySpan<byte> der, ref int position, out BigInteger value)
        {
            value = BigInteger.Zero;
            if (position >= der.Length || der[position++] != 0x02)
                return false;
            if (!TryReadLength(der, ref position, out int length) || length < 1 || length > der.Length - position)
                return false;

            ReadOnlySpan<byte> content = der.Slice(position, length);
            if (content.Length > 1
                && ((content[0] == 0x00 && (content[1] & 0x80) == 0)
                    || (content[0] == 0xFF && (content[1] & 0x80) != 0)))
            {
                return false;
            }
            position += length;
            value = Unsigned(content);
            return true;
        }

        // Definite form only, in the fewest bytes: DER allows no other.
        private static bool TryReadLength(ReadOnlySpan<byte> der, ref int position, out int length)
        {
            length = 0;
            if (position >= der.Length)
                return false;
            byte first = der[position++];
            if (first < 0x80)
            {
                length = first;
                return true;
            }

            // Up to four length bytes, as EncodeToDer writes; a fourth byte of 0x80 or more would
            // overflow int.
            int count = first & 0x7F;
            if (count == 0 || count > 4 || count > der.Length - position || der[position] == 0
                || (count == 4 && der[position] >= 0x80))
                return false;
            for (int i = 0; i < count; i++)
                length = (length << 8) | der[position++];
            return length >= 0x80;
        }

        private static int LengthOfLength(int length) =>
            length < 0x80 ? 1 : length <= 0xFF ? 2 : length <= 0xFFFF ? 3 : length <= 0xFFFFFF ? 4 : 5;

        private static int WriteLength(byte[] der, int position, int length)
        {
            int lengthBytes = LengthOfLength(length);
            if (lengthBytes == 1)
            {
                der[position++] = (byte)length;
                return position;
            }
            der[position++] = (byte)(0x80 | (lengthBytes - 1));
            for (int shift = 8 * (lengthBytes - 2); shift >= 0; shift -= 8)
                der[position++] = (byte)(length >> shift);
            return position;
        }

        private static int WriteInteger(byte[] der, int position, byte[] value)
        {
            der[position++] = 0x02;
            position = WriteLength(der, position, value.Length);
            Buffer.BlockCopy(value, 0, der, position, value.Length);
            return position + value.Length;
        }
    }
}
