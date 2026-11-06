using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// A TrustSet built in code starts with tfSetNoRipple; one read from JSON has the flags the JSON gave it and no
    /// others. rippled treats a missing Flags as no flags, and on a line it creates only tfSetNoRipple sets NoRipple on
    /// the sender's side, so the two forms are different transactions.
    /// </summary>
    [TestClass]
    public class TestUTrustSetFlags
    {
        private const string Account = "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo";
        private const string Issuer = "rcXY84C4g14iFp6taFXjjQGVeHqSCh9RX";
        private const uint NoRipple = (uint)TrustSetFlags.tfSetNoRipple;
        private const uint FullyCanonicalSig = 0x80000000;

        private static JsonObject Body(string limit = "0", uint? flags = null)
        {
            JsonObject body = new JsonObject
            {
                ["TransactionType"] = "TrustSet",
                ["Account"] = Account,
                ["Fee"] = "12",
                ["Sequence"] = 7L,
                ["SigningPubKey"] = "",
                ["LimitAmount"] = new JsonObject { ["currency"] = "USD", ["issuer"] = Issuer, ["value"] = limit },
            };

            if (flags.HasValue)
            {
                body["Flags"] = flags.Value;
            }

            return body;
        }

        private static ITrustSet Parse(JsonObject body) =>
            (ITrustSet)JsonSerializer.Deserialize<ITransactionRequest>(body.ToJsonString(), XrplJsonOptions.Default);

        private static JsonObject Written(TrustSet transaction) =>
            JsonNode.Parse(JsonSerializer.Serialize(transaction, transaction.GetType(), XrplJsonOptions.Default))!.AsObject();

        private static TrustSet Built() => new TrustSet
        {
            Account = Account,
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = Issuer, Value = "100" },
        };

        [TestMethod]
        public void Parsed_WithoutFlags_HasNoFlags()
        {
            Assert.IsNull(Parse(Body()).Flags);
        }

        [TestMethod]
        public void Parsed_AsConcreteType_WithoutFlags_HasNoFlags()
        {
            TrustSet parsed = JsonSerializer.Deserialize<TrustSet>(Body().ToJsonString(), XrplJsonOptions.Default);

            Assert.IsNull(parsed.Flags);
        }

        [TestMethod]
        public void Parsed_WithZeroFlags_KeepsZero()
        {
            Assert.AreEqual((TrustSetFlags)0, Parse(Body(flags: 0)).Flags);
        }

        [TestMethod]
        public void Parsed_NoRippleWithZeroLimit_KeepsTheFlag()
        {
            ITrustSet parsed = Parse(Body(limit: "0", flags: NoRipple));

            Assert.AreEqual(TrustSetFlags.tfSetNoRipple, parsed.Flags);
            Assert.AreEqual("0", parsed.LimitAmount.Value);
        }

        [TestMethod]
        public void Parsed_ClearNoRipple_DoesNotPickUpSetNoRipple()
        {
            Assert.AreEqual(TrustSetFlags.tfClearNoRipple, Parse(Body(flags: (uint)TrustSetFlags.tfClearNoRipple)).Flags);
        }

        /// <summary>
        /// Older ledger history carries tfFullyCanonicalSig, a global flag the enum does not name.
        /// </summary>
        [TestMethod]
        [DataRow(FullyCanonicalSig)]
        [DataRow(FullyCanonicalSig | NoRipple)]
        public void Parsed_GlobalFlagOutsideTheEnum_KeepsEveryBit(uint flags)
        {
            Assert.AreEqual((TrustSetFlags)flags, Parse(Body(flags: flags)).Flags);
        }

        [TestMethod]
        public void ParsedFromLedgerListing_WithoutFlags_HasNoFlags()
        {
            JsonObject entry = new JsonObject { ["hash"] = new string('A', 64), ["tx_json"] = Body() };

            HashOrTransaction parsed = JsonSerializer.Deserialize<HashOrTransaction>(entry.ToJsonString(), XrplJsonOptions.Default);

            Assert.IsNull(((ITrustSet)parsed.Transaction.Transaction).Flags);
        }

        /// <summary>
        /// Picking up the preset here would turn a signed blob into a different transaction on the next signature.
        /// </summary>
        [TestMethod]
        public void BlobWithoutFlags_ReadsBackWithoutFlags()
        {
            SignatureResult signed = new SignatureResult(XrplBinaryCodec.Encode(Body()), new string('A', 64));

            Assert.IsNull(((ITrustSet)signed.GetTx()).Flags);
        }

        [TestMethod]
        public void Built_StartsWithSetNoRipple()
        {
            Assert.AreEqual(TrustSetFlags.tfSetNoRipple, Built().Flags);
        }

        [TestMethod]
        public void Built_WritesSetNoRipple()
        {
            Assert.AreEqual(NoRipple, Written(Built())["Flags"]!.GetValue<uint>());
        }

        [TestMethod]
        public void Built_WithOtherFlags_WritesOnlyThose()
        {
            TrustSet built = Built();
            built.Flags = TrustSetFlags.tfClearNoRipple;

            Assert.AreEqual((uint)TrustSetFlags.tfClearNoRipple, Written(built)["Flags"]!.GetValue<uint>());
        }

        [TestMethod]
        public void Built_WithClearedFlags_WritesNoFlags()
        {
            TrustSet built = Built();
            built.Flags = null;

            Assert.IsFalse(Written(built).ContainsKey("Flags"));
        }

        [TestMethod]
        public void Built_ThroughJson_KeepsSetNoRipple()
        {
            Assert.AreEqual(TrustSetFlags.tfSetNoRipple, Parse(Written(Built())).Flags);
        }

        [TestMethod]
        public void Built_WithClearedFlags_ThroughJson_StaysCleared()
        {
            TrustSet built = Built();
            built.Flags = null;

            Assert.IsNull(Parse(Written(built)).Flags);
        }

        [TestMethod]
        public void Built_SignsWithSetNoRipple()
        {
            XrplWallet wallet = XrplWallet.Generate();
            TrustSet built = Built();
            built.Account = wallet.ClassicAddress;
            built.Fee = new Currency { Value = "12" };
            built.Sequence = 7;

            ITrustSet signed = (ITrustSet)wallet.Sign(built).GetTx();

            Assert.AreEqual(TrustSetFlags.tfSetNoRipple, signed.Flags);
        }

        [TestMethod]
        public void Built_WithClearedFlags_SignsWithoutFlags()
        {
            XrplWallet wallet = XrplWallet.Generate();
            TrustSet built = Built();
            built.Account = wallet.ClassicAddress;
            built.Fee = new Currency { Value = "12" };
            built.Sequence = 7;
            built.Flags = null;

            ITrustSet signed = (ITrustSet)wallet.Sign(built).GetTx();

            Assert.IsNull(signed.Flags);
        }
    }
}
