using System;
using System.Text;
using Xrpl.Keypairs.Utils;
using System.Globalization;

namespace Xrpl.Keypairs
{
    internal static class ExtensionHelpers
    {
        internal static byte[] Sha512HashHalf(this byte[] input)
        {
            return Sha512.Half(input: input);
        }
    }
}