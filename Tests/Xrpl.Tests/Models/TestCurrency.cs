using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Globalization;

using System;

using Xrpl.Amounts;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCurrency
    {
        #region Amounts outside decimal's range - issue #148

        private static Currency Iou(string value) =>
            new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = value };

        /// <summary>
        /// An amount too large for <c>decimal</c> is refused, whichever sign it carries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These used to do two different things. A positive one clamped to <c>decimal.MaxValue</c>
        /// and said nothing, so a balance of 1e96 was answered with 7.9e28 - wrong by 67 orders of
        /// magnitude, and wrong in a way that flowed onward into the caller's arithmetic. A
        /// negative one threw <c>FormatException</c>, because the fallback parse was missing
        /// <c>AllowLeadingSign</c> and could not read the minus.
        /// </para>
        /// <para>
        /// The threshold is not near the ledger's ceiling: 1e29 is barely above
        /// <c>decimal.MaxValue</c> and already unreachable.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_OutsideDecimalRange_Throws()
        {
            foreach (string value in new[]
                     {
                         "1e29", "-1e29",
                         "9e80", "-9e80",
                         "9999999999999999e80", "-9999999999999999e80",
                     })
            {
                Assert.ThrowsExactly<AmountOutOfRangeException>(
                    () => _ = Iou(value).ValueAsNumber,
                    $"'{value}' is a legitimate ledger amount that decimal cannot hold.");
            }
        }

        /// <summary>
        /// The exception carries the amount as the node sent it.
        /// </summary>
        /// <remarks>
        /// The point of refusing rather than clamping is that the real figure is still available;
        /// an exception that only said "too big" would trade one lost value for another.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_OutOfRangeException_CarriesTheOriginalValue()
        {
            AmountOutOfRangeException error = Assert.ThrowsExactly<AmountOutOfRangeException>(
                () => _ = Iou("-9999999999999999e80").ValueAsNumber);

            Assert.AreEqual("-9999999999999999e80", error.Value);
            Assert.Contains("-9999999999999999e80", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Negative amounts inside the range still parse, which is what makes the bound the bug.
        /// </summary>
        /// <remarks>
        /// Without this the test above would pass on an implementation that simply refused every
        /// negative amount - and negative balances are ordinary: a <c>RippleState</c> balance is
        /// negative from the low account's side.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_NegativeInsideRange_StillParses()
        {
            Assert.AreEqual(-100m, Iou("-100").ValueAsNumber);
            Assert.AreEqual(-0.00000000015m, Iou("-1.5e-10").ValueAsNumber);
            Assert.AreEqual(-79228162514264337593543950335m, Iou("-79228162514264337593543950335").ValueAsNumber);
        }

        /// <summary>
        /// An amount too small for <c>decimal</c> becomes zero rather than throwing.
        /// </summary>
        /// <remarks>
        /// The asymmetry with overflow is deliberate. The ledger goes down to 1e-81 and decimal
        /// stops near 1e-28, but a balance that small is zero at any scale a caller can act on, so
        /// failing over it would cost more than it protects. Overflow is the opposite: the number
        /// that would be returned is wrong by orders of magnitude and unsafe to use.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_BelowDecimalPrecision_IsZeroNotAnError()
        {
            Assert.AreEqual(0m, Iou("1e-96").ValueAsNumber);
            Assert.AreEqual(0m, Iou("-9999999999999999e-96").ValueAsNumber);
        }

        /// <summary>
        /// Something that is not a number is still a format error, not an out-of-range one.
        /// </summary>
        /// <remarks>
        /// The two are told apart by whether <c>double</c> can read the string: it spans the whole
        /// ledger range, so it succeeds exactly when the value is real and decimal merely cannot
        /// hold it. Reporting both the same way would hide a malformed response behind a message
        /// about magnitude.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_NotANumber_IsAFormatError()
        {
            Assert.ThrowsExactly<FormatException>(() => _ = Iou("abc").ValueAsNumber);
            Assert.ThrowsExactly<FormatException>(() => _ = Iou("1.2.3").ValueAsNumber);
            Assert.ThrowsExactly<FormatException>(() => _ = Iou(" 100 ").ValueAsNumber);
        }

        /// <summary>
        /// A non-finite string is not a quantity too large, and must not be reported as one.
        /// </summary>
        /// <remarks>
        /// <c>double.TryParse</c> accepts <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> whatever
        /// <c>NumberStyles</c> it is handed, because those symbols are matched separately from the
        /// numeric ones. Since the check that separates "will not fit" from "is not a number" runs
        /// through <c>double</c>, without a finiteness test these would come back as
        /// <see cref="AmountOutOfRangeException"/> - a confident answer about magnitude for a
        /// string that has none.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_NonFiniteStrings_AreFormatErrorsNotRangeErrors()
        {
            foreach (string value in new[] { "NaN", "Infinity", "-Infinity" })
            {
                Assert.ThrowsExactly<FormatException>(
                    () => _ = Iou(value).ValueAsNumber,
                    $"'{value}' is not a quantity at all, let alone one that is too large.");
            }
        }

        /// <summary>
        /// An out-of-range numerator is refused even when the denominator is zero.
        /// </summary>
        /// <remarks>
        /// <c>Offer.AmountEach</c> returns zero when <c>TakerPays</c> is zero. While it read the
        /// two sides lazily, that early return meant an unrepresentable <c>TakerGets</c> slipped
        /// through unnoticed - so whether the documented exception appeared depended on the value
        /// of an unrelated field, which is not a contract anyone can hold you to.
        /// </remarks>
        [TestMethod]
        public void AmountEach_OutOfRangeNumerator_ThrowsEvenWithAZeroDenominator()
        {
            Offer offer = new Offer { TakerGets = Iou("9e80"), TakerPays = Iou("0") };

            Assert.ThrowsExactly<AmountOutOfRangeException>(() => _ = offer.AmountEach);
        }

        /// <summary>
        /// A zero denominator on its own still yields zero rather than dividing.
        /// </summary>
        [TestMethod]
        public void AmountEach_ZeroDenominator_IsZero()
        {
            Offer offer = new Offer { TakerGets = Iou("100"), TakerPays = Iou("0") };

            Assert.AreEqual(0m, offer.AmountEach);
        }

        /// <summary>
        /// The two properties that compute rather than read fail the same single way.
        /// </summary>
        /// <remarks>
        /// <c>GetBalanceChanges</c> subtracts two balances and <c>Offer.AmountEach</c> divides two
        /// amounts, and both used to have a second failure behind the first: a clamped
        /// <c>decimal.MaxValue</c> would go on to throw <c>OverflowException</c> from the
        /// arithmetic, or - worse for the order book - return a plausible exchange rate that was
        /// wrong by 67 orders of magnitude without throwing at all.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_ArithmeticOnOutOfRangeAmounts_FailsAtTheSource()
        {
            Assert.ThrowsExactly<AmountOutOfRangeException>(
                () => _ = Iou("9e80").ValueAsNumber - Iou("-100").ValueAsNumber,
                "This used to be an OverflowException from subtracting a clamped MaxValue.");

            Offer offer = new Offer { TakerGets = Iou("9e80"), TakerPays = Iou("1") };

            Assert.ThrowsExactly<AmountOutOfRangeException>(
                () => _ = offer.AmountEach,
                "This used to return decimal.MaxValue as an exchange rate, silently.");
        }

        /// <summary>
        /// <c>ToString</c> shows the amount instead of failing on it.
        /// </summary>
        /// <remarks>
        /// By convention <see cref="object.ToString"/> does not throw, and the places it is reached
        /// from - logging, string interpolation, a debugger's watch window - are exactly where
        /// someone would be while working out why an amount is unusual. Letting the getter throw
        /// through it would hide the value at the moment it is most wanted.
        /// </remarks>
        [TestMethod]
        public void ToString_OutOfRangeAmount_ShowsTheRawValue()
        {
            Assert.AreEqual("USD: 9e80", Iou("9e80").ToString());
            Assert.AreEqual("USD: -9e80", Iou("-9e80").ToString());
            Assert.AreEqual("USD: NaN", Iou("NaN").ToString());

            // Anything it can render, it still renders the same way.
            Assert.AreEqual("USD: 100", Iou("100").ToString());
        }

        /// <summary>
        /// Writing the largest <see cref="decimal"/> produces an amount that can be read back.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The setter formats with <c>G16</c> to keep the ledger's sixteen significant digits,
        /// rounding to nearest - which is what rippled does, so it is not a place to be clever.
        /// The one input that could not serve is the top of <see cref="decimal"/>'s own range,
        /// where rounding to nearest rounds <em>up</em>, past what the type holds: the SDK wrote a
        /// string it then refused to read.
        /// </para>
        /// <para>
        /// There the sixteenth digit is truncated instead. Truncating cannot overflow, because
        /// dropping digits only ever moves a number toward zero.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_WritingTheLargestDecimal_CanBeReadBack()
        {
            foreach (decimal edge in new[] { decimal.MaxValue, decimal.MinValue })
            {
                Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
                currency.ValueAsNumber = edge;

                decimal readBack = currency.ValueAsNumber;

                Assert.IsTrue(
                    Math.Abs(readBack) <= Math.Abs(edge),
                    $"Truncation moves toward zero; {currency.Value} came back as {readBack}.");
            }
        }

        /// <summary>
        /// A dust remainder survives being read, written and encoded for the ledger.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Balances like <c>0.000000000000000001</c> do arrive from the network, and the SDK has to
        /// be able to send one back. They are safe here because smallness is not the constraint -
        /// the ledger's limit is sixteen <em>significant</em> digits, and dust carries one.
        /// </para>
        /// <para>
        /// Pinned because the obvious way to bound precision destroys exactly these. Truncating to
        /// sixteen <em>decimal places</em> rather than significant digits turns <c>1e-18</c> into
        /// zero: a remainder would silently disappear, and the SDK would send nothing where a
        /// balance stood. This test fails if anyone reaches for that.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_DustRemainders_SurviveTheWholeRoundTrip()
        {
            foreach (string fromTheWire in new[]
                     {
                         "0.000000000000000001",          // 1e-18
                         "1e-18",
                         "0.0000000000000000000000001",   // 1e-25
                         "1e-28",                         // the smallest decimal holds
                         "-0.000000000000000001",
                     })
            {
                Currency incoming = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = fromTheWire };
                decimal amount = incoming.ValueAsNumber;

                Assert.AreNotEqual(0m, amount, $"'{fromTheWire}' must not read as nothing.");

                Currency outgoing = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
                outgoing.ValueAsNumber = amount;

                Assert.AreEqual(
                    amount,
                    outgoing.ValueAsNumber,
                    $"'{fromTheWire}' must survive being written back; it became '{outgoing.Value}'.");
            }
        }

        #endregion

        #region Round-trip ValueAsNumber (G16 fix verification)

        [TestMethod]
        public void ValueAsNumber_RoundTrip_16SignificantDigits_AmmLpToken()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "316227.7660168379" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("316227.7660168379", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_MaxMantissa()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "9999999999999999" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("9999999999999999", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_MinMantissa()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "1000000000000000" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("1000000000000000", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_15SignificantDigits()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "316227.766016838" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("316227.766016838", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_SmallValueWithLeadingZeros()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "0.001234567890123456" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("0.001234567890123456", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_ScientificNotation()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "1.234567890123456e10" };
            decimal number = currency.ValueAsNumber;
            Assert.AreEqual(12345678901.23456m, number);

            currency.ValueAsNumber = number;
            Assert.AreEqual("12345678901.23456", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_NegativeValue()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "-316227.7660168379" };
            decimal number = currency.ValueAsNumber;
            Assert.IsTrue(number < 0);

            currency.ValueAsNumber = number;
            Assert.AreEqual("-316227.7660168379", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_Zero()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "0" };
            decimal number = currency.ValueAsNumber;
            Assert.AreEqual(0m, number);

            currency.ValueAsNumber = number;
            Assert.AreEqual("0", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_EmptyString_ReturnsZero()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "" };
            Assert.AreEqual(0m, currency.ValueAsNumber);
        }

        [TestMethod]
        public void ValueAsNumber_NullValue_ReturnsZero()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = null };
            Assert.AreEqual(0m, currency.ValueAsNumber);
        }

        [TestMethod]
        public void ValueAsNumber_Setter_UsesG0ForXrp()
        {
            Currency currency = new Currency { CurrencyCode = "XRP" };
            currency.ValueAsNumber = 1500000m;
            Assert.AreEqual("1500000", currency.Value);
        }

        /// <summary>
        /// An amount already at the ledger's precision survives a round trip untouched.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This was called <c>NeverRoundsUp</c> and asserted that a round trip must not increase a
        /// value. That is not a property of the ledger: <c>G16</c> rounds to nearest, and so does
        /// rippled - <c>Number</c>'s default mode is <c>ToNearest</c> - so an amount carrying more
        /// than sixteen significant digits can legitimately come back larger. The old name
        /// promised an invariant the protocol does not have.
        /// </para>
        /// <para>
        /// It also could not have caught a violation. The value it used has exactly sixteen
        /// significant digits, so there is nothing for <c>G16</c> to round in either direction; the
        /// assertion passed for a value where it could not fail.
        /// </para>
        /// <para>
        /// What is worth pinning is the property that does hold: an amount the ledger could have
        /// sent goes out again unchanged.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_AtLedgerPrecision_RoundTripsUnchanged()
        {
            foreach (string atPrecision in new[]
                     {
                         "316227.7660168379",     // an AMM LP token amount
                         "9999999999999999",      // the largest mantissa
                         "1000000000000000",      // the smallest
                         "0.1234567890123457",
                     })
            {
                Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = atPrecision };
                decimal original = currency.ValueAsNumber;

                currency.ValueAsNumber = original;

                Assert.AreEqual(
                    original,
                    currency.ValueAsNumber,
                    $"'{atPrecision}' is already at ledger precision and must survive intact.");
            }
        }

        /// <summary>
        /// Beyond sixteen digits the value rounds to nearest, and may grow. That is the ledger's
        /// own behaviour, not a defect.
        /// </summary>
        /// <remarks>
        /// Stated as a test because the opposite was previously asserted, and because a reader who
        /// finds a value that grew will otherwise reach for the same wrong fix: rippled's
        /// <c>Number</c> defaults to <c>RoundingMode::ToNearest</c>, so truncating here would move
        /// the SDK away from the protocol rather than toward it.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_BeyondLedgerPrecision_RoundsToNearestAsTheLedgerDoes()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            currency.ValueAsNumber = 1.2345678901234565m;

            Assert.AreEqual("1.234567890123457", currency.Value);
            Assert.IsTrue(currency.ValueAsNumber > 1.2345678901234565m, "Rounding to nearest went up here, as it should.");
        }

        #endregion

        #region ValueAsXrp

        [TestMethod]
        public void ValueAsXrp_SetValue_ConvertsToDrops()
        {
            Currency currency = new Currency();
            currency.ValueAsXrp = 1.5m;
            Assert.AreEqual("XRP", currency.CurrencyCode);
            Assert.AreEqual("1500000", currency.Value);
        }

        [TestMethod]
        public void ValueAsXrp_GetValue_ConvertsFromDrops()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Value = "1500000" };
            Assert.AreEqual(1.5m, currency.ValueAsXrp);
        }

        [TestMethod]
        public void ValueAsXrp_NonXrpCurrency_ReturnsNull()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "100" };
            Assert.IsNull(currency.ValueAsXrp);
        }

        [TestMethod]
        public void ValueAsXrp_SetNull_SetsValueToZero()
        {
            Currency currency = new Currency();
            currency.ValueAsXrp = null;
            Assert.AreEqual("0", currency.Value);
        }

        [TestMethod]
        public void ValueAsXrp_EmptyValue_ReturnsNull()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Value = "" };
            Assert.IsNull(currency.ValueAsXrp);
        }

        #endregion

        #region Implicit operators

        [TestMethod]
        public void ImplicitOperator_FromString()
        {
            Currency currency = "100.5";
            Assert.AreEqual("100.5", currency.Value);
        }

        [TestMethod]
        public void ImplicitOperator_FromDecimal()
        {
            Currency currency = 316227.7660168379m;
            Assert.AreEqual("316227.7660168379", currency.Value);
        }

        [TestMethod]
        public void ImplicitOperator_FromDouble()
        {
            Currency currency = 100.5;
            Assert.AreEqual("100.5", currency.Value);
        }

        [TestMethod]
        public void ImplicitOperator_FromInt()
        {
            Currency currency = 42;
            Assert.AreEqual("42", currency.Value);
        }

        #endregion

        #region CurrencyExtensions

        [TestMethod]
        public void GetValue_XrpCurrency_ReturnsXrpValue()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Value = "1500000" };
            decimal? value = currency.GetValue();
            Assert.AreEqual(1.5m, value);
        }

        [TestMethod]
        public void GetValue_TokenCurrency_ReturnsRawValue()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "123.456789" };
            decimal? value = currency.GetValue();
            Assert.AreEqual(123.456789m, value);
        }

        [TestMethod]
        public void GetValue_WithRound_RoundsCorrectly()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "123.456789" };
            decimal? value = currency.GetValue(round: 2);
            Assert.AreEqual(123.46m, value);
        }

        [TestMethod]
        public void GetValue_NullCurrency_ReturnsNull()
        {
            Currency currency = null;
            decimal? value = currency.GetValue();
            Assert.IsNull(value);
        }

        [TestMethod]
        public void IsXrp_XrpWithoutIssuer_ReturnsTrue()
        {
            Currency currency = new Currency { CurrencyCode = "XRP" };
            Assert.IsTrue(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_XrpWithIssuer_ReturnsFalse()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Issuer = "rSomeIssuer" };
            Assert.IsFalse(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_TokenCurrency_ReturnsFalse()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Assert.IsFalse(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_NullCurrency_ReturnsFalse()
        {
            Currency currency = null;
            Assert.IsFalse(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_LowercaseXrpWithoutIssuer_ReturnsFalse()
        {
            // A lowercase "xrp" IOU is legal on the ledger; only the exact code "XRP" is reserved.
            Assert.IsFalse(new Currency { CurrencyCode = "xrp" }.IsXrp());
        }

        [TestMethod]
        public void IsXrp_MixedCaseXrp_ReturnsFalse()
        {
            Assert.IsFalse(new Currency { CurrencyCode = "Xrp" }.IsXrp());
        }

        [TestMethod]
        public void FromCurrency_LowercaseXrpWithoutIssuer_KeepsTheCodeAndIsStillXrpKind()
        {
            // Malformed input: the code is no longer normalized to "XRP", but the kind comes from
            // IssuedCurrency.IsXrp(), which looks only at the issuer and the MPT id.
            XrplAmount amount = XrplAmount.FromCurrency(new Currency { CurrencyCode = "xrp", Value = "1000000" });

            Assert.AreEqual("xrp", amount.Asset.Currency);
            Assert.IsNull(amount.Asset.Issuer);
            Assert.AreEqual(AmountKind.Xrp, amount.Kind);

            XrplAmount native = XrplAmount.FromCurrency(new Currency { CurrencyCode = "XRP", Value = "1000000" });
            Assert.AreEqual("XRP", native.Asset.Currency);
            Assert.AreEqual(AmountKind.Xrp, native.Kind);
        }

        [TestMethod]
        public void IsLpToken_LpCurrencyCode_ReturnsTrue()
        {
            Currency currency = new Currency
            {
                CurrencyCode = "03AB1234000000000000000000000000000000AB",
                Issuer = "rAmmIssuer"
            };
            Assert.IsTrue(currency.IsLpToken());
        }

        [TestMethod]
        public void IsLpToken_RegularToken_ReturnsFalse()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Assert.IsFalse(currency.IsLpToken());
        }

        [TestMethod]
        public void NormalizeCurrencyCode_ThreeCharCode_ReturnsSame()
        {
            Assert.AreEqual("USD", "USD".NormalizeCurrencyCode());
            Assert.AreEqual("EUR", "EUR".NormalizeCurrencyCode());
        }

        [TestMethod]
        public void NormalizeCurrencyCode_LpToken_ReturnsLpPrefix()
        {
            string lpCode = "03AB1234000000000000000000000000000000AB";
            string result = lpCode.NormalizeCurrencyCode();
            Assert.IsTrue(result.StartsWith("LP "));
        }

        #endregion

        #region Equals and operators

        [TestMethod]
        public void Equals_SameCurrencyAndIssuer_DifferentValue_ReturnsTrue()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "100" };
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "200" };
            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
        }

        [TestMethod]
        public void Equals_DifferentIssuer_ReturnsFalse()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer1" };
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer2" };
            Assert.IsFalse(a.Equals(b));
            Assert.IsTrue(a != b);
        }

        [TestMethod]
        public void Equals_DifferentCurrencyCode_ReturnsFalse()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Currency b = new Currency { CurrencyCode = "EUR", Issuer = "rIssuer" };
            Assert.IsFalse(a.Equals(b));
        }

        [TestMethod]
        public void OperatorEquals_NullLeft_ReturnsFalse()
        {
            Currency a = null;
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Assert.IsFalse(a == b);
        }

        [TestMethod]
        public void OperatorEquals_NullRight_ReturnsFalse()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Currency b = null;
            Assert.IsFalse(a == b);
        }

        [TestMethod]
        public void OperatorEquals_BothNull_ReturnsTrue()
        {
            Currency a = null;
            Currency b = null;
            Assert.IsTrue(a == b);
        }

        #endregion
    }
}
