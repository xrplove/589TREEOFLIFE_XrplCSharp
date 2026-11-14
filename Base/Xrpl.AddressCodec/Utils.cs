using System;

using System.Linq;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-address-codec/src/utils.ts

namespace Xrpl.AddressCodec
{
    public static class Utils
    {
        private const string HexDigits = "0123456789ABCDEF";

        /// <summary>
        /// Encodes bytes as upper-case hex, two digits per byte.
        /// </summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>The hex string; empty for an empty array.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
        public static string ToHex(this byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[2 * i] = HexDigits[bytes[i] >> 4];
                chars[2 * i + 1] = HexDigits[bytes[i] & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>
        /// Decodes a hex string, either case. Spaces, tabs, carriage returns and line feeds between
        /// digits are skipped, as the BouncyCastle decoder this replaces skipped them.
        /// </summary>
        /// <param name="hex">The hex string.</param>
        /// <returns>The decoded bytes; empty for an empty string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="hex"/> is null.</exception>
        /// <exception cref="FormatException"><paramref name="hex"/> has a character that is not a hex digit or skipped whitespace, or an odd number of digits.</exception>
        public static byte[] FromHex(this string hex)
        {
            if (hex == null)
                throw new ArgumentNullException(nameof(hex));

            // Private keys and seeds are decoded here: the message names a position, never the
            // input or any character of it, so a malformed key does not end up in a log.
            int digits = 0;
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                if (HexValue(c) >= 0)
                    digits++;
                else if (!IsSkipped(c))
                    throw new FormatException($"Not a hex string: the character at index {i} is not a hex digit.");
            }
            if ((digits & 1) != 0)
                throw new FormatException($"Not a hex string: {digits} hex digits, an odd number.");

            byte[] bytes = new byte[digits / 2];
            int high = -1;
            int index = 0;
            foreach (char c in hex)
            {
                int value = HexValue(c);
                if (value < 0)
                    continue;
                if (high < 0)
                {
                    high = value;
                }
                else
                {
                    bytes[index++] = (byte)((high << 4) | value);
                    high = -1;
                }
            }
            return bytes;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            return -1;
        }

        private static bool IsSkipped(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

        /// <summary>
        /// combine bytes arrays to single array
        /// </summary>
        /// <param name="arrays">bytes arrays</param>
        /// <returns></returns>
        public static byte[] Combine(params byte[][] arrays)
        {
            var rv = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (byte[] array in arrays)
            {
                System.Buffer.BlockCopy(array, 0, rv, offset, array.Length);
                offset += array.Length;
            }
            return rv;
        }
    }
}
