using System.Numerics;

//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-keypairs/src/secp256k1.ts

namespace Xrpl.Keypairs.K256
{
    /// <summary>secp256k1 curve constants.</summary>
    public class Secp256K1
    {
        private static readonly BigInteger GroupOrder = BigInteger.Parse(
            "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
            System.Globalization.NumberStyles.HexNumber);

        /// <summary>The order n of the curve's base point.</summary>
        public static BigInteger Order() => GroupOrder;
    }
}
