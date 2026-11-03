using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Common;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// The asset-identity rules the money path depends on. Each case is a way the ledger lets two
    /// different assets look alike, or the same asset look different.
    /// </summary>
    [TestClass]
    public class TestUAssetIdentity
    {
        private const string UsdIssuer = "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH";
        private const string EurIssuer = "rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De";

        [TestMethod]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow("XRP", false)]
        [DataRow("USD", false)]
        public void TestUIsHexCurrencyCode_LengthGuard(string code, bool expected)
        {
            Assert.AreEqual(expected, AssetIdentity.IsHexCurrencyCode(code));
        }

        [TestMethod]
        public void TestUIsHexCurrencyCode_RejectsNonHexChars()
        {
            string notHex = "000000000000000000000000005850?4D0000000";
            Assert.AreEqual(40, notHex.Length);
            Assert.IsFalse(AssetIdentity.IsHexCurrencyCode(notHex));
            Assert.IsFalse(AssetIdentity.IsHexCurrencyCode(new string('Z', 40)));
        }

        [TestMethod]
        public void TestUIsHexCurrencyCode_AcceptsFortyHexChars_UpperAndLower()
        {
            Assert.IsTrue(AssetIdentity.IsHexCurrencyCode(new string('A', 40)));
            Assert.IsTrue(AssetIdentity.IsHexCurrencyCode(new string('a', 40)));
            Assert.IsTrue(AssetIdentity.IsHexCurrencyCode(new string('0', 40)));
        }

        [TestMethod]
        public void TestUIsHexCurrencyCode_RejectsWrongLengthAndTrailingNewline()
        {
            Assert.IsFalse(AssetIdentity.IsHexCurrencyCode(new string('A', 39)));
            Assert.IsFalse(AssetIdentity.IsHexCurrencyCode(new string('A', 41)));
            Assert.IsFalse(AssetIdentity.IsHexCurrencyCode(new string('A', 39) + "\n"));
        }

        [TestMethod]
        public void TestUCurrencyEquals_ThreeCharCodes_AreCaseSensitive()
        {
            Assert.IsFalse(AssetIdentity.CurrencyEquals("XPM", "xpm"));
            Assert.IsTrue(AssetIdentity.CurrencyEquals("XPM", "XPM"));
        }

        [TestMethod]
        public void TestUCurrencyEquals_HexCodes_AreCaseInsensitive()
        {
            // Different sources render byte-identical non-standard codes in different casing.
            Assert.IsTrue(AssetIdentity.CurrencyEquals(
                "58504D00000000000000000000000000000000AB",
                "58504d00000000000000000000000000000000ab"));
        }

        [TestMethod]
        public void TestUCurrencyEquals_FortyCharNonHex_IsNotTreatedAsHex()
        {
            string notHex = new string('Z', 40);

            Assert.IsFalse(AssetIdentity.CurrencyEquals(notHex, notHex.ToLowerInvariant()));
            Assert.IsTrue(AssetIdentity.CurrencyEquals(notHex, notHex));
        }

        [TestMethod]
        public void TestUCurrencyEquals_HexVsThreeChar_NeverEqual()
        {
            Assert.IsFalse(AssetIdentity.CurrencyEquals(new string('4', 40), "XRP"));
        }

        [TestMethod]
        public void TestUCurrencyEquals_NullHandling()
        {
            Assert.IsTrue(AssetIdentity.CurrencyEquals(null, null));
            Assert.IsFalse(AssetIdentity.CurrencyEquals(null, "XRP"));
            Assert.IsFalse(AssetIdentity.CurrencyEquals("XRP", null));
        }

        [TestMethod]
        public void TestUIssuerEquals_IsCaseSensitive()
        {
            // Base58 has no case-insensitive form; two addresses differing in case are two accounts.
            Assert.IsTrue(AssetIdentity.IssuerEquals(UsdIssuer, UsdIssuer));
            Assert.IsFalse(AssetIdentity.IssuerEquals(UsdIssuer, UsdIssuer.ToLowerInvariant()));
        }

        [TestMethod]
        public void TestUAssetEquals_SameCodeSameIssuer_True()
        {
            Assert.IsTrue(AssetIdentity.AssetEquals("USD", UsdIssuer, "USD", UsdIssuer));
        }

        [TestMethod]
        public void TestUAssetEquals_SameCodeDifferentIssuer_False()
        {
            Assert.IsFalse(AssetIdentity.AssetEquals("USD", UsdIssuer, "USD", EurIssuer));
        }

        [TestMethod]
        public void TestUAssetEquals_SameIssuerDifferentCaseCode_False()
        {
            Assert.IsFalse(AssetIdentity.AssetEquals("XPM", UsdIssuer, "xpm", UsdIssuer));
        }

        [TestMethod]
        public void TestUAssetEquals_NullEmptyAndBlankIssuers_AreTheSameAsset()
        {
            Assert.IsTrue(AssetIdentity.AssetEquals("XRP", null, "XRP", ""));
            Assert.IsTrue(AssetIdentity.AssetEquals("XRP", "   ", "XRP", null));
        }

        [TestMethod]
        public void TestUAssetEquals_HexCurrency_DifferentCasing_SameIssuer_True()
        {
            Assert.IsTrue(AssetIdentity.AssetEquals(
                "58504D00000000000000000000000000000000AB", UsdIssuer,
                "58504d00000000000000000000000000000000ab", UsdIssuer));
        }

        [TestMethod]
        [DataRow("XRP", true)]
        [DataRow("xrp", false)]
        [DataRow("Xrp", false)]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow("USD", false)]
        public void TestUIsXrp_CodeOnly_IsCaseSensitive(string code, bool expected)
        {
            // A lowercase "xrp" IOU is legal; only the exact "XRP" is rejected by the ledger (temBAD_CURRENCY).
            Assert.AreEqual(expected, AssetIdentity.IsXrp(code));
        }

        [TestMethod]
        public void TestUIsXrp_WithIssuer_RequiresEmptyIssuer()
        {
            Assert.IsTrue(AssetIdentity.IsXrp("XRP", null));
            Assert.IsTrue(AssetIdentity.IsXrp("XRP", ""));
            Assert.IsTrue(AssetIdentity.IsXrp("XRP", "   "));
            Assert.IsFalse(AssetIdentity.IsXrp("XRP", UsdIssuer));
            Assert.IsFalse(AssetIdentity.IsXrp("xrp", null));
        }
    }
}
