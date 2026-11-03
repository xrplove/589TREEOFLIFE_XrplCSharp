using System;
using System.Globalization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// <see cref="LedgerAmountGrid"/>: the amounts the ledger can hold. Expectations are literals on
/// purpose; computing them with the function under test would check the formula, not the code.
/// </summary>
[TestClass]
public class TestULedgerAmountGrid
{
    private static decimal D(string raw) =>
        decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);

    [TestMethod]
    public void TestUXrpIsCutToWholeDrops()
    {
        Assert.AreEqual(1.234_567m, LedgerAmountGrid.RoundDown(D("1.2345678"), isXrp: true));
        Assert.AreEqual(1.234_568m, LedgerAmountGrid.RoundUp(D("1.2345678"), isXrp: true));
    }

    [TestMethod]
    public void TestUAnAmountAlreadyOnTheGridIsLeftExactlyAsItIs()
    {
        // Both directions must be idempotent on the grid, or rounding twice would drift the amount.
        Assert.AreEqual(1.234_567m, LedgerAmountGrid.RoundDown(1.234_567m, isXrp: true));
        Assert.AreEqual(1.234_567m, LedgerAmountGrid.RoundUp(1.234_567m, isXrp: true));
    }

    [TestMethod]
    [DataRow("1.234567890123456789", "1.234567890123456", "1.234567890123457")]
    [DataRow("199.6007984031936127744510978", "199.6007984031936", "199.6007984031937")]
    public void TestUIssuedAmountsKeepTheMantissaTheCodecCanHold(string raw, string down, string up)
    {
        // Fifteen places counted from the most significant digit: fifteen after the point for
        // units, thirteen for hundreds; sixteen significant digits either way, the mantissa's top.
        Assert.AreEqual(D(down), LedgerAmountGrid.RoundDown(D(raw), isXrp: false));
        Assert.AreEqual(D(up), LedgerAmountGrid.RoundUp(D(raw), isXrp: false));
    }

    [TestMethod]
    public void TestUSmallAmountsGetMoreDecimalPlacesRatherThanVanishing()
    {
        Assert.AreEqual(
            D("0.0000001234567890123456"),
            LedgerAmountGrid.RoundDown(D("0.000000123456789012345678"), isXrp: false));
    }

    [TestMethod]
    public void TestUADecimalRunningOutOfScaleIsClampedRatherThanOverflowing()
    {
        // A decimal has 28 places; a value near 1e-19 asks for more and must hit the type's limit.
        decimal tiny = D("0.00000000000000000012345678901234567");

        Assert.AreEqual(D("0.0000000000000000001234567890123456"), LedgerAmountGrid.RoundDown(tiny, isXrp: false));
    }

    [TestMethod]
    public void TestUALargeAmountKeepsTheFewPlacesItHasRoom()
    {
        Assert.AreEqual(D("999999999999999.9"), LedgerAmountGrid.RoundDown(D("999999999999999.9"), isXrp: false));
        Assert.AreEqual(D("999999999999999.9"), LedgerAmountGrid.RoundUp(D("999999999999999.9"), isXrp: false));
    }

    [TestMethod]
    [DataRow("12345678901234567", "12345678901234560", "12345678901234570")]
    [DataRow("1234567890123456789", "1234567890123456000", "1234567890123457000")]
    public void TestUBeyondTheMantissaTheGridStepGrowsPastOne(string raw, string down, string up)
    {
        // The mantissa is [1e15, 1e16): from 1e16 only multiples of 10, 100, ... are representable.
        Assert.AreEqual(D(down), LedgerAmountGrid.RoundDown(D(raw), isXrp: false));
        Assert.AreEqual(D(up), LedgerAmountGrid.RoundUp(D(raw), isXrp: false));
    }

    [TestMethod]
    public void TestUNothingKeepsMoreThanTheCodecCanHold()
    {
        foreach (string raw in new[] { "9999999999999999", "12345678901234567", "999999999999999999999" })
        {
            decimal down = LedgerAmountGrid.RoundDown(D(raw), isXrp: false);
            string digits = down.ToString("0", CultureInfo.InvariantCulture).TrimStart('-').TrimEnd('0');

            Assert.IsLessThanOrEqualTo(16, digits.Length, $"{raw} became {down}: {digits.Length} significant digits.");
        }
    }

    [TestMethod]
    public void TestUZeroPassesThroughUntouched()
    {
        Assert.AreEqual(0m, LedgerAmountGrid.RoundDown(0m, isXrp: true));
        Assert.AreEqual(0m, LedgerAmountGrid.RoundUp(0m, isXrp: false));
    }

    [TestMethod]
    public void TestUArithmeticNoiseDoesNotBuyAWholeStep()
    {
        // Summing a route's contributions leaves units in the last place; rounding that up must not
        // turn 400000 into 400000.000001.
        Assert.AreEqual(400_000m, LedgerAmountGrid.RoundUp(400_000m + D("1e-16"), isXrp: true));
        Assert.AreEqual(1m, LedgerAmountGrid.RoundUp(1m + D("1e-13"), isXrp: true));
    }

    [TestMethod]
    public void TestUARealFractionOfAStepStillBuysOne()
    {
        Assert.AreEqual(D("1.000001"), LedgerAmountGrid.RoundUp(D("1.0000005"), isXrp: true));
        Assert.AreEqual(D("1.000001"), LedgerAmountGrid.RoundUp(D("1.0000001"), isXrp: true));
    }

    [TestMethod]
    public void TestUTheNoiseThresholdSitsFarBelowTheLedgersOwnResolution()
    {
        Assert.AreEqual(1m, LedgerAmountGrid.RoundUp(1m + D("1e-13"), isXrp: true));
        Assert.AreEqual(D("1.000001"), LedgerAmountGrid.RoundUp(1m + D("1e-12"), isXrp: true));
    }

    [TestMethod]
    public void TestUSubDropAmountsAreRecognisedAsUnsendable()
    {
        Assert.IsTrue(LedgerAmountGrid.IsBelowSmallest(D("0.0000005"), isXrp: true));
        Assert.IsFalse(LedgerAmountGrid.IsBelowSmallest(LedgerAmountGrid.SmallestXrp, isXrp: true));
        Assert.IsFalse(LedgerAmountGrid.IsBelowSmallest(0m, isXrp: true));
        Assert.IsFalse(LedgerAmountGrid.IsBelowSmallest(-0.0000005m, isXrp: true));
    }

    [TestMethod]
    public void TestUIssuedCurrenciesHaveNoSmallestAmountAtAll()
    {
        Assert.IsFalse(LedgerAmountGrid.IsBelowSmallest(D("1e-20"), isXrp: false));
    }

    [TestMethod]
    public void TestUTheGridIsSymmetricAroundZero()
    {
        Assert.AreEqual(-0.123457m, LedgerAmountGrid.RoundUp(-0.1234567m, isXrp: true));
        Assert.AreEqual(-0.123456m, LedgerAmountGrid.RoundDown(-0.1234567m, isXrp: true));
        Assert.AreEqual(D("-12345678901234570"), LedgerAmountGrid.RoundUp(D("-12345678901234567"), isXrp: false));
        Assert.AreEqual(D("-12345678901234560"), LedgerAmountGrid.RoundDown(D("-12345678901234567"), isXrp: false));
    }

    [TestMethod]
    public void TestUStepGrowsPastOneAboveTheMantissa()
    {
        Assert.AreEqual(10m, LedgerAmountGrid.Step(12345678901234567m, isXrp: false));
        Assert.AreEqual(100m, LedgerAmountGrid.Step(D("123456789012345678"), isXrp: false));
        Assert.AreEqual(1e-15m, LedgerAmountGrid.Step(1m, isXrp: false));
        Assert.AreEqual(0.000001m, LedgerAmountGrid.Step(5m, isXrp: true));
        Assert.AreEqual(1m, LedgerAmountGrid.Step(1_234_567_890_123_456m, isXrp: false));
        Assert.AreEqual(0.1m, LedgerAmountGrid.Step(123_456_789_012_345.6m, isXrp: false));
    }

    [TestMethod]
    public void TestUStepIgnoresTheSign()
    {
        Assert.AreEqual(10m, LedgerAmountGrid.Step(D("-12345678901234567"), isXrp: false));
    }

    [TestMethod]
    public void TestUStepOfTheSmallestDecimalStopsAtTheDecimalScale()
    {
        Assert.AreEqual(1e-28m, LedgerAmountGrid.Step(D("1e-28"), isXrp: false));
    }

    [TestMethod]
    [Timeout(5000)]
    public void TestUZeroHasNoGridStep()
    {
        // Zero has no most significant digit; a scan for one would never end. Both kinds throw.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LedgerAmountGrid.Step(0m, isXrp: false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LedgerAmountGrid.Step(0m, isXrp: true));
    }

    [TestMethod]
    public void TestUTheTopOfDecimalStaysRepresentable()
    {
        decimal down = LedgerAmountGrid.RoundDown(decimal.MaxValue, isXrp: false);

        string digits = down.ToString("0", CultureInfo.InvariantCulture).TrimEnd('0');

        Assert.IsLessThanOrEqualTo(16, digits.Length);
        Assert.IsLessThanOrEqualTo(decimal.MaxValue, down);
        Assert.AreEqual(D("79228162514264330000000000000"), down);
    }

    [TestMethod]
    public void TestUAHugeXrpAmountIsAlreadyOnTheDropGrid()
    {
        // Past about 7.9e22 a decimal keeps at most five fractional digits, so scaling by 1e6 would
        // overflow for an amount that needs no rounding at all.
        decimal huge = D("79228162514264337593543.95033");

        Assert.AreEqual(huge, LedgerAmountGrid.RoundDown(huge, isXrp: true));
        Assert.AreEqual(huge, LedgerAmountGrid.RoundUp(huge, isXrp: true));
    }

    [TestMethod]
    public void TestUResidueBelowHalfAMillionthOfAStepCountsAsZero()
    {
        // The scaled value is rounded to six places before the cut, so a figure within 5e-7 of a
        // step lands on it even when rounding down; this pins the size of that window.
        Assert.AreEqual(1.234_568m, LedgerAmountGrid.RoundDown(D("1.23456799999999"), isXrp: true));
        Assert.AreEqual(1m, LedgerAmountGrid.RoundDown(1m - D("1e-23"), isXrp: false));
        Assert.AreEqual(D("1.234567"), LedgerAmountGrid.RoundDown(D("1.2345679"), isXrp: true));
        Assert.AreEqual(D("0.9999999999999999"), LedgerAmountGrid.RoundDown(1m - D("1e-17"), isXrp: false));
        Assert.AreEqual(D("0.9999999999999"), LedgerAmountGrid.RoundDown(1m - D("1e-13"), isXrp: false));
    }

    [TestMethod]
    public void TestUResidueWindowIsSymmetricForRoundUp()
    {
        Assert.AreEqual(1.234_567m, LedgerAmountGrid.RoundUp(D("1.23456700000001"), isXrp: true));
        Assert.AreEqual(D("1.234569"), LedgerAmountGrid.RoundUp(D("1.2345681"), isXrp: true));
    }

    [TestMethod]
    public void TestURoundUpAtTheTopOfDecimalOverflows()
    {
        Assert.ThrowsExactly<OverflowException>(() => LedgerAmountGrid.RoundUp(decimal.MaxValue, isXrp: false));
        Assert.ThrowsExactly<OverflowException>(() => LedgerAmountGrid.RoundUp(decimal.MinValue, isXrp: false));
    }

    [TestMethod]
    public void TestURoundDownAtTheBottomOfDecimalStaysRepresentable()
    {
        Assert.AreEqual(D("-79228162514264330000000000000"), LedgerAmountGrid.RoundDown(decimal.MinValue, isXrp: false));
    }

    [TestMethod]
    public void TestUANegativeHugeXrpAmountIsReturnedUnchanged()
    {
        Assert.AreEqual(-decimal.MaxValue, LedgerAmountGrid.RoundDown(-decimal.MaxValue, isXrp: true));
        Assert.AreEqual(-decimal.MaxValue, LedgerAmountGrid.RoundUp(-decimal.MaxValue, isXrp: true));
    }
}
