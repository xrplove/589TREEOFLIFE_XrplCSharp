using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text.Json;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Trust line side helpers. Expected values follow rippled: the Low side of a RippleState is the
    /// account with the numerically smaller 160-bit AccountID, <c>Balance</c> is stored from the Low
    /// account's side (<c>trustCreate</c> writes <c>-balance</c> when the High account is the one
    /// being set), and <c>lsfLowReserve</c>/<c>lsfHighReserve</c> mark the side whose owner count the
    /// line is in (<c>trustCreate</c>, <c>trustDelete</c>, <c>deleteAMMTrustLine</c>).
    /// </summary>
    [TestClass]
    public class TestURippleStateExtensions
    {
        // The RippleState example from xrpl.org (account_objects): rsA2... is Low, rf1Bi... is High.
        // 'rf' sorts before 'rs' as text, so a base58 string comparison names the wrong side - the
        // fixture is chosen to catch exactly that.
        private const string Low = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";
        private const string High = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";
        private const string Stranger = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

        private const uint HighReserveAndLowAuth = 393216; // 0x60000 in the xrpl.org example

        private static LORippleState Line(
            RippleStateFlags? flags = null,
            string balance = "-5",
            string lowLimit = "0",
            string highLimit = "110")
        {
            return new LORippleState
            {
                Flags = flags,
                Balance = balance == null ? null : new Currency { CurrencyCode = "USD", Issuer = "rrrrrrrrrrrrrrrrrrrrBZbvji", Value = balance },
                LowLimit = lowLimit == null ? null : new Currency { CurrencyCode = "USD", Issuer = Low, Value = lowLimit },
                HighLimit = highLimit == null ? null : new Currency { CurrencyCode = "USD", Issuer = High, Value = highLimit },
            };
        }

        private static int CompareAccountIds(string a, string b)
        {
            byte[] x = XrplCodec.DecodeAccountID(a);
            byte[] y = XrplCodec.DecodeAccountID(b);
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }

            return 0;
        }

        #region Fixture sanity

        [TestMethod]
        public void Fixture_LowIsNumericallySmallerAccountId_AndStringOrderDisagrees()
        {
            Assert.IsLessThan(0, CompareAccountIds(Low, High), "Low must be the smaller AccountID.");
            Assert.IsGreaterThan(0, string.CompareOrdinal(Low, High), "The fixture must distinguish ID order from text order.");
        }

        #endregion

        #region IsLowSide / GetPeerOf

        [TestMethod]
        public void IsLowSide_TellsSidesByAccountNotByAddressText()
        {
            LORippleState line = Line();

            Assert.IsTrue(line.IsLowSide(Low));
            Assert.IsFalse(line.IsLowSide(High));
        }

        [TestMethod]
        public void IsLowSide_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().IsLowSide(Stranger));
        }

        [TestMethod]
        public void IsLowSide_NullArguments_Throw()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).IsLowSide(Low));
            Assert.ThrowsExactly<ArgumentNullException>(() => Line().IsLowSide(null));
        }

        [TestMethod]
        public void IsLowSide_SparseLineWithOnlyTheAccountsOwnLimit_StillAnswers()
        {
            LORippleState lowOnly = Line(lowLimit: "0", highLimit: null);
            LORippleState highOnly = Line(lowLimit: null, highLimit: "110");

            Assert.IsTrue(lowOnly.IsLowSide(Low));
            Assert.IsFalse(highOnly.IsLowSide(High));
        }

        [TestMethod]
        public void IsLowSide_SparseLineWithOnlyThePeersLimit_Throws()
        {
            // Without the account's own limit the side cannot be read from the entry, and guessing
            // would hand a wrong answer to a caller that passed the wrong account.
            Assert.ThrowsExactly<ArgumentException>(() => Line(lowLimit: null).IsLowSide(Low));
            Assert.ThrowsExactly<ArgumentException>(() => Line(highLimit: null).IsLowSide(High));
        }

        [TestMethod]
        public void GetPeerOf_IsTheOtherSide()
        {
            LORippleState line = Line();

            Assert.AreEqual(High, line.GetPeerOf(Low));
            Assert.AreEqual(Low, line.GetPeerOf(High));
        }

        [TestMethod]
        public void GetPeerOf_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetPeerOf(Stranger));
        }

        [TestMethod]
        public void GetPeerOf_PeerLimitMissing_Throws()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Line(highLimit: null).GetPeerOf(Low));
        }

        #endregion

        #region Limits

        [TestMethod]
        public void GetLimitOf_And_GetPeerLimitOf_ReadTheAccountsOwnAndTheOtherSide()
        {
            LORippleState line = Line();

            Assert.AreEqual("0", line.GetLimitOf(Low).Value);
            Assert.AreEqual("110", line.GetPeerLimitOf(Low).Value);
            Assert.AreEqual("110", line.GetLimitOf(High).Value);
            Assert.AreEqual("0", line.GetPeerLimitOf(High).Value);
        }

        [TestMethod]
        public void GetLimitOf_MissingLimit_IsNull()
        {
            LORippleState line = Line(highLimit: null);

            Assert.IsNull(line.GetPeerLimitOf(Low));
            Assert.IsNotNull(line.GetLimitOf(Low));
        }

        [TestMethod]
        public void GetLimitOf_MaximumLimit_DoesNotThrow()
        {
            LORippleState line = Line(highLimit: "9999999999999999e80");

            Assert.AreEqual("9999999999999999e80", line.GetLimitOf(High).Value);
            Assert.AreEqual("9999999999999999e80", line.GetPeerLimitOf(Low).Value);
        }

        #endregion

        #region Balance

        [TestMethod]
        public void GetBalanceFor_IsStoredFromTheLowSideAndNegatedForTheHighSide()
        {
            // Balance -5: the Low account has issued 5 to the High account, so the High account holds 5.
            LORippleState line = Line(balance: "-5");

            Assert.AreEqual(-5m, line.GetBalanceFor(Low));
            Assert.AreEqual(5m, line.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_PositiveStoredBalance_MeansTheLowAccountHolds()
        {
            LORippleState line = Line(balance: "12.5");

            Assert.AreEqual(12.5m, line.GetBalanceFor(Low));
            Assert.AreEqual(-12.5m, line.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_BeyondDecimal_SaturatesInsteadOfThrowing()
        {
            LORippleState issued = Line(balance: "-9999999999999999e80");

            Assert.AreEqual(decimal.MinValue, issued.GetBalanceFor(Low));
            Assert.AreEqual(decimal.MaxValue, issued.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_MissingBalance_ReadsAsZero()
        {
            LORippleState line = Line(balance: null);

            Assert.AreEqual(0m, line.GetBalanceFor(Low));
            Assert.AreEqual(0m, line.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetBalanceFor(Stranger));
        }

        [TestMethod]
        public void GetBalanceFor_NullArguments_Throw()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).GetBalanceFor(Low));
            Assert.ThrowsExactly<ArgumentNullException>(() => Line().GetBalanceFor(null));
        }

        [TestMethod]
        public void GetBalanceFor_ChecksTheAccountBeforeReadingTheBalance()
        {
            // A malformed balance must not mask the contract violation of an account that is not on the line.
            Assert.ThrowsExactly<ArgumentException>(() => Line(balance: "abc").GetBalanceFor(Stranger));
        }

        [TestMethod]
        public void GetLimitOf_And_GetPeerLimitOf_AccountNotOnTheLine_Throw()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetLimitOf(Stranger));
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetPeerLimitOf(Stranger));
        }

        [TestMethod]
        public void GetLimitOf_And_GetPeerLimitOf_NullArguments_Throw()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).GetLimitOf(Low));
            Assert.ThrowsExactly<ArgumentNullException>(() => Line().GetPeerLimitOf(null));
        }

        [TestMethod]
        public void GetBalanceValueFor_NullArguments_Throw()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).GetBalanceValueFor(Low));
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetBalanceValueFor(Stranger));
        }

        [TestMethod]
        public void GetBalanceValueFor_KeepsEveryDigitOfTheLedgerText()
        {
            // A decimal round trip would saturate the first and round the second.
            LORippleState huge = Line(balance: "-9999999999999999e80");
            LORippleState dust = Line(balance: "1.234567890123456e-30");

            Assert.AreEqual("-9999999999999999e80", huge.GetBalanceValueFor(Low));
            Assert.AreEqual("9999999999999999e80", huge.GetBalanceValueFor(High));
            Assert.AreEqual("1.234567890123456e-30", dust.GetBalanceValueFor(Low));
            Assert.AreEqual("-1.234567890123456e-30", dust.GetBalanceValueFor(High));
        }

        [TestMethod]
        public void GetBalanceValueFor_ZeroHasNoSign()
        {
            foreach (string zero in new[] { "0", "-0", "0.00", "0e5" })
            {
                LORippleState line = Line(balance: zero);

                Assert.AreEqual(zero, line.GetBalanceValueFor(Low), "The Low side gets the stored text.");
                Assert.IsFalse(line.GetBalanceValueFor(High).StartsWith("-", StringComparison.Ordinal), $"'{zero}' negated must not become negative.");
            }
        }

        [TestMethod]
        public void GetBalanceValueFor_NotANumber_IsNullForTheHighSide()
        {
            foreach (string text in new[] { "abc", "", "1e", "NaN", "Infinity", "1e999" })
            {
                Assert.IsNull(Line(balance: text).GetBalanceValueFor(High), $"'{text}' is not a ledger amount.");
            }

            Assert.IsNull(Line(balance: null).GetBalanceValueFor(Low));
            Assert.IsNull(Line(balance: null).GetBalanceValueFor(High));
        }

        #endregion

        #region IsFlagSet

        [TestMethod]
        public void IsFlagSet_NullFlags_NothingIsSet()
        {
            RippleStateFlags? absent = null;

            foreach (RippleStateFlags flag in Enum.GetValues<RippleStateFlags>())
            {
                Assert.IsFalse(absent.IsFlagSet(flag), $"{flag} must not read as set on an absent flag word.");
            }
        }

        [TestMethod]
        public void IsFlagSet_ReadsTheBit()
        {
            RippleStateFlags? flags = (RippleStateFlags)HighReserveAndLowAuth;

            Assert.IsTrue(flags.IsFlagSet(RippleStateFlags.lsfHighReserve));
            Assert.IsTrue(flags.IsFlagSet(RippleStateFlags.lsfLowAuth));
            Assert.IsFalse(flags.IsFlagSet(RippleStateFlags.lsfLowReserve));
            Assert.IsFalse(flags.IsFlagSet(RippleStateFlags.lsfHighFreeze));
        }

        [TestMethod]
        public void IsFlagSet_ZeroFlagWord_NothingIsSet()
        {
            RippleStateFlags? zero = 0;

            Assert.IsFalse(zero.IsFlagSet(RippleStateFlags.lsfLowReserve));
        }

        [TestMethod]
        public void IsFlagSet_CombinedFlag_RequiresEveryBit()
        {
            RippleStateFlags? flags = RippleStateFlags.lsfLowFreeze;
            RippleStateFlags both = RippleStateFlags.lsfLowFreeze | RippleStateFlags.lsfHighFreeze;

            Assert.IsFalse(flags.IsFlagSet(both));
            Assert.IsTrue(((RippleStateFlags?)both).IsFlagSet(both));
        }

        #endregion

        #region Flags of a side

        [TestMethod]
        public void GetFlagOf_And_GetPeerFlagOf_ProjectPairedFlagsToTheAccount()
        {
            // Low auth only: the Low account has authorized the High account.
            LORippleState line = Line(RippleStateFlags.lsfLowAuth | RippleStateFlags.lsfHighNoRipple);

            Assert.IsTrue(line.GetFlagOf(Low, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsFalse(line.GetFlagOf(High, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsFalse(line.GetPeerFlagOf(Low, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsTrue(line.GetPeerFlagOf(High, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));

            Assert.IsFalse(line.GetFlagOf(Low, RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple));
            Assert.IsTrue(line.GetFlagOf(High, RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple));
            Assert.IsTrue(line.GetPeerFlagOf(Low, RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple));
        }

        [TestMethod]
        public void GetFlagOf_AbsentFlagWord_IsFalse()
        {
            LORippleState line = Line(flags: null);

            Assert.IsFalse(line.GetFlagOf(Low, RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze));
            Assert.IsFalse(line.GetPeerFlagOf(Low, RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze));
        }

        [TestMethod]
        public void GetFlagOf_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => Line(RippleStateFlags.lsfLowFreeze).GetFlagOf(Stranger, RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze));
        }

        #endregion

        #region Reserve

        [TestMethod]
        public void IsReserveSetFor_FollowsTheReserveFlagOfTheAccountsSide()
        {
            // 393216 from the xrpl.org example: lsfHighReserve | lsfLowAuth. The High account's owner
            // count includes the line; the Low account's does not.
            LORippleState line = Line((RippleStateFlags)HighReserveAndLowAuth);

            Assert.IsFalse(line.IsReserveSetFor(Low));
            Assert.IsTrue(line.IsReserveSetFor(High));
            Assert.IsTrue(line.IsPeerReserveSetFor(Low));
            Assert.IsFalse(line.IsPeerReserveSetFor(High));
        }

        [TestMethod]
        public void IsReserveSetFor_BothSidesReserved()
        {
            LORippleState line = Line(RippleStateFlags.lsfLowReserve | RippleStateFlags.lsfHighReserve);

            Assert.IsTrue(line.IsReserveSetFor(Low));
            Assert.IsTrue(line.IsReserveSetFor(High));
            Assert.IsTrue(line.IsPeerReserveSetFor(Low));
            Assert.IsTrue(line.IsPeerReserveSetFor(High));
        }

        [TestMethod]
        public void IsReserveSetFor_LowReserveOnly()
        {
            LORippleState line = Line(RippleStateFlags.lsfLowReserve);

            Assert.IsTrue(line.IsReserveSetFor(Low));
            Assert.IsFalse(line.IsReserveSetFor(High));
            Assert.IsFalse(line.IsPeerReserveSetFor(Low));
            Assert.IsTrue(line.IsPeerReserveSetFor(High));
        }

        [TestMethod]
        public void IsReserveSetFor_AbsentFlagWord_IsFalse()
        {
            LORippleState line = Line(flags: null);

            Assert.IsFalse(line.IsReserveSetFor(Low));
            Assert.IsFalse(line.IsPeerReserveSetFor(Low));
        }

        [TestMethod]
        public void IsReserveSetFor_AccountNotOnTheLine_Throws()
        {
            LORippleState line = Line(RippleStateFlags.lsfLowReserve);

            Assert.ThrowsExactly<ArgumentException>(() => line.IsReserveSetFor(Stranger));
            Assert.ThrowsExactly<ArgumentException>(() => line.IsPeerReserveSetFor(Stranger));
        }

        #endregion

        #region Quality

        [TestMethod]
        public void Quality_ReadsTheAccountsOwnAndThePeersValues()
        {
            LORippleState line = Line();
            line.LowQualityIn = 950_000_000;
            line.LowQualityOut = 1_010_000_000;
            line.HighQualityIn = 990_000_000;
            line.HighQualityOut = 1_020_000_000;

            Assert.AreEqual(950_000_000u, line.GetQualityInOf(Low));
            Assert.AreEqual(1_010_000_000u, line.GetQualityOutOf(Low));
            Assert.AreEqual(990_000_000u, line.GetPeerQualityInOf(Low));
            Assert.AreEqual(1_020_000_000u, line.GetPeerQualityOutOf(Low));

            Assert.AreEqual(990_000_000u, line.GetQualityInOf(High));
            Assert.AreEqual(1_020_000_000u, line.GetQualityOutOf(High));
            Assert.AreEqual(950_000_000u, line.GetPeerQualityInOf(High));
            Assert.AreEqual(1_010_000_000u, line.GetPeerQualityOutOf(High));
        }

        [TestMethod]
        public void Quality_NotSet_IsNullNotZero()
        {
            // rippled writes the field only when non-zero; absence is face value and is not invented as 0.
            LORippleState line = Line();

            Assert.IsNull(line.GetQualityInOf(Low));
            Assert.IsNull(line.GetQualityOutOf(High));
            Assert.IsNull(line.GetPeerQualityInOf(Low));
            Assert.IsNull(line.GetPeerQualityOutOf(High));
        }

        [TestMethod]
        public void Quality_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetQualityInOf(Stranger));
        }

        #endregion

        #region OverlayPreviousFields

        [TestMethod]
        public void OverlayPreviousFields_NoPrevious_ReturnsTheFinalState()
        {
            LORippleState final = Line(RippleStateFlags.lsfLowReserve);

            Assert.AreSame(final, final.OverlayPreviousFields(null));
        }

        [TestMethod]
        public void OverlayPreviousFields_NullFinal_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).OverlayPreviousFields(Line()));
        }

        [TestMethod]
        public void OverlayPreviousFields_DeletedLineWithOneLimitChanged_KeepsTheFinalFlags()
        {
            // The common shape of a deleted line in metadata: FinalFields carries the line, and
            // PreviousFields lists only what the transaction changed - here the Low limit the
            // initiator set back to zero. Flags is absent from PreviousFields and must survive.
            LORippleState final = Line(
                RippleStateFlags.lsfLowReserve | RippleStateFlags.lsfLowNoRipple,
                balance: "0",
                lowLimit: "0",
                highLimit: "0");
            final.Index = "AAA";
            final.PreviousTxnID = "FINALTX";
            LORippleState previous = new LORippleState
            {
                LowLimit = new Currency { CurrencyCode = "USD", Issuer = Low, Value = "100" },
            };

            LORippleState merged = final.OverlayPreviousFields(previous);

            Assert.AreEqual("100", merged.LowLimit.Value);
            Assert.AreEqual("0", merged.HighLimit.Value);
            Assert.AreEqual(final.Flags, merged.Flags);
            Assert.IsTrue(merged.Flags.IsFlagSet(RippleStateFlags.lsfLowReserve));
            Assert.IsTrue(merged.IsReserveSetFor(Low));
            Assert.AreEqual("AAA", merged.Index);
            Assert.AreEqual("FINALTX", merged.PreviousTxnID);
        }

        [TestMethod]
        public void OverlayPreviousFields_PreviousFlagsOfZero_IsARealDiffAndWins()
        {
            // PreviousFields.Flags == 0 means the transaction cleared every flag; it is not "absent".
            LORippleState final = Line(RippleStateFlags.lsfLowReserve);
            LORippleState previous = new LORippleState { Flags = 0 };

            LORippleState merged = final.OverlayPreviousFields(previous);

            Assert.AreEqual((RippleStateFlags)0, merged.Flags);
            Assert.IsFalse(merged.IsReserveSetFor(Low));
        }

        [TestMethod]
        public void OverlayPreviousFields_PreviousFieldsWinOverFinalFields()
        {
            LORippleState final = Line(RippleStateFlags.lsfHighReserve, balance: "-3");
            final.LowQualityIn = 1;
            final.HighQualityOut = 2;
            final.LowNode = "0";
            final.HighNode = "1";
            final.PreviousTxnLgrSeq = 10;
            final.LowSponsor = "rLowSponsorFinal";
            LORippleState previous = new LORippleState
            {
                Flags = RippleStateFlags.lsfLowReserve,
                Balance = new Currency { CurrencyCode = "USD", Issuer = "rrrrrrrrrrrrrrrrrrrrBZbvji", Value = "-7" },
                LowQualityIn = 5,
                HighQualityOut = 6,
                LowNode = "9",
                HighNode = "8",
                PreviousTxnLgrSeq = 3,
                LowSponsor = "rLowSponsorPrevious",
                HighSponsor = "rHighSponsorPrevious",
            };

            LORippleState merged = final.OverlayPreviousFields(previous);

            Assert.AreEqual(RippleStateFlags.lsfLowReserve, merged.Flags);
            Assert.AreEqual("-7", merged.Balance.Value);
            Assert.AreEqual(5u, merged.LowQualityIn);
            Assert.AreEqual(6u, merged.HighQualityOut);
            Assert.AreEqual("9", merged.LowNode);
            Assert.AreEqual("8", merged.HighNode);
            Assert.AreEqual(3u, merged.PreviousTxnLgrSeq);
            Assert.AreEqual("rLowSponsorPrevious", merged.LowSponsor);
            Assert.AreEqual("rHighSponsorPrevious", merged.HighSponsor);
            // Not in PreviousFields: taken from the final state.
            Assert.AreEqual("110", merged.HighLimit.Value);
            Assert.IsNull(merged.HighQualityIn);
        }

        [TestMethod]
        public void OverlayPreviousFields_DoesNotChangeEitherInput()
        {
            LORippleState final = Line(RippleStateFlags.lsfLowReserve, lowLimit: "0");
            LORippleState previous = new LORippleState
            {
                LowLimit = new Currency { CurrencyCode = "USD", Issuer = Low, Value = "100" },
            };

            LORippleState merged = final.OverlayPreviousFields(previous);

            Assert.AreNotSame(final, merged);
            Assert.AreEqual("0", final.LowLimit.Value);
            Assert.AreEqual("100", previous.LowLimit.Value);
            Assert.IsNull(previous.Flags);
        }

        [TestMethod]
        public void OverlayPreviousFields_UnknownFieldsAreMergedWithPreviousWinning()
        {
            LORippleState final = Line();
            final.UnknownFields = new Dictionary<string, JsonElement>
            {
                ["OnlyFinal"] = JsonDocument.Parse("1").RootElement.Clone(),
                ["Both"] = JsonDocument.Parse("2").RootElement.Clone(),
            };
            LORippleState previous = new LORippleState
            {
                UnknownFields = new Dictionary<string, JsonElement>
                {
                    ["Both"] = JsonDocument.Parse("3").RootElement.Clone(),
                },
            };

            LORippleState merged = final.OverlayPreviousFields(previous);

            Assert.AreEqual(1, merged.UnknownFields["OnlyFinal"].GetInt32());
            Assert.AreEqual(3, merged.UnknownFields["Both"].GetInt32());
            Assert.HasCount(2, final.UnknownFields);
        }

        #endregion

        #region Deserialized entries and metadata nodes

        // The RippleState from xrpl.org (account_objects), verbatim.
        private const string LedgerEntryJson = """
            {
                "Balance": {"currency": "USD", "issuer": "rrrrrrrrrrrrrrrrrrrrBZbvji", "value": "-5"},
                "Flags": 393216,
                "HighLimit": {"currency": "USD", "issuer": "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", "value": "110"},
                "HighNode": "0000000000000000",
                "LedgerEntryType": "RippleState",
                "LowLimit": {"currency": "USD", "issuer": "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW", "value": "0"},
                "LowNode": "0000000000000000",
                "PreviousTxnID": "E3FE6EA3D48F0C2B639448020EA4F03D4F4F8FFDB243A852A0F59177921B4879",
                "PreviousTxnLgrSeq": 14090896,
                "index": "9CA88CDEDFF9252B3DE183CE35B038F57282BC9503CDFA1923EF9A95DF0D6F7B"
            }
            """;

        private static AffectedNode ReadNode(string json)
            => JsonSerializer.Deserialize<AffectedNode>(json, XrplJsonOptions.Default);

        [TestMethod]
        public void LedgerEntry_DeserializedFromTheXrplOrgExample_ReadsFromBothSides()
        {
            LORippleState line = (LORippleState)JsonSerializer.Deserialize<BaseLedgerEntry>(LedgerEntryJson, XrplJsonOptions.Default);

            Assert.AreEqual((RippleStateFlags)HighReserveAndLowAuth, line.Flags);
            Assert.IsTrue(line.IsLowSide(Low));
            Assert.IsFalse(line.IsLowSide(High));
            Assert.AreEqual(High, line.GetPeerOf(Low));
            Assert.AreEqual("110", line.GetLimitOf(High).Value);
            Assert.AreEqual("0", line.GetPeerLimitOf(High).Value);
            Assert.AreEqual(-5m, line.GetBalanceFor(Low));
            Assert.AreEqual(5m, line.GetBalanceFor(High));
            Assert.AreEqual("5", line.GetBalanceValueFor(High));
            Assert.IsTrue(line.IsReserveSetFor(High));
            Assert.IsFalse(line.IsReserveSetFor(Low));
            Assert.IsTrue(line.GetFlagOf(Low, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsNull(line.GetQualityInOf(Low));
        }

        [TestMethod]
        public void ModifiedNode_FinalAndPreviousFields_MergeToThePreTransactionLine()
        {
            // A payment moved 2 USD from the Low account's side: the balance went from -3 to -5. The
            // flags and limits did not change, so PreviousFields lists only the balance.
            AffectedNode node = ReadNode("""
                {
                    "ModifiedNode": {
                        "FinalFields": {
                            "Balance": {"currency": "USD", "issuer": "rrrrrrrrrrrrrrrrrrrrBZbvji", "value": "-5"},
                            "Flags": 393216,
                            "HighLimit": {"currency": "USD", "issuer": "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", "value": "110"},
                            "HighNode": "0000000000000000",
                            "LowLimit": {"currency": "USD", "issuer": "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW", "value": "0"},
                            "LowNode": "0000000000000000"
                        },
                        "LedgerEntryType": "RippleState",
                        "LedgerIndex": "9CA88CDEDFF9252B3DE183CE35B038F57282BC9503CDFA1923EF9A95DF0D6F7B",
                        "PreviousFields": {
                            "Balance": {"currency": "USD", "issuer": "rrrrrrrrrrrrrrrrrrrrBZbvji", "value": "-3"}
                        },
                        "PreviousTxnID": "E3FE6EA3D48F0C2B639448020EA4F03D4F4F8FFDB243A852A0F59177921B4879",
                        "PreviousTxnLgrSeq": 14090896
                    }
                }
                """);

            LORippleState final = (LORippleState)node.ModifiedNode.FinalFields;
            LORippleState previous = (LORippleState)node.ModifiedNode.PreviousFields;
            Assert.IsNull(previous.Flags, "PreviousFields carries no flag word here.");

            LORippleState before = final.OverlayPreviousFields(previous);

            Assert.AreEqual(-5m, final.GetBalanceFor(Low));
            Assert.AreEqual(-3m, before.GetBalanceFor(Low));
            Assert.AreEqual(3m, before.GetBalanceFor(High));
            Assert.AreEqual(final.Flags, before.Flags);
            Assert.IsTrue(before.IsReserveSetFor(High));
            Assert.AreEqual("110", before.GetLimitOf(High).Value);
        }

        [TestMethod]
        public void DeletedNode_PreviousFieldsWithOnlyALimit_KeepTheFinalFlags()
        {
            // The Low account deleted its side of an empty line by taking its limit back to zero.
            // PreviousFields lists the limit alone; the flag word has to come from FinalFields.
            AffectedNode node = ReadNode("""
                {
                    "DeletedNode": {
                        "FinalFields": {
                            "Balance": {"currency": "USD", "issuer": "rrrrrrrrrrrrrrrrrrrrBZbvji", "value": "0"},
                            "Flags": 65536,
                            "HighLimit": {"currency": "USD", "issuer": "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", "value": "0"},
                            "HighNode": "0000000000000000",
                            "LowLimit": {"currency": "USD", "issuer": "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW", "value": "0"},
                            "LowNode": "0000000000000000"
                        },
                        "LedgerEntryType": "RippleState",
                        "LedgerIndex": "9CA88CDEDFF9252B3DE183CE35B038F57282BC9503CDFA1923EF9A95DF0D6F7B",
                        "PreviousFields": {
                            "LowLimit": {"currency": "USD", "issuer": "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW", "value": "100"}
                        }
                    }
                }
                """);

            LORippleState before = ((LORippleState)node.DeletedNode.FinalFields)
                .OverlayPreviousFields((LORippleState)node.DeletedNode.PreviousFields);

            Assert.AreEqual("100", before.GetLimitOf(Low).Value);
            Assert.AreEqual("0", before.GetPeerLimitOf(Low).Value);
            Assert.AreEqual((RippleStateFlags)65536, before.Flags);
            Assert.IsTrue(before.IsReserveSetFor(Low));
            Assert.IsFalse(before.IsPeerReserveSetFor(Low));
        }

        [TestMethod]
        public void DeletedNode_PreviousFieldsWithTheFlagWord_ShowTheReserveBeforeItWasCleared()
        {
            // rippled clears the reserve flags before it deletes a line, so the final flag word of a
            // deleted node no longer says who paid the reserve; PreviousFields does.
            AffectedNode node = ReadNode("""
                {
                    "DeletedNode": {
                        "FinalFields": {
                            "Balance": {"currency": "USD", "issuer": "rrrrrrrrrrrrrrrrrrrrBZbvji", "value": "0"},
                            "Flags": 0,
                            "HighLimit": {"currency": "USD", "issuer": "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", "value": "0"},
                            "LowLimit": {"currency": "USD", "issuer": "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW", "value": "0"}
                        },
                        "LedgerEntryType": "RippleState",
                        "LedgerIndex": "9CA88CDEDFF9252B3DE183CE35B038F57282BC9503CDFA1923EF9A95DF0D6F7B",
                        "PreviousFields": {
                            "Flags": 131072,
                            "HighLimit": {"currency": "USD", "issuer": "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", "value": "100"}
                        }
                    }
                }
                """);

            LORippleState final = (LORippleState)node.DeletedNode.FinalFields;
            LORippleState before = final.OverlayPreviousFields((LORippleState)node.DeletedNode.PreviousFields);

            Assert.IsFalse(final.IsReserveSetFor(High));
            Assert.IsTrue(before.IsReserveSetFor(High));
            Assert.AreEqual("100", before.GetLimitOf(High).Value);
        }

        #endregion

        #region ToDecimalClamped

        private static Currency Iou(string value) => new Currency { CurrencyCode = "USD", Issuer = Low, Value = value };

        [TestMethod]
        public void ToDecimalClamped_InRange_IsTheExactValue()
        {
            Assert.AreEqual(12.5m, Iou("12.5").ToDecimalClamped());
            Assert.AreEqual(-0.000001m, Iou("-0.000001").ToDecimalClamped());
            Assert.AreEqual(0m, Iou("0").ToDecimalClamped());
        }

        [TestMethod]
        public void ToDecimalClamped_AboveDecimalRange_SaturatesWithTheSign()
        {
            foreach (string big in new[] { "1e29", "8e28", "9999999999999999e80" })
            {
                Assert.AreEqual(decimal.MaxValue, Iou(big).ToDecimalClamped(), big);
                Assert.AreEqual(decimal.MinValue, Iou("-" + big).ToDecimalClamped(), "-" + big);
            }
        }

        [TestMethod]
        public void ToDecimalClamped_NullAndBlank_AreZero()
        {
            Assert.AreEqual(0m, ((Currency)null).ToDecimalClamped());
            Assert.AreEqual(0m, Iou(null).ToDecimalClamped());
            Assert.AreEqual(0m, Iou("  ").ToDecimalClamped());
        }

        [TestMethod]
        public void ToDecimalClamped_BelowDecimalPrecision_ReadsAsZeroLikeValueAsNumber()
        {
            Assert.AreEqual(0m, Iou("1e-81").ToDecimalClamped());
        }

        [TestMethod]
        public void ToDecimalClamped_NotANumber_StillThrowsFormatException()
        {
            // Saturation answers "too large", not "not a quantity".
            Assert.ThrowsExactly<FormatException>(() => Iou("abc").ToDecimalClamped());
            Assert.ThrowsExactly<FormatException>(() => Iou("NaN").ToDecimalClamped());
        }

        [TestMethod]
        public void ToDecimalClamped_XrpIsReadInDrops_LikeValueAsNumber()
        {
            Currency drops = new Currency { CurrencyCode = "XRP", Value = "1000000" };

            Assert.AreEqual(1000000m, drops.ToDecimalClamped());
        }

        [TestMethod]
        public void ValueAsNumber_StillThrowsWhereTheSaturatingReadDoesNot()
        {
            Currency big = Iou("9999999999999999e80");

            Assert.ThrowsExactly<AmountOutOfRangeException>(() => _ = big.ValueAsNumber);
            Assert.AreEqual(decimal.MaxValue, big.ToDecimalClamped());
        }

        #endregion
    }
}
