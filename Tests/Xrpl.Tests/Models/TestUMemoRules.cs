using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace Xrpl.Tests.Models
{
    /// <summary>
    /// Memos a node refuses locally are refused before the transaction is signed - issue #119.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>isMemoOkay</c> runs in rippled's <c>passesLocalChecks</c>: the transaction is not relayed,
    /// reaches no ledger and costs no fee, and the answer names no field. The consumer has by then
    /// built, autofilled and signed it. These tests go through <see cref="XrplWallet.Sign"/> rather
    /// than a validator, because that is the point of the fix: <c>Validation.Validate</c> is called
    /// nowhere in production, so a rule that lived only there would be a rule nobody runs.
    /// </para>
    /// <para>
    /// The two rules the codec already enforces - a member other than <c>MemoType</c>,
    /// <c>MemoData</c> or <c>MemoFormat</c> inside a <c>Memo</c>, and a value that is not hex - are
    /// not retested here. <c>TestUStrictNestedFields</c> covers the first, and duplicating them
    /// would create a second place to keep in step with the same truth.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUMemoRules
    {
        private const string Seed = "snGHNrPbHrdUcszeuDEigMdC1Lyyd";

        /// <summary>
        /// The largest <c>MemoData</c> that fits in a memo carrying nothing else.
        /// </summary>
        /// <remarks>
        /// 1 byte of object start marker + 1 of field id + 2 of length prefix (a value over 192
        /// bytes takes two) + the data + 1 of object end marker = 1024 exactly.
        /// </remarks>
        private const int LargestMemoDataInOneMemo = 1019;

        private static Dictionary<string, object> Payment(XrplWallet wallet) => new Dictionary<string, object>
        {
            { "TransactionType", "Payment" },
            { "Account", wallet.ClassicAddress },
            { "Destination", "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c" },
            { "Amount", "1000" },
            { "Fee", "12" },
            { "Sequence", 1u },
            { "LastLedgerSequence", 100u },
        };

        private static Dictionary<string, object> Memo(string memoData, string memoType = null, string memoFormat = null)
        {
            Dictionary<string, object> memo = new Dictionary<string, object>();
            if (memoType != null)
            {
                memo["MemoType"] = memoType;
            }

            if (memoData != null)
            {
                memo["MemoData"] = memoData;
            }

            if (memoFormat != null)
            {
                memo["MemoFormat"] = memoFormat;
            }

            return new Dictionary<string, object> { { "Memo", memo } };
        }

        private static string HexOf(int byteCount) => new string('A', byteCount * 2);

        /// <summary>
        /// The boundary from below: a memo filling the limit exactly still signs.
        /// </summary>
        /// <remarks>
        /// Without this, a check that refused every memo would satisfy every other test here.
        /// </remarks>
        [TestMethod]
        public void TestUMemoAtTheLimitStillSigns()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo)) };

            SignatureResult signed = wallet.Sign(tx);

            Assert.IsFalse(
                string.IsNullOrEmpty(signed.TxBlob),
                "A memo of exactly the maximum size is legal and must still produce a blob.");
        }

        /// <summary>
        /// One byte over, and it is refused - before a signature exists.
        /// </summary>
        [TestMethod]
        public void TestUMemoOverTheLimitIsRefusedBeforeSigning()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(
                () => wallet.Sign(tx),
                "one byte past the limit is what the node refuses, so signing it is work thrown away");

            StringAssert.Contains(
                error.Message,
                "1025",
                "The message must say how large the array actually came out, or the caller is left guessing how much to cut.");
            Assert.IsFalse(
                tx.ContainsKey("TxnSignature"),
                "Refused before signing: nothing may have been added to the transaction.");
        }

        /// <summary>
        /// The limit is on the array, not on one memo, so splitting the content does not get round it.
        /// </summary>
        /// <remarks>
        /// Worth its own test because the opposite is the natural assumption, and acting on it
        /// costs another round trip to a node that refuses the transaction just the same.
        /// </remarks>
        [TestMethod]
        public void TestUSeveralMemosShareOneLimit()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object>
            {
                Memo(HexOf(600)),
                Memo(HexOf(600)),
            };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => wallet.Sign(tx));

            StringAssert.Contains(
                error.Message,
                "whole array",
                "The message must say the limit is on the array, since splitting is the obvious thing to try next.");
        }

        /// <summary>
        /// <c>MemoType</c> and <c>MemoFormat</c> may only decode to characters a URL allows.
        /// </summary>
        [TestMethod]
        public void TestUMemoTypeMustDecodeToUrlSafeCharacters()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);

            // "a b" - the space is legal hex and legal UTF-8, and not legal here.
            tx["Memos"] = new List<object> { Memo(memoData: "72656E74", memoType: "612062") };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => wallet.Sign(tx));

            StringAssert.Contains(error.Message, "MemoType");
            StringAssert.Contains(
                error.Message,
                "0x20",
                "Naming the byte is what turns this from a puzzle into a fix.");
        }

        /// <summary>
        /// The same restriction on <c>MemoFormat</c>, which is the field it is easiest to forget.
        /// </summary>
        [TestMethod]
        public void TestUMemoFormatMustDecodeToUrlSafeCharacters()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(memoData: "72656E74", memoFormat: "612062") };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => wallet.Sign(tx));

            StringAssert.Contains(error.Message, "MemoFormat");
        }

        /// <summary>
        /// The restriction stops at <c>MemoData</c>: it carries arbitrary bytes by design.
        /// </summary>
        /// <remarks>
        /// The same bytes that are refused in a <c>MemoType</c> above must go through here, or the
        /// check has been applied one field too widely - and a memo is mostly used for exactly the
        /// content that is not URL-safe.
        /// </remarks>
        [TestMethod]
        public void TestUMemoDataMayHoldAnythingAtAll()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(memoData: "612062FF00", memoType: "687474703A2F2F612E62") };

            SignatureResult signed = wallet.Sign(tx);

            Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob));
        }

        /// <summary>
        /// A transaction without memos is not touched by any of this.
        /// </summary>
        [TestMethod]
        public void TestUNoMemosSignsUnchanged()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);

            SignatureResult signed = wallet.Sign(Payment(wallet));

            Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob));
        }

        /// <summary>
        /// The rule holds for the helpers that sign without going through <c>Sign</c>.
        /// </summary>
        /// <remarks>
        /// <c>Sign</c> is not the only public way into a signature: <c>SignAsSponsor</c>,
        /// <c>SignAsLoanCounterparty</c> and <c>SignAsBatchPart</c> each sign on their own, and the
        /// SDK's own multi-batch submission calls the last of them directly (<c>Submit.cs</c>). A
        /// guard on <c>Sign</c> alone would therefore have left the batch path unchecked - which is
        /// how this was found, in review.
        /// </remarks>
        [TestMethod]
        public void TestUSigningAsSponsorIsCheckedToo()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Sponsor"] = wallet.ClassicAddress;
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(
                () => wallet.SignAsSponsor(tx));

            StringAssert.Contains(error.Message, "1025 bytes");
        }

        /// <summary>
        /// The same for the borrower's co-signature on a <c>LoanSet</c>.
        /// </summary>
        [TestMethod]
        public void TestUSigningAsLoanCounterpartyIsCheckedToo()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["TransactionType"] = "LoanSet";
            tx["Counterparty"] = wallet.ClassicAddress;
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(
                () => wallet.SignAsLoanCounterparty(tx));

            StringAssert.Contains(error.Message, "1025 bytes");
        }

        /// <summary>
        /// And for a batch part - the one the SDK itself signs directly.
        /// </summary>
        [TestMethod]
        public void TestUSigningABatchPartIsCheckedToo()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["TransactionType"] = "Batch";
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(
                () => wallet.SignAsBatchPart(tx, multisign: false, signingFor: wallet.ClassicAddress));

            StringAssert.Contains(error.Message, "1025 bytes");
        }

        /// <summary>
        /// A <c>MemoType</c> that is not a string at all is the codec's to refuse, not these rules'.
        /// </summary>
        /// <remarks>
        /// Reading it as a string would throw <c>InvalidOperationException</c> from deep inside
        /// <c>System.Text.Json</c> - neither the exception a caller of the SDK expects nor a message
        /// that names the field. The same principle as the rules the codec already owns: what these
        /// rules cannot read, they leave alone.
        /// </remarks>
        [TestMethod]
        public void TestUANonStringMemoTypeIsLeftToTheCodec()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    {
                        "Memo", new Dictionary<string, object>
                        {
                            { "MemoType", 42 },
                            { "MemoData", "72656E74" },
                        }
                    },
                },
            };

            Exception error = null;
            try
            {
                wallet.Sign(tx);
            }
            catch (Exception thrown)
            {
                error = thrown;
            }

            Assert.IsNotNull(error, "A MemoType that is not a string cannot end up on the wire.");
            Assert.IsNotInstanceOfType<InvalidOperationException>(
                error,
                $"A non-string MemoType must be reported by whoever owns that rule, not leaked as a " +
                $"JSON reader's own exception. Got: {error}");
        }

        /// <summary>
        /// The measurement is the node's own: the array's serialized length, without the array's
        /// own markers.
        /// </summary>
        /// <remarks>
        /// Asserted directly rather than only through signing, because the arithmetic is the part
        /// that is easy to get wrong by a byte or two and hard to see afterwards.
        /// </remarks>
        [TestMethod]
        public void TestUTheLimitIsMeasuredTheWayANodeMeasuresIt()
        {
            List<object> atTheLimit = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo)) };
            List<object> justOver = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            MemoRules.Validate(atTheLimit);

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => MemoRules.Validate(justOver));

            StringAssert.Contains(error.Message, $"{MemoRules.MaxSerializedLength + 1} bytes");
        }

        /// <summary>
        /// "text/plain" as hex: the <c>MemoFormat</c> a form that sends text puts beside the data.
        /// </summary>
        private const string TextPlainHex = "746578742F706C61696E";

        private static int LengthOf(object memos)
        {
            Assert.IsTrue(MemoRules.TryGetSerializedLength(memos, out int length), "the array must be measurable");
            return length;
        }

        /// <summary>
        /// The public measurement agrees with the check: a data-only memo fills the limit at 1019 bytes.
        /// </summary>
        [TestMethod]
        public void TestUOneMemoOfDataOnlyFillsTheLimitAt1019Bytes()
        {
            List<object> atTheLimit = new List<object> { Memo(HexOf(1019)) };
            List<object> justOver = new List<object> { Memo(HexOf(1020)) };

            Assert.AreEqual(MemoRules.MaxSerializedLength, LengthOf(atTheLimit));
            MemoRules.Validate(atTheLimit);

            Assert.AreEqual(MemoRules.MaxSerializedLength + 1, LengthOf(justOver));
            Assert.ThrowsExactly<ValidationException>(() => MemoRules.Validate(justOver));
        }

        /// <summary>
        /// A caller adding the fixed overhead of a plain-text memo (17 bytes) gets the same answer
        /// as the measurement.
        /// </summary>
        [TestMethod]
        public void TestUAPlainTextMemoFillsTheLimitAt1007Bytes()
        {
            List<object> atTheLimit = new List<object> { Memo(HexOf(1007), memoFormat: TextPlainHex) };
            List<object> justOver = new List<object> { Memo(HexOf(1008), memoFormat: TextPlainHex) };

            Assert.AreEqual(MemoRules.MaxSerializedLength, LengthOf(atTheLimit));
            MemoRules.Validate(atTheLimit);

            Assert.AreEqual(MemoRules.MaxSerializedLength + 1, LengthOf(justOver));
            Assert.ThrowsExactly<ValidationException>(() => MemoRules.Validate(justOver));
        }

        /// <summary>
        /// The overhead is constant from 193 bytes of data on (two length bytes), so
        /// <c>n + 17</c> holds across that range.
        /// </summary>
        [TestMethod]
        public void TestUAPlainTextMemoCostsItsDataPlus17Bytes()
        {
            foreach (int dataBytes in new[] { 193, 500, 1007 })
            {
                Assert.AreEqual(
                    dataBytes + 17,
                    LengthOf(new List<object> { Memo(HexOf(dataBytes), memoFormat: TextPlainHex) }));
            }
        }

        /// <summary>
        /// The limit is on the array as a whole, so the lengths of several memos add up.
        /// </summary>
        [TestMethod]
        public void TestUSeveralMemosAreMeasuredAsOneArray()
        {
            int one = LengthOf(new List<object> { Memo(HexOf(300)) });
            int two = LengthOf(new List<object> { Memo(HexOf(300)), Memo(HexOf(300)) });

            Assert.AreEqual(one * 2, two);
        }

        /// <summary>
        /// An already-built JSON array is measured as the same value as the typed collection.
        /// </summary>
        [TestMethod]
        public void TestUAJsonArrayIsMeasuredLikeTheCollectionItCameFrom()
        {
            List<object> memos = new List<object> { Memo(HexOf(40), "AB", TextPlainHex) };
            System.Text.Json.Nodes.JsonNode node =
                System.Text.Json.JsonSerializer.SerializeToNode(memos, Xrpl.Client.Json.XrplJsonOptions.Default);

            Assert.AreEqual(LengthOf(memos), LengthOf(node));
        }

        /// <summary>
        /// Nothing to measure is not a length of zero: the caller must be able to tell them apart.
        /// </summary>
        [TestMethod]
        public void TestUNothingToMeasureIsNotALength()
        {
            AssertNotMeasurable(null);
            AssertNotMeasurable(new List<object>());
            AssertNotMeasurable(new List<object> { "x" });
            AssertNotMeasurable("x");
            AssertNotMeasurable(new List<object> { Memo(HexOf(4)), "x" });
            AssertNotMeasurable(new List<object> { Memo("not hex") });
            AssertNotMeasurable(new List<object> { Memo("ABC") });
            AssertNotMeasurable(new List<object> { Memo(HexOf(4), memoFormat: "zz") });
        }

        /// <summary>
        /// Every way a member can be unserializable ends as "not measurable", not as an exception,
        /// whichever type the codec fails with.
        /// </summary>
        [TestMethod]
        public void TestUMembersTheCodecCannotReadAreNotMeasurable()
        {
            AssertNotMeasurable(new List<object>
            {
                new Dictionary<string, object> { { "Memo", new Dictionary<string, object> { { "MemoData", 5 } } } },
            });
            AssertNotMeasurable(new List<object>
            {
                new Dictionary<string, object> { { "Memo", new Dictionary<string, object> { { "MemoData", null } } } },
            });
            AssertNotMeasurable(new List<object>
            {
                new Dictionary<string, object>
                {
                    { "Memo", new Dictionary<string, object> { { "MemoData", new Dictionary<string, object>() } } },
                },
            });
        }

        /// <summary>
        /// A JSON null below the memo's direct members fails inside the codec with its own
        /// <see cref="NullReferenceException"/>; that is "not measurable" too.
        /// </summary>
        [TestMethod]
        public void TestUANullDeeperInAMemoIsNotMeasurable()
        {
            List<object> nullInNestedObject = new List<object>
            {
                new Dictionary<string, object>
                {
                    {
                        "Memo", new Dictionary<string, object>
                        {
                            { "Memo", new Dictionary<string, object> { { "MemoData", null } } },
                        }
                    },
                },
            };
            List<object> nullInNestedArray = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "Memo", new Dictionary<string, object> { { "Memos", new List<object> { null } } } },
                },
            };

            AssertNotMeasurable(nullInNestedObject);
            AssertNotMeasurable(nullInNestedArray);
            MemoRules.Validate(nullInNestedObject);
            MemoRules.Validate(nullInNestedArray);
        }

        /// <summary>
        /// A field past the serializer's own cap parses, then fails when written. That is not a
        /// length and not an exception either: the codec reports the real cause when the blob is built.
        /// </summary>
        [TestMethod]
        public void TestUAMemoFieldPastTheSerializerCapIsNotMeasurable()
        {
            const int OverTheCap = 918_745;
            List<object> dictionaries = new List<object> { Memo(HexOf(OverTheCap)) };
            List<MemoWrapper> typed = new List<MemoWrapper>
            {
                new MemoWrapper { Memo = new Xrpl.Models.Transactions.Memo { MemoData = HexOf(OverTheCap) } },
            };

            AssertNotMeasurable(dictionaries);
            Assert.IsFalse(MemoRules.TryGetSerializedLength(typed, out int typedLength));
            Assert.AreEqual(0, typedLength);
            MemoRules.Validate(dictionaries);
        }

        /// <summary>
        /// Refused while parsing by a nested type that reads the value itself: not a length, and not
        /// an exception either.
        /// </summary>
        [TestMethod]
        public void TestUABadMptAmountInAMemoIsNotMeasurable()
        {
            // Refused while parsing: a nested Amount whose MPT value is not a number.
            List<object> badMptAmount = new List<object>
            {
                new Dictionary<string, object>
                {
                    {
                        "Memo", new Dictionary<string, object>
                        {
                            {
                                "Amount", new Dictionary<string, object>
                                {
                                    { "mpt_issuance_id", new string('0', 48) },
                                    { "value", "abc" },
                                }
                            },
                        }
                    },
                },
            };

            AssertNotMeasurable(badMptAmount);
            MemoRules.Validate(badMptAmount);
        }

        /// <summary>
        /// Parses, then the codec refuses it when writing: a path set holding an empty path.
        /// </summary>
        [TestMethod]
        public void TestUAnEmptyPathInAMemoIsNotMeasurable()
        {
            List<object> emptyPath = new List<object>
            {
                new Dictionary<string, object>
                {
                    {
                        "Memo", new Dictionary<string, object>
                        {
                            { "Paths", new List<object> { new List<object>() } },
                        }
                    },
                },
            };

            AssertNotMeasurable(emptyPath);
            MemoRules.Validate(emptyPath);
        }

        /// <summary>
        /// The typed model measures the same as the dictionary shape, so a caller that holds
        /// <see cref="MemoWrapper"/>s needs no conversion and gets the same <c>n + 17</c>.
        /// </summary>
        [TestMethod]
        public void TestUTypedMemosAreMeasuredLikeTheDictionaryShape()
        {
            foreach (int dataBytes in new[] { 10, 193, 1007 })
            {
                List<MemoWrapper> typed = new List<MemoWrapper>
                {
                    new MemoWrapper { Memo = new Xrpl.Models.Transactions.Memo { MemoData = HexOf(dataBytes), MemoFormat = TextPlainHex } },
                };
                List<object> dictionaries = new List<object> { Memo(HexOf(dataBytes), memoFormat: TextPlainHex) };

                Assert.IsTrue(MemoRules.TryGetSerializedLength(typed, out int typedLength));
                Assert.AreEqual(LengthOf(dictionaries), typedLength);
            }

            List<MemoWrapper> dataOnly = new List<MemoWrapper>
            {
                new MemoWrapper { Memo = new Xrpl.Models.Transactions.Memo { MemoData = HexOf(1019) } },
            };
            Assert.IsTrue(MemoRules.TryGetSerializedLength(dataOnly, out int dataOnlyLength));
            Assert.AreEqual(MemoRules.MaxSerializedLength, dataOnlyLength, "a missing MemoType/MemoFormat is not written");
        }

        /// <summary>
        /// The typed overload has the same "nothing to measure" answers.
        /// </summary>
        [TestMethod]
        public void TestUTypedNothingToMeasureIsNotALength()
        {
            Assert.IsFalse(MemoRules.TryGetSerializedLength((IEnumerable<MemoWrapper>)null, out int none));
            Assert.AreEqual(0, none);
            Assert.IsFalse(MemoRules.TryGetSerializedLength(new List<MemoWrapper>(), out int empty));
            Assert.AreEqual(0, empty);
            Assert.IsFalse(MemoRules.TryGetSerializedLength(new List<MemoWrapper> { new MemoWrapper() }, out int noMemo));
            Assert.AreEqual(0, noMemo);
            Assert.IsFalse(MemoRules.TryGetSerializedLength(
                new List<MemoWrapper> { new MemoWrapper { Memo = new Xrpl.Models.Transactions.Memo { MemoData = "not hex" } } },
                out int notHex));
            Assert.AreEqual(0, notHex);
        }

        /// <summary>
        /// Memo elements the codec fails on, keyed by what is wrong with them. The failures come
        /// from different parts of the codec in unrelated exception types, which is why the
        /// measurement does not list the types it expects.
        /// </summary>
        private static readonly Dictionary<string, string> MemosTheCodecFailsOn = new Dictionary<string, string>
        {
            ["amount value not a number"] =
                "{\"Memo\":{\"Amount\":{\"value\":\"abc\",\"currency\":\"USD\",\"issuer\":\"rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh\"}}}",
            ["hash of the wrong length"] = "{\"Memo\":{\"LedgerHash\":\"AB\"}}",
            ["currency of the wrong length"] = "{\"Memo\":{\"TakerPaysCurrency\":\"AB\"}}",
            ["hash in a vector of the wrong length"] = "{\"Memo\":{\"Hashes\":[\"AB\"]}}",
            ["MPT issuance id of the wrong length"] = "{\"Memo\":{\"Amount\":{\"mpt_issuance_id\":\"AB\",\"value\":\"1\"}}}",
            ["unknown transaction type"] = "{\"Memo\":{\"TransactionType\":\"Nope\"}}",
            ["empty path in a path set"] = "{\"Memo\":{\"Paths\":[[]]}}",
            ["MPT amount value not a number"] =
                "{\"Memo\":{\"Amount\":{\"mpt_issuance_id\":\"" + new string('0', 48) + "\",\"value\":\"abc\"}}}",
            ["field past the serializer cap"] = "{\"Memo\":{\"MemoData\":\"" + HexOf(918_745) + "\"}}",
            ["null member"] = "{\"Memo\":{\"MemoData\":null}}",
            ["null in a nested object"] = "{\"Memo\":{\"Memo\":{\"MemoData\":null}}}",
            ["null in a nested array"] = "{\"Memo\":{\"Memos\":[null]}}",
        };

        public static IEnumerable<object[]> MemosTheCodecFailsOnNames()
        {
            foreach (string name in MemosTheCodecFailsOn.Keys)
            {
                yield return new object[] { name };
            }
        }

        /// <summary>
        /// Whatever the codec throws on a memo's content, the measurement answers "nothing to
        /// measure" and <see cref="MemoRules.Validate"/> stays quiet: the codec reports the cause
        /// when the transaction is built.
        /// </summary>
        /// <remarks>
        /// Rows are named rather than carrying the JSON, so the 1.8 MB row does not become a test name.
        /// </remarks>
        [TestMethod]
        [DynamicData(nameof(MemosTheCodecFailsOnNames))]
        public void TestUContentTheCodecFailsOnIsNotMeasurable(string failure)
        {
            System.Text.Json.Nodes.JsonNode memos =
                System.Text.Json.Nodes.JsonNode.Parse("[" + MemosTheCodecFailsOn[failure] + "]");

            AssertNotMeasurable(memos);
            MemoRules.Validate(memos);
        }

        private sealed class MemoWithAThrowingGetter
        {
            public string MemoData => throw new InvalidOperationException("getter fails");
        }

        /// <summary>
        /// An object System.Text.Json cannot convert to JSON never reaches the codec; the
        /// measurement answers "nothing to measure" for it as well, whatever the serializer throws.
        /// </summary>
        [TestMethod]
        public void TestUAnObjectTheSerializerCannotConvertIsNotMeasurable()
        {
            // System.Text.Json refuses a NaN double with ArgumentException.
            AssertNotMeasurable(new List<object>
            {
                new Dictionary<string, object> { { "Memo", new Dictionary<string, object> { { "MemoData", double.NaN } } } },
            });

            // A getter's own exception passes through the serializer unwrapped.
            AssertNotMeasurable(new List<object>
            {
                new Dictionary<string, object> { { "Memo", new MemoWithAThrowingGetter() } },
            });
        }

        private static void AssertNotMeasurable(object memos)
        {
            Assert.IsFalse(MemoRules.TryGetSerializedLength(memos, out int length));
            Assert.AreEqual(0, length, "length is zero whenever there is no measurement");
        }
    }
}
