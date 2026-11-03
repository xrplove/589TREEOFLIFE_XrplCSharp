

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/hashes.ts

using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Utils.Hashes;

namespace XrplTests.Xrpl.Utils
{
    [TestClass]
    public class TestUHashes
    {
        [TestMethod]
        public void TestLedgerSpaceHex()
        {
            var expectedEntryHex = "0078";
            var actualEntryHex = Hashes.LedgerSpaceHex(LedgerSpace.Paychan);

            Assert.AreEqual(expectedEntryHex, actualEntryHex);
        }

        [TestMethod]
        public void TestAddressToHex()
        {
            var account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
            var expectedEntryHex = "B5F762798A53D543A014CAF8B297CFF8F2F937E8";
            var actualEntryHex = Hashes.AddressToHex(account);

            Assert.AreEqual(expectedEntryHex, actualEntryHex);
        }

        [TestMethod]
        public void TestAccountRootEntryHash()
        {
            var account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
            var expectedEntryHash = "2B6AC232AA4C4BE41BF49D2459FA4A0347E1B543A4C92FCEE0821C0201E2E9A8";
            var actualEntryHash = Hashes.HashAccountRoot(account);

            Assert.AreEqual(expectedEntryHash, actualEntryHash);
        }

        // Each pair has exactly one AccountID with the high bit set (B5F7... / 7588..., 550F... / 82F1...),
        // so the low/high ordering must compare the IDs as unsigned bytes.
        [TestMethod]
        [DataRow("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", "rB5TihdPbKgMrkFqrqUC3yLdE8hhv4BdeY", "USD", "C683B5BB928F025F1E860D9D69D6C554C2202DE0D45877ADB3077DA4CB9E125C")]
        [DataRow("r3kmLJN5D28dHuH8vZNUZpMC43pEHpaocV", "rUAMuQTfVhbfqUDuro7zzy4jj4Wq57MPTj", "UAM", "AE9ADDC584358E5847ADFC971834E471436FC3E9DE6EA1773DF49F419DC0F65E")]
        public void TestRippleStateEntryHash(string account1, string account2, string currency, string expectedEntryHash)
        {
            Assert.AreEqual(expectedEntryHash, Hashes.HashTrustline(account1, account2, currency));
            Assert.AreEqual(expectedEntryHash, Hashes.HashTrustline(account2, account1, currency));
        }

        [TestMethod]
        public void TestRippleStateEntryHashHexCurrency()
        {
            var account1 = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
            var account2 = "rB5TihdPbKgMrkFqrqUC3yLdE8hhv4BdeY";
            var usdHex = "0000000000000000000000005553440000000000";
            var expectedEntryHash = "C683B5BB928F025F1E860D9D69D6C554C2202DE0D45877ADB3077DA4CB9E125C";

            Assert.AreEqual(expectedEntryHash, Hashes.HashTrustline(account1, account2, usdHex));
            Assert.AreEqual(expectedEntryHash, Hashes.HashTrustline(account2, account1, usdHex));
        }

        [TestMethod]
        [DataRow("XRP")]
        [DataRow("0000000000000000000000000000000000000000")]
        public void TestRippleStateEntryHashRejectsXrp(string currency)
        {
            Assert.ThrowsExactly<System.ArgumentException>(
                () => Hashes.HashTrustline("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", "rB5TihdPbKgMrkFqrqUC3yLdE8hhv4BdeY", currency));
        }

        [TestMethod]
        [DataRow("r32UufnaCGL82HubijgJGDmdE5hac7ZvLw", 137u, "03F0AED09DEEE74CEF85CD57A0429D6113507CF759C597BABB4ADB752F734CE3")]
        [DataRow("rLewiS7bCme2EVyU2w3fSbPVbivAJ6FJtd", 1670u, "44B2B312F38C16464F76568BE33F71BC7206042AE73C2E5201A3B78598C59F01")]
        [DataRow("rGw5dXZ7f4bmCCPawx2BQc2EYZDsgGzR27", 1695u, "1537132147D764F879F23F5DAB4094A7BCCE7D548906C0CC9A62131308F2B455")]
        public void TestOfferEntryHash(string account, uint sequence, string expectedEntryHash)
        {
            Assert.AreEqual(expectedEntryHash, Hashes.HashOfferId(account, sequence));
        }

        [TestMethod]
        public void TestSignerListEntryHash()
        {
            var account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
            var expectedEntryHash = "778365D5180F5DF3016817D1F318527AD7410D83F8636CF48C43E8AF72AB49BF";
            Assert.AreEqual(expectedEntryHash, Hashes.HashSignerListId(account));
        }

        [TestMethod]
        public void TestEscrowEntryHash()
        {
            var account = "rDx69ebzbowuqztksVDmZXjizTd12BVr4x";
            var sequence = 84;
            var expectedEntryHash = "61E8E8ED53FA2CEBE192B23897071E9A75217BF5A410E9CB5B45AAB7AECA567A";
            Assert.AreEqual(expectedEntryHash, Hashes.HashEscrow(account, sequence));
        }

        [TestMethod]
        public void TestPaymentChannelEntryHash()
        {
            var account = "rDx69ebzbowuqztksVDmZXjizTd12BVr4x";
            var dstAccount = "rLFtVprxUEfsH54eCWKsZrEQzMDsx1wqso";
            var sequence = 82;
            var expectedEntryHash = "E35708503B3C3143FB522D749AAFCC296E8060F0FB371A9A56FAE0B1ED127366";
            var actualEntryHash = Hashes.HashPaymentChannel(account, dstAccount, sequence);
            Assert.AreEqual(expectedEntryHash, actualEntryHash);
        }

        // Middle and max are real mainnet pages listed by account_objects (type NFTokenPage) for this account at validated
        // ledger 108074793. Min is NOT from the ledger: it is constructed per keylet::nftpage_min(owner), AccountID followed
        // by 96 zero bits, because rippled does not normally create a page at that key.
        private const string NftPageOwner = "rpbjkoncKiv1LkPWShzZksqYPzKXmUhTW7";
        private const string NftPageOwnerAccountIdHex = "1189F5687DBB7516339D6CCB5593D96622AD82DF";

        [TestMethod]
        [DataRow("1189F5687DBB7516339D6CCB5593D96622AD82DF000000000000000000000000")]
        [DataRow("1189F5687DBB7516339D6CCB5593D96622AD82DF22AD82DF6A3EBC6500001CB6")]
        [DataRow("1189F5687DBB7516339D6CCB5593D96622AD82DFFFFFFFFFFFFFFFFFFFFFFFFF")]
        public void TestGetNFTokenPageOwner(string pageIndex)
        {
            Assert.AreEqual(NftPageOwner, Hashes.GetNFTokenPageOwner(pageIndex));
        }

        [TestMethod]
        public void TestNFTokenPageOwnerVectorsUseTheOwnerAccountId()
        {
            Assert.AreEqual(NftPageOwnerAccountIdHex, Hashes.AddressToHex(NftPageOwner));
        }

        [TestMethod]
        public void TestGetNFTokenPageOwnerAcceptsLowerCase()
        {
            string lower = "1189f5687dbb7516339d6ccb5593d96622ad82df22ad82df6a3ebc6500001cb6";
            Assert.AreEqual(NftPageOwner, Hashes.GetNFTokenPageOwner(lower));
        }

        [TestMethod]
        public void TestGetNFTokenPageOwnerRoundTripsMinAndMaxKeylets()
        {
            string account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
            string accountIdHex = Hashes.AddressToHex(account);

            Assert.AreEqual(account, Hashes.GetNFTokenPageOwner(accountIdHex + new string('0', 24)));
            Assert.AreEqual(account, Hashes.GetNFTokenPageOwner(accountIdHex + new string('F', 24)));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("1189F5687DBB7516339D6CCB5593D96622AD82DF")]
        [DataRow("1189F5687DBB7516339D6CCB5593D96622AD82DFFFFFFFFFFFFFFFFFFFFFFFFFF")]
        [DataRow("1189F5687DBB7516339D6CCB5593D96622AD82DFFFFFFFFFFFFFFFFFFFFFFFFG")]
        [DataRow("0x89F5687DBB7516339D6CCB5593D96622AD82DFFFFFFFFFFFFFFFFFFFFFFFFF")]
        [DataRow(" 189F5687DBB7516339D6CCB5593D96622AD82DFFFFFFFFFFFFFFFFFFFFFFFFF")]
        [DataRow("189F5687DBB7516339D6CCB5593D96622AD82DFFFFFFFFFFFFFFFFFFFFFFFFF\n")]
        public void TestGetNFTokenPageOwnerRejectsMalformedIndex(string pageIndex)
        {
            Assert.ThrowsExactly<System.ArgumentException>(() => Hashes.GetNFTokenPageOwner(pageIndex));
        }

        [TestMethod]
        public void TestSequence()
        {
            var expected = "00000052";
            var sequence = 82;
            var BYTE_LENGTH = 4;
            var actual = sequence.ToString("X").PadLeft(BYTE_LENGTH * 2, '0');
            Assert.AreEqual(expected, actual);
        }
    }
}

