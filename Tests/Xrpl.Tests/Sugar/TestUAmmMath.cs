using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using Xrpl.Sugar;

namespace XrplTests.Xrpl.Sugar;

/// <summary>
/// AMM deposit and withdrawal arithmetic, checked against what rippled computes - issue #133.
/// </summary>
/// <remarks>
/// The formulas are equations 3 and 7 from rippled's <c>AMMHelpers.cpp</c>, and the point of taking
/// them from there rather than from the widely quoted approximation is measurable: see
/// <see cref="TestUTheCirculatingApproximationIsWrongOnceThereIsAFee"/>.
/// </remarks>
[TestClass]
public class TestUAmmMath
{
    /// <summary>
    /// The figure from the report, and the reason this class exists.
    /// </summary>
    /// <remarks>
    /// A deposit the size of the pool at a 1% fee. rippled credits <c>0.41213·T</c>; the formula
    /// that circulates for this, <c>T·(√(1 + b·(1 − f/2)/B) − 1)</c>, says <c>0.41244·T</c>.
    /// </remarks>
    [TestMethod]
    public void TestUASingleAssetDepositMatchesTheNodesFigure()
    {
        decimal tokens = AmmMath.LPTokensForSingleAssetDeposit(
            poolBalance: 1_000_000m,
            deposit: 1_000_000m,
            lpTokenBalance: 1_000_000m,
            tradingFee: 1000);

        decimal asFractionOfT = tokens / 1_000_000m;

        Assert.AreEqual(
            0.41213m,
            Math.Round(asFractionOfT, 5),
            $"Equation 3 gives 0.41213·T for this pool; got {asFractionOfT}");
    }

    /// <summary>
    /// The approximation this replaces, shown to be wrong rather than asserted to be.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the two agree closely enough that a spot check does not tell them
    /// apart - 0.08% here - and because the error is always in the same direction: the
    /// approximation credits more tokens than the node will.
    /// </remarks>
    [TestMethod]
    public void TestUTheCirculatingApproximationIsWrongOnceThereIsAFee()
    {
        const decimal Pool = 1_000_000m;
        const uint Fee = 1000; // one per cent

        decimal exact = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, Fee) / Pool;

        // T * (sqrt(1 + b*(1 - f/2)/B) - 1), with b = B
        decimal f = AmmMath.TradingFeeFraction(Fee);
        decimal approximate = AmmMath.Sqrt(1m + (1m - f / 2m)) - 1m;

        Assert.AreEqual(0.41213m, Math.Round(exact, 5));
        Assert.AreEqual(0.41244m, Math.Round(approximate, 5));
        Assert.IsTrue(
            approximate > exact,
            "The approximation overstates the credit, which is the direction that disappoints a caller.");
    }

    /// <summary>
    /// Without a fee the two agree exactly, which is where the approximation came from.
    /// </summary>
    /// <remarks>
    /// This is what makes the difference easy to miss: the approximation is not a rough model of
    /// the wrong thing, it is the right formula with the fee handled loosely, so it is exact
    /// wherever there is no fee to handle.
    /// </remarks>
    [TestMethod]
    public void TestUWithoutAFeeTheTwoAgree()
    {
        const decimal Pool = 1_000_000m;

        decimal exact = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, tradingFee: 0) / Pool;
        decimal approximate = AmmMath.Sqrt(2m) - 1m;

        Assert.AreEqual(Math.Round(approximate, 20), Math.Round(exact, 20));
    }

    /// <summary>
    /// The auction slot holder trades at a tenth of the pool's fee, and is credited accordingly.
    /// </summary>
    /// <remarks>
    /// One of the two things the report names as making an otherwise correct estimate miss: the
    /// node computes the slot holder's deposits and withdrawals at <c>DiscountedFee</c>, so an
    /// estimate at the pool's fee is wrong for exactly the account most likely to be doing the
    /// estimating.
    /// </remarks>
    [TestMethod]
    public void TestUTheAuctionSlotHolderIsCreditedAtTheDiscountedFee()
    {
        const decimal Pool = 1_000_000m;

        Assert.AreEqual(100u, AmmMath.DiscountedTradingFee(1000), "A tenth of the pool's fee.");

        decimal atPoolFee = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, 1000);
        decimal atSlotFee = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, AmmMath.DiscountedTradingFee(1000));

        Assert.IsTrue(
            atSlotFee > atPoolFee,
            "A smaller fee means more tokens for the same deposit; estimating at the pool's fee shortchanges the slot holder.");
    }

    /// <summary>
    /// A withdrawal costs at least what the same deposit earned, and more once there is a fee.
    /// </summary>
    /// <remarks>
    /// Equations 3 and 7 are separate formulas, and a transcription error in either would most
    /// likely show up as this invariant breaking - putting an asset in and taking the same amount
    /// straight back out cannot be free, or the pool could be drained by doing it repeatedly.
    /// </remarks>
    [TestMethod]
    public void TestUARoundTripCostsTheFee()
    {
        const decimal Pool = 1_000_000m;
        const decimal Amount = 100_000m;
        const uint Fee = 1000;

        decimal earned = AmmMath.LPTokensForSingleAssetDeposit(Pool, Amount, Pool, Fee);
        decimal spent = AmmMath.LPTokensForSingleAssetWithdraw(Pool + Amount, Amount, Pool + earned, Fee);

        Assert.IsTrue(
            spent > earned,
            $"Depositing and withdrawing the same amount must cost the fee, but earned {earned} and spent {spent}.");
    }

    /// <summary>
    /// Without a fee the round trip is exactly neutral, which is what pins equation 7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deposit <c>b</c> into a pool of <c>B</c>, then take the same <c>b</c> straight back out of
    /// the pool it has become. With no fee to pay, that must return precisely the tokens it earned
    /// - the pool ends where it started, so the LP must too.
    /// </para>
    /// <para>
    /// This is here because a mutation survived without it. Equation 7 multiplies by the fee where
    /// equation 3 multiplies by <c>1 − fee</c> - rippled's <c>lpTokensIn</c> calls <c>getFee</c>
    /// where <c>lpTokensOut</c> calls <c>feeMult</c> - and swapping the two passed every other test
    /// here, including the round-trip inequality, which is too loose to notice. The identity below
    /// is not: with the wrong multiplier the two sides stop matching by a wide margin.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TestUWithoutAFeeTheRoundTripIsExactlyNeutral()
    {
        const decimal Pool = 1_000_000m;
        const decimal Amount = 250_000m;
        const decimal Tokens = 1_000_000m;

        decimal earned = AmmMath.LPTokensForSingleAssetDeposit(Pool, Amount, Tokens, tradingFee: 0);
        decimal spent = AmmMath.LPTokensForSingleAssetWithdraw(
            Pool + Amount,
            Amount,
            Tokens + earned,
            tradingFee: 0);

        Assert.AreEqual(
            Math.Round(earned, 18),
            Math.Round(spent, 18),
            $"With no fee the two equations must invert each other exactly; earned {earned}, spent {spent}.");
    }

    [TestMethod]
    public void TestUAProportionalDepositIsLimitedByWhicheverAssetRunsOutFirst()
    {
        decimal tokens = AmmMath.LPTokensForProportionalDeposit(
            poolBalance1: 1_000m,
            poolBalance2: 4_000m,
            deposit1: 100m,      // a tenth of the pool
            deposit2: 200m,      // a twentieth, and therefore the limit
            lpTokenBalance: 2_000m);

        Assert.AreEqual(100m, tokens, "frac is the smaller of the two ratios: 200/4000 = 0.05.");

        (decimal asset1, decimal asset2) = AmmMath.AssetsForProportionalDeposit(1_000m, 4_000m, 100m, 200m);

        Assert.AreEqual(50m, asset1, "Only half of what was offered on the first asset is taken.");
        Assert.AreEqual(200m, asset2, "All of the limiting one.");
    }

    [TestMethod]
    public void TestUAProportionalWithdrawReturnsBothSidesAtTheSameFraction()
    {
        (decimal asset1, decimal asset2) = AmmMath.AssetsForProportionalWithdraw(
            poolBalance1: 1_000m,
            poolBalance2: 4_000m,
            lpTokens: 500m,
            lpTokenBalance: 2_000m);

        Assert.AreEqual(250m, asset1);
        Assert.AreEqual(1_000m, asset2);
    }

    /// <summary>
    /// The square root keeps more digits than a double one would.
    /// </summary>
    /// <remarks>
    /// The reason the class does its own: <see cref="Math.Sqrt"/> carries 15 significant digits and
    /// the rest of the arithmetic carries 28, so using it would throw away precision at the one step
    /// where the formulas need it most.
    /// </remarks>
    [TestMethod]
    public void TestUTheSquareRootIsExactToDecimalPrecision()
    {
        decimal root = AmmMath.Sqrt(2m);

        Assert.IsTrue(
            Math.Abs(root * root - 2m) < 0.0000000000000000000000001m,
            $"√2 squared came back as {root * root}");

        double viaDouble = Math.Sqrt(2.0);
        Assert.IsTrue(
            Math.Abs(root - (decimal)viaDouble) > 0m,
            "If this matched the double result exactly there would be no point computing it separately.");
    }

    [TestMethod]
    public void TestUImpossibleInputsAreRefusedRatherThanReturningNonsense()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(0m, 10m, 100m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(100m, -1m, 100m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetWithdraw(100m, 101m, 100m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.AssetsForProportionalWithdraw(100m, 100m, 101m, 100m));
    }

    /// <summary>
    /// A fee in the wrong units is refused rather than quietly answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TradingFee</c> is in units of 1/100 000, and rippled caps it at 1000 - one per cent - in
    /// <c>kTradingFeeThreshold</c>. A caller who reaches for basis points or for whole per cent is
    /// out by a factor of ten or a hundred, and nothing in the arithmetic notices: at 5000 every
    /// intermediate value stays finite and a plausible, wrong number comes back. Only at 100 000
    /// does <c>1 - fee</c> reach zero and the division fail, and a <c>DivideByZeroException</c> is
    /// not what a caller should have to diagnose from.
    /// </para>
    /// <para>
    /// The bound is on the fee itself, so it holds even for an amount of zero - otherwise whether
    /// bad input is reported would depend on how much was being deposited.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TestUAFeeInTheWrongUnitsIsRefused()
    {
        Assert.AreEqual(1000u, AmmMath.TradingFeeThreshold);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(1_000m, 100m, 1_000m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetWithdraw(1_000m, 100m, 1_000m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.TradingFeeFraction(100_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.DiscountedTradingFee(1001));

        // Not conditional on there being an amount to compute.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(1_000m, 0m, 1_000m, tradingFee: 5000));

        // And the cap itself is allowed - an off-by-one here would refuse the highest legal pool.
        AmmMath.LPTokensForSingleAssetDeposit(1_000m, 100m, 1_000m, AmmMath.TradingFeeThreshold);
    }

    /// <summary>
    /// Equations 3 and 4 invert each other, which is the only cheap way to pin equation 4.
    /// </summary>
    /// <remarks>
    /// rippled derives equation 4 by solving equation 3 for the deposit, so the two must compose
    /// to the identity for any input. That derivation runs through a quadratic, and a sign or a
    /// factor lost anywhere in it breaks this while leaving a plausible-looking number behind.
    /// </remarks>
    [TestMethod]
    public void TestUTheDepositEquationsInvertEachOther()
    {
        const decimal Pool = 1_000_000m;
        const decimal Tokens = 1_000_000m;

        foreach (uint fee in new uint[] { 0, 1, 500, 1000 })
        {
            foreach (decimal deposit in new[] { 1m, 1_000m, 250_000m, 1_000_000m })
            {
                decimal tokens = AmmMath.LPTokensForSingleAssetDeposit(Pool, deposit, Tokens, fee);
                decimal back = AmmMath.SingleAssetDepositForLPTokens(Pool, tokens, Tokens, fee);

                Assert.AreEqual(
                    Math.Round(deposit, 12),
                    Math.Round(back, 12),
                    $"Depositing {deposit} at a fee of {fee} earns {tokens}, which should cost {deposit} to buy back - got {back}.");
            }
        }
    }

    /// <summary>
    /// And equations 7 and 8, the withdrawal pair.
    /// </summary>
    [TestMethod]
    public void TestUTheWithdrawEquationsInvertEachOther()
    {
        const decimal Pool = 1_000_000m;
        const decimal Tokens = 1_000_000m;

        foreach (uint fee in new uint[] { 0, 1, 500, 1000 })
        {
            foreach (decimal withdraw in new[] { 1m, 1_000m, 250_000m, 900_000m })
            {
                decimal tokens = AmmMath.LPTokensForSingleAssetWithdraw(Pool, withdraw, Tokens, fee);
                decimal back = AmmMath.SingleAssetWithdrawForLPTokens(Pool, tokens, Tokens, fee);

                Assert.AreEqual(
                    Math.Round(withdraw, 12),
                    Math.Round(back, 12),
                    $"Withdrawing {withdraw} at a fee of {fee} costs {tokens}, which should return {withdraw} - got {back}.");
            }
        }
    }

    /// <summary>
    /// Redeeming every token empties the pool, whatever the fee.
    /// </summary>
    /// <remarks>
    /// The input where equation 8's denominator comes closest to zero, and the answer is still
    /// exact: at <c>t1 = 1</c> the fraction is <c>(fee - 1)/(fee - 1)</c>. Worth its own test
    /// because a formula that is merely close would show it here first.
    /// </remarks>
    [TestMethod]
    public void TestURedeemingEveryTokenTakesTheWholePool()
    {
        Assert.AreEqual(1_000m, AmmMath.SingleAssetWithdrawForLPTokens(1_000m, 500m, 500m, 1000));
        Assert.AreEqual(1_000m, AmmMath.SingleAssetWithdrawForLPTokens(1_000m, 500m, 500m, 0));
    }

    /// <summary>
    /// The swap matches the closed form, and the fee is taken before the curve rather than after.
    /// </summary>
    /// <remarks>
    /// Two different things could be called "a 1% fee on a swap of 100": one takes 1 off the input
    /// and puts 99 through the curve, the other puts 100 through and takes 1% off the output.
    /// rippled does the first, and the two do not agree.
    /// </remarks>
    [TestMethod]
    public void TestUASwapTakesTheFeeOffTheInputBeforeTheCurve()
    {
        decimal withoutFee = AmmMath.SwapAssetIn(1_000m, 1_000m, 100m, 0);
        Assert.AreEqual(1_000m * 100m / 1_100m, withoutFee, "out = poolOut * in / (poolIn + in)");

        decimal withFee = AmmMath.SwapAssetIn(1_000m, 1_000m, 100m, 1000);
        Assert.AreEqual(1_000m * 99m / 1_099m, withFee, "99 goes through the curve, not 100.");

        decimal feeOnOutput = withoutFee * 0.99m;
        Assert.AreNotEqual(
            Math.Round(feeOnOutput, 12),
            Math.Round(withFee, 12),
            "Taking the fee off the output instead gives a different number, and is the natural mistake.");
    }

    /// <summary>
    /// Without a fee the swap leaves the constant product where it found it.
    /// </summary>
    [TestMethod]
    public void TestUAFreeSwapPreservesTheInvariant()
    {
        const decimal In = 1_000m;
        const decimal Out = 4_000m;

        decimal received = AmmMath.SwapAssetIn(In, Out, 250m, tradingFee: 0);

        Assert.AreEqual(
            Math.Round(In * Out, 8),
            Math.Round((In + 250m) * (Out - received), 8),
            "k must be unchanged when nothing is charged for the trade.");
    }

    /// <summary>
    /// The two halves of the swap invert each other exactly.
    /// </summary>
    [TestMethod]
    public void TestUTheSwapInvertsExactly()
    {
        foreach (uint fee in new uint[] { 0, 500, 1000 })
        {
            decimal received = AmmMath.SwapAssetIn(1_000m, 4_000m, 250m, fee);
            decimal cost = AmmMath.SwapAssetOut(1_000m, 4_000m, received, fee);

            Assert.AreEqual(
                Math.Round(250m, 12),
                Math.Round(cost, 12),
                $"At a fee of {fee}, buying back what 250 bought should cost 250 - got {cost}.");
        }
    }

    /// <summary>
    /// A constant-product pool cannot be swapped empty, and says so instead of dividing by zero.
    /// </summary>
    [TestMethod]
    public void TestUAPoolCannotBeSwappedEmpty()
    {
        decimal nearly = AmmMath.SwapAssetOut(1_000m, 4_000m, 3_999m, 0);
        decimal nearer = AmmMath.SwapAssetOut(1_000m, 4_000m, 3_999.9m, 0);
        Assert.IsTrue(nearer > nearly * 9m, $"The cost should climb steeply: {nearly} then {nearer}.");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.SwapAssetOut(1_000m, 4_000m, 4_000m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.SwapAssetOut(1_000m, 4_000m, 4_001m, 0));
    }

    /// <summary>
    /// The fee bound covers everything that charges a fee, not only what it was written for.
    /// </summary>
    [TestMethod]
    public void TestUTheFeeBoundCoversTheSwapAndTheInverses()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.SwapAssetIn(1_000m, 1_000m, 100m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.SwapAssetOut(1_000m, 1_000m, 100m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.SingleAssetDepositForLPTokens(1_000m, 100m, 1_000m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.SingleAssetWithdrawForLPTokens(1_000m, 100m, 1_000m, tradingFee: 5000));
    }

    [TestMethod]
    public void TestUSwappingAndRedeemingNothingGivesNothing()
    {
        Assert.AreEqual(0m, AmmMath.SwapAssetIn(1_000m, 1_000m, 0m, 1000));
        Assert.AreEqual(0m, AmmMath.SwapAssetOut(1_000m, 1_000m, 0m, 1000));
        Assert.AreEqual(0m, AmmMath.SingleAssetDepositForLPTokens(1_000m, 0m, 1_000m, 1000));
        Assert.AreEqual(0m, AmmMath.SingleAssetWithdrawForLPTokens(1_000m, 0m, 1_000m, 1000));
    }

    [TestMethod]
    public void TestUDepositingNothingEarnsNothing()
    {
        Assert.AreEqual(0m, AmmMath.LPTokensForSingleAssetDeposit(100m, 0m, 100m, 1000));
        Assert.AreEqual(0m, AmmMath.LPTokensForSingleAssetWithdraw(100m, 0m, 100m, 1000));
    }

    /// <summary>
    /// Equation 3 multiplies the LP token balance by a ratio near the deposit-to-pool ratio, and the
    /// multiply-first product of a huge deposit into a tiny pool overflows <see cref="decimal"/>; only
    /// then is the ratio divided down before it meets the balance.
    /// </summary>
    [TestMethod]
    public void TestUAHugeDepositIntoATinyPoolDoesNotOverflow()
    {
        decimal tokens = AmmMath.LPTokensForSingleAssetDeposit(1m, 1e22m, 1e10m, 0);

        // Equation 3 at fee 0 (f1 = f2 = 1), worked in double, which does not overflow here:
        // c = sqrt(1 + r) - 1 and t = T * (r - c) / (1 + c), about 1e21 for r = 1e22 and T = 1e10.
        double r = 1e22;
        double c = Math.Sqrt(1d + r) - 1d;
        double expected = 1e10 * (r - c) / (1d + c);

        Assert.AreEqual(expected, (double)tokens, expected * 1e-9);
    }

    /// <summary>
    /// The node credits a two-sided deposit at the smaller of the two ratios and leaves the
    /// surplus of the other asset with the depositor.
    /// </summary>
    [TestMethod]
    public void TestUAnUnbalancedTwoSidedDepositIsCreditedAtTheSmallerRatio()
    {
        Assert.AreEqual(100m, AmmMath.LPTokensForProportionalDeposit(1000m, 1000m, 100m, 200m, 1000m));
    }

    /// <summary>
    /// The square root must finish at every magnitude a decimal can hold, including the two
    /// extremes where the seed from <see cref="Math.Sqrt"/> is least helpful.
    /// </summary>
    [TestMethod]
    [Timeout(2000)]
    public void TestUSqrtSettlesAcrossTheDecimalRange()
    {
        for (int k = -28; k <= 28; k++)
        {
            decimal value = 1m;
            for (int i = 0; i < Math.Abs(k); i++)
            {
                value = k < 0 ? value / 10m : value * 10m;
            }

            decimal root = AmmMath.Sqrt(value);
            Assert.IsTrue(root > 0m, $"sqrt(1e{k}) is not positive: {root}");

            if (value >= 1e-8m && value <= 1e28m)
            {
                Assert.IsTrue(
                    Math.Abs(root * root - value) <= value * 1e-20m,
                    $"sqrt(1e{k}) = {root} squares to {root * root}");
            }
        }

        Assert.IsTrue(AmmMath.Sqrt(decimal.MaxValue) > 0m);
    }

    /// <summary>
    /// Dividing first rescues the overflow case but costs significant digits on small deposits:
    /// a decimal keeps 28 digits after the point, so a quotient near 1e-12 holds 16 digits where
    /// the product it feeds could have held all 28. The multiplication therefore stays first and
    /// the order flips only where it would overflow.
    /// </summary>
    [TestMethod]
    public void TestUSmallDepositsKeepTheDigitsOfTheMultiplyFirstOrder()
    {
        const decimal Pool = 1_000_000m;

        foreach (uint fee in new uint[] { 0, 1000 })
        {
            foreach (decimal lp in new[] { 1m, 1_000_000m, 1e15m })
            {
                foreach (decimal ratio in new[] { 1e-3m, 1e-6m, 1e-9m, 1e-12m, 1e-15m, 1e-18m })
                {
                    decimal deposit = ratio * Pool;
                    (decimal r, decimal c) = SingleDepositTerms(Pool, deposit, fee);

                    decimal multiplyFirst = lp * (r - c) / (1m + c);

                    Assert.AreEqual(
                        multiplyFirst,
                        AmmMath.LPTokensForSingleAssetDeposit(Pool, deposit, lp, fee),
                        $"fee {fee}, LP balance {lp}, ratio {ratio}");
                }
            }
        }

        (decimal ratioAtTwelve, decimal cAtTwelve) = SingleDepositTerms(Pool, 1e-6m, 1000);
        decimal multiplyFirstAtTwelve = 1_000_000m * (ratioAtTwelve - cAtTwelve) / (1m + cAtTwelve);
        decimal divideFirst = 1_000_000m * ((ratioAtTwelve - cAtTwelve) / (1m + cAtTwelve));
        Assert.IsTrue(
            Math.Abs(divideFirst - multiplyFirstAtTwelve) / multiplyFirstAtTwelve > 1e-20m,
            "Divide-first no longer loses digits here; the order guard may be unnecessary.");
    }

    private static (decimal Ratio, decimal C) SingleDepositTerms(decimal pool, decimal deposit, uint tradingFee)
    {
        decimal fee = tradingFee / 100_000m;
        decimal f1 = 1m - fee;
        decimal f2 = (1m - fee / 2m) / f1;
        decimal r = deposit / pool;
        return (r, AmmMath.Sqrt(f2 * f2 + r / f1) - f2);
    }
}
