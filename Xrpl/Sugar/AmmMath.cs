using System;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What an AMM pool will do before you ask it to - what a deposit is worth in LP tokens, what a
    /// withdrawal costs, what a swap pays out, and each of those read backwards - computed the way
    /// rippled computes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The formulas are equations 3 and 7 from rippled's <c>AMMHelpers.cpp</c>, not the ones that
    /// circulate as the "AMM single-sided deposit formula". The circulating one,
    /// <c>T·(√(1 + b·(1 − f/2)/B) − 1)</c>, is close and always wrong in the same direction: a
    /// deposit the size of the pool at a 1% fee gives <c>0.41244·T</c> against rippled's
    /// <c>0.41213·T</c>, an error of 0.08%. <see cref="LPTokensForSingleAssetDeposit"/> reproduces
    /// the node's figure.
    /// </para>
    /// <para>
    /// Two things decide whether an estimate matches what the node actually credits, and neither is
    /// in the formulas:
    /// </para>
    /// <list type="number">
    /// <item><description><b>Whose fee.</b> The holder of the pool's auction slot trades at
    /// <c>DiscountedFee</c> - a tenth of the pool's trading fee - and the node computes their
    /// deposits and withdrawals at that rate too. Estimating at the pool's fee is wrong for them;
    /// see <see cref="DiscountedTradingFee"/>.</description></item>
    /// <item><description><b>How fresh the pool state is.</b> <c>amm_info</c> has to be read
    /// immediately before the calculation. Run against a snapshot taken when a screen opened, the
    /// drift looks exactly like an error in the arithmetic.</description></item>
    /// </list>
    /// <para>
    /// What comes back is a bound rather than the exact credit, and the direction is known. Under
    /// <c>fixAMMv1_3</c> rippled rounds the final multiplication against the caller in both
    /// directions - <c>lpTokensOut</c> downward ("minimize tokens out"), <c>lpTokensIn</c> upward
    /// ("maximize tokens in") - so a deposit is credited this much or a shade less, and a
    /// withdrawal costs this much or a shade more. The difference lands in the last of
    /// <c>STAmount</c>'s 15 significant digits, which is below what differencing two reported LP
    /// token balances can resolve.
    /// </para>
    /// <para>
    /// Units are the caller's and nothing here converts between them. That matters most for XRP:
    /// <c>amm_info</c> reports the XRP side of a pool in drops, so a balance read from it and an
    /// amount the caller is thinking of in XRP are a million apart. Mixing the two in one call
    /// returns a number that reads as a broken formula rather than as a unit mistake.
    /// </para>
    /// <para>
    /// Everything is computed in <see cref="decimal"/> rather than <see cref="double"/>: 28
    /// significant digits against 15. That is also why the square root here is Newton's method -
    /// <see cref="Math.Sqrt"/> would throw away the precision the rest of the calculation keeps.
    /// </para>
    /// <para>
    /// For the exact figures a deposit or withdrawal moves - rippled's own <c>Number</c> and
    /// <c>STAmount</c> arithmetic, the rounding against the trader and the node's refusals - see
    /// <see cref="AmmLiquidity"/>.
    /// </para>
    /// </remarks>
    public static class AmmMath
    {
        /// <summary>
        /// What a <c>TradingFee</c> of 1 is worth as a fraction: 1/100 000, so 1000 is one per cent.
        /// </summary>
        /// <remarks>
        /// rippled's <c>kAuctionSlotFeeScaleFactor</c>. The field is in units of 1/10 of a basis
        /// point, which is easy to be out by a factor of ten on.
        /// </remarks>
        public const uint TradingFeeScale = 100_000;

        /// <summary>
        /// How much cheaper the auction slot holder's fee is than the pool's.
        /// </summary>
        /// <remarks>rippled's <c>kAuctionSlotDiscountedFeeFraction</c>.</remarks>
        public const uint AuctionSlotFeeDiscount = 10;

        /// <summary>
        /// The largest <c>TradingFee</c> a pool can have: 1000, one per cent.
        /// </summary>
        /// <remarks>
        /// rippled's <c>kTradingFeeThreshold</c>, and the reason a fee is checked against it here.
        /// The field is in units of 1/10 of a basis point, so a caller who reaches for basis points
        /// or for whole per cent is out by a factor of ten or a hundred - and the arithmetic below
        /// would carry on and return a plausible number rather than say so.
        /// </remarks>
        public const uint TradingFeeThreshold = 1000;

        /// <summary>
        /// The trading fee as a fraction of 1.
        /// </summary>
        /// <param name="tradingFee">The pool's <c>TradingFee</c>. rippled caps it at 1000 - one per cent - in <c>kTradingFeeThreshold</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tradingFee"/> exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal TradingFeeFraction(uint tradingFee)
        {
            RequireValidTradingFee(tradingFee);
            return (decimal)tradingFee / TradingFeeScale;
        }

        /// <summary>
        /// The fee the auction slot holder trades at.
        /// </summary>
        /// <remarks>
        /// Use this in place of the pool's fee when the account holding the slot is the one
        /// depositing or withdrawing - the node does, and an estimate at the pool's fee will not
        /// match what it credits.
        /// </remarks>
        /// <param name="tradingFee">The pool's <c>TradingFee</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tradingFee"/> exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static uint DiscountedTradingFee(uint tradingFee)
        {
            RequireValidTradingFee(tradingFee);
            return tradingFee / AuctionSlotFeeDiscount;
        }

        /// <summary>
        /// LP tokens credited for depositing one asset only.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Equation 3: with <c>f1 = 1 − fee</c>, <c>f2 = (1 − fee/2)/f1</c> and <c>r = b/B</c>,
        /// </para>
        /// <code>
        /// c = √(f2² + r/f1) − f2
        /// t = T · (r − c) / (1 + c)
        /// </code>
        /// <para>
        /// The node rounds the last multiplication down, so it credits this or a shade less.
        /// </para>
        /// <para>
        /// The plus under the root is deliberate and is what rippled computes. The comment above
        /// that equation in <c>AMMHelpers.cpp</c> writes it as <c>√(f2² − b/(B·f1))</c>, but the
        /// code uses <c>+</c>, and so does the derivation of equation 4 immediately below it. With
        /// a minus the radicand goes negative for ordinary inputs, which settles it.
        /// </para>
        /// </remarks>
        /// <param name="poolBalance">The pool's balance of the asset being deposited, before the deposit.</param>
        /// <param name="deposit">How much of it is being deposited.</param>
        /// <param name="lpTokenBalance">The pool's LP token balance, before the deposit.</param>
        /// <param name="tradingFee">The fee this depositor trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the deposit is negative, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        /// <exception cref="OverflowException">An extreme input does not fit a <see cref="decimal"/>: the
        /// ratio <c>deposit / poolBalance</c> (or the root of it) exceeds the range, or the credited
        /// tokens do even after the product is reordered to divide first.</exception>
        public static decimal LPTokensForSingleAssetDeposit(
            decimal poolBalance,
            decimal deposit,
            decimal lpTokenBalance,
            uint tradingFee)
        {
            RequirePositive(poolBalance, nameof(poolBalance));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(deposit, nameof(deposit));
            RequireValidTradingFee(tradingFee);

            if (deposit == 0m)
            {
                return 0m;
            }

            decimal fee = TradingFeeFraction(tradingFee);
            decimal f1 = 1m - fee;
            decimal f2 = (1m - fee / 2m) / f1;
            decimal r = deposit / poolBalance;
            decimal c = Sqrt(f2 * f2 + r / f1) - f2;

            // rippled evaluates this in Number, which does not overflow; decimal does when a huge
            // deposit meets a large LP balance, so the product is only reordered in that case.
            // Dividing first by default would cost digits: a decimal keeps 28 places after the
            // point, so a small quotient holds far fewer significant digits than the product.
            decimal numerator = r - c;
            decimal denominator = 1m + c;
            try
            {
                return lpTokenBalance * numerator / denominator;
            }
            catch (OverflowException)
            {
                return lpTokenBalance * (numerator / denominator);
            }
        }

        /// <summary>
        /// LP tokens spent to withdraw one asset only.
        /// </summary>
        /// <remarks>
        /// Equation 7: with <c>fr = b/B</c> and <c>c = fr·fee + 2 − fee</c>,
        /// <code>
        /// t = T · (c − √(c² − 4·fr)) / 2
        /// </code>
        /// Note that this one uses the fee itself where <see cref="LPTokensForSingleAssetDeposit"/>
        /// uses <c>1 − fee</c>; rippled's <c>lpTokensIn</c> calls <c>getFee</c> rather than
        /// <c>feeMult</c>, and the difference is easy to lose when transcribing.
        /// The node rounds the last multiplication up here rather than down - both directions go
        /// against the caller - so a withdrawal costs this or a shade more.
        /// </remarks>
        /// <param name="poolBalance">The pool's balance of the asset being withdrawn, before the withdrawal.</param>
        /// <param name="withdraw">How much of it is being withdrawn.</param>
        /// <param name="lpTokenBalance">The pool's LP token balance, before the withdrawal.</param>
        /// <param name="tradingFee">The fee this account trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the amount is negative, it exceeds the pool, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal LPTokensForSingleAssetWithdraw(
            decimal poolBalance,
            decimal withdraw,
            decimal lpTokenBalance,
            uint tradingFee)
        {
            RequirePositive(poolBalance, nameof(poolBalance));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(withdraw, nameof(withdraw));
            RequireValidTradingFee(tradingFee);

            if (withdraw == 0m)
            {
                return 0m;
            }

            if (withdraw > poolBalance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(withdraw),
                    $"Cannot withdraw {withdraw} from a pool holding {poolBalance}.");
            }

            decimal fee = TradingFeeFraction(tradingFee);
            decimal fr = withdraw / poolBalance;
            decimal c = fr * fee + 2m - fee;

            return lpTokenBalance * (c - Sqrt(c * c - 4m * fr)) / 2m;
        }

        /// <summary>
        /// How much of one asset must be deposited to be credited exactly this many LP tokens.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Equation 4, which is rippled solving equation 3 for <c>b</c>. With <c>f1</c> and
        /// <c>f2</c> as in <see cref="LPTokensForSingleAssetDeposit"/>, <c>t1 = t/T</c> and
        /// <c>t2 = 1 + t1</c>:
        /// </para>
        /// <code>
        /// d = f2 - t1/t2
        /// a = 1/t2²,  b = 2·d/t2 - 1/f1,  c = d² - f2²
        /// deposit = B · (-b + √(b² - 4ac)) / 2a
        /// </code>
        /// <para>
        /// The root is always real: swept across every fee up to the cap and token ratios from
        /// 1e-6 to 1000, the discriminant never falls below 1, so the quadratic cannot hand
        /// <see cref="Sqrt"/> a negative and rippled's <c>solveQuadraticEq</c> does not guard it
        /// either.
        /// </para>
        /// <para>
        /// This is what an <c>AMMDeposit</c> carrying <c>LPTokenOut</c> will actually take from
        /// the account, and the direction of the node's rounding reverses here: it maximizes the
        /// deposit, so it takes this much or a shade more. That is consistent rather than
        /// contrary - every one of these roundings favours the pool.
        /// </para>
        /// </remarks>
        /// <param name="poolBalance">The pool's balance of the asset being deposited.</param>
        /// <param name="lpTokens">The LP tokens wanted.</param>
        /// <param name="lpTokenBalance">The pool's LP token balance, before the deposit.</param>
        /// <param name="tradingFee">The fee this depositor trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the token amount is negative, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal SingleAssetDepositForLPTokens(
            decimal poolBalance,
            decimal lpTokens,
            decimal lpTokenBalance,
            uint tradingFee)
        {
            RequirePositive(poolBalance, nameof(poolBalance));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(lpTokens, nameof(lpTokens));
            RequireValidTradingFee(tradingFee);

            if (lpTokens == 0m)
            {
                return 0m;
            }

            decimal fee = TradingFeeFraction(tradingFee);
            decimal f1 = 1m - fee;
            decimal f2 = (1m - fee / 2m) / f1;
            decimal t1 = lpTokens / lpTokenBalance;
            decimal t2 = 1m + t1;
            decimal d = f2 - t1 / t2;
            decimal a = 1m / (t2 * t2);
            decimal b = 2m * d / t2 - 1m / f1;
            decimal c = d * d - f2 * f2;

            return poolBalance * SolveQuadratic(a, b, c);
        }

        /// <summary>
        /// How much of one asset comes out for redeeming exactly this many LP tokens.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Equation 8, rippled solving equation 7 for <c>b</c>. With <c>t1 = t/T</c>:
        /// </para>
        /// <code>
        /// withdraw = B · (t1² - t1·(2 - fee)) / (t1·fee - 1)
        /// </code>
        /// <para>
        /// Both halves of that fraction are negative for any real input, which is why the result
        /// is not. What an <c>AMMWithdraw</c> carrying <c>LPTokenIn</c> pays out; the node
        /// minimizes the withdrawal, so it pays this or a shade less.
        /// </para>
        /// </remarks>
        /// <param name="poolBalance">The pool's balance of the asset being withdrawn.</param>
        /// <param name="lpTokens">The LP tokens being redeemed.</param>
        /// <param name="lpTokenBalance">The pool's LP token balance, before the withdrawal.</param>
        /// <param name="tradingFee">The fee this account trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the token amount is negative or exceeds the pool's, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal SingleAssetWithdrawForLPTokens(
            decimal poolBalance,
            decimal lpTokens,
            decimal lpTokenBalance,
            uint tradingFee)
        {
            RequirePositive(poolBalance, nameof(poolBalance));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(lpTokens, nameof(lpTokens));
            RequireValidTradingFee(tradingFee);

            if (lpTokens == 0m)
            {
                return 0m;
            }

            if (lpTokens > lpTokenBalance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lpTokens),
                    $"Cannot redeem {lpTokens} tokens against a balance of {lpTokenBalance}.");
            }

            decimal fee = TradingFeeFraction(tradingFee);
            decimal t1 = lpTokens / lpTokenBalance;

            return poolBalance * (t1 * t1 - t1 * (2m - fee)) / (t1 * fee - 1m);
        }

        /// <summary>
        /// LP tokens credited for depositing both assets at the pool's own ratio.
        /// </summary>
        /// <remarks>
        /// A deposit in proportion does not move the price, so no fee applies: the node credits
        /// <c>T · frac</c> and takes <c>A · frac</c> and <c>B · frac</c>, where <c>frac</c> is the
        /// smaller of the two ratios offered - whichever asset runs out first decides how much of
        /// the other is used. Use <see cref="AssetsForProportionalDeposit"/> to find out how much of
        /// each will actually be taken.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, or an amount is negative.</exception>
        public static decimal LPTokensForProportionalDeposit(
            decimal poolBalance1,
            decimal poolBalance2,
            decimal deposit1,
            decimal deposit2,
            decimal lpTokenBalance)
        {
            RequirePositive(poolBalance1, nameof(poolBalance1));
            RequirePositive(poolBalance2, nameof(poolBalance2));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(deposit1, nameof(deposit1));
            RequireNotNegative(deposit2, nameof(deposit2));

            decimal frac = Math.Min(deposit1 / poolBalance1, deposit2 / poolBalance2);
            return lpTokenBalance * frac;
        }

        /// <summary>
        /// How much of each asset a proportional deposit will actually take.
        /// </summary>
        /// <remarks>
        /// The leftover of the more plentiful asset stays where it is; the node deposits both sides
        /// at the same fraction of the pool.
        /// </remarks>
        public static (decimal Asset1, decimal Asset2) AssetsForProportionalDeposit(
            decimal poolBalance1,
            decimal poolBalance2,
            decimal deposit1,
            decimal deposit2)
        {
            RequirePositive(poolBalance1, nameof(poolBalance1));
            RequirePositive(poolBalance2, nameof(poolBalance2));
            RequireNotNegative(deposit1, nameof(deposit1));
            RequireNotNegative(deposit2, nameof(deposit2));

            decimal frac = Math.Min(deposit1 / poolBalance1, deposit2 / poolBalance2);
            return (poolBalance1 * frac, poolBalance2 * frac);
        }

        /// <summary>
        /// What redeeming LP tokens returns when both assets are taken out at the pool's ratio.
        /// </summary>
        /// <remarks>
        /// Equations 1 and 2: <c>a = (t/T)·A</c> and <c>b = (t/T)·B</c>. No fee, for the same
        /// reason as a proportional deposit - the price does not move.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the token amount is negative, or it exceeds the pool's.</exception>
        public static (decimal Asset1, decimal Asset2) AssetsForProportionalWithdraw(
            decimal poolBalance1,
            decimal poolBalance2,
            decimal lpTokens,
            decimal lpTokenBalance)
        {
            RequirePositive(poolBalance1, nameof(poolBalance1));
            RequirePositive(poolBalance2, nameof(poolBalance2));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(lpTokens, nameof(lpTokens));

            if (lpTokens > lpTokenBalance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lpTokens),
                    $"Cannot redeem {lpTokens} tokens against a balance of {lpTokenBalance}.");
            }

            decimal frac = lpTokens / lpTokenBalance;
            return (poolBalance1 * frac, poolBalance2 * frac);
        }

        /// <summary>
        /// What comes out of the pool for swapping this much of the other asset in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// rippled's <c>swapAssetIn</c>, and what a payment routed through an AMM pays the taker.
        /// The node writes it as
        /// </para>
        /// <code>
        /// out = poolOut - (poolIn · poolOut) / (poolIn + in·(1 - fee))
        /// </code>
        /// <para>
        /// which is the form used here, rearranged to <c>poolOut·x/(poolIn + x)</c> with
        /// <c>x = in·(1 - fee)</c>. The two are the same expression; the difference is that the
        /// node's form subtracts two nearly equal numbers for a small swap and loses digits to
        /// the cancellation, while this one has nothing to cancel.
        /// </para>
        /// <para>
        /// The fee comes off the input before the curve sees it, so the whole of
        /// <paramref name="assetIn"/> still enters the pool - the fee stays there for the
        /// liquidity providers rather than being taken away.
        /// </para>
        /// </remarks>
        /// <param name="poolIn">The pool's balance of the asset being swapped in.</param>
        /// <param name="poolOut">The pool's balance of the asset being swapped out.</param>
        /// <param name="assetIn">How much is being swapped in, fee included.</param>
        /// <param name="tradingFee">The fee this account trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the amount is negative, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal SwapAssetIn(
            decimal poolIn,
            decimal poolOut,
            decimal assetIn,
            uint tradingFee)
        {
            RequirePositive(poolIn, nameof(poolIn));
            RequirePositive(poolOut, nameof(poolOut));
            RequireNotNegative(assetIn, nameof(assetIn));
            RequireValidTradingFee(tradingFee);

            if (assetIn == 0m)
            {
                return 0m;
            }

            decimal effectiveIn = assetIn * (1m - TradingFeeFraction(tradingFee));

            return poolOut * effectiveIn / (poolIn + effectiveIn);
        }

        /// <summary>
        /// What must be swapped in to take exactly this much of the other asset out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// rippled's <c>swapAssetOut</c>, the inverse of <see cref="SwapAssetIn"/>:
        /// </para>
        /// <code>
        /// in = ((poolIn · poolOut) / (poolOut - out) - poolIn) / (1 - fee)
        /// </code>
        /// <para>
        /// rearranged here to <c>poolIn·out / ((poolOut - out)·(1 - fee))</c> for the same reason
        /// as above. The cost climbs without bound as <paramref name="assetOut"/> approaches the
        /// pool's balance, which is the constant product refusing to be emptied; asking for the
        /// whole of it, or more, is rejected rather than answered with a division by zero.
        /// </para>
        /// </remarks>
        /// <param name="poolIn">The pool's balance of the asset being swapped in.</param>
        /// <param name="poolOut">The pool's balance of the asset being swapped out.</param>
        /// <param name="assetOut">How much is wanted out.</param>
        /// <param name="tradingFee">The fee this account trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the amount is negative or is not less than the pool, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal SwapAssetOut(
            decimal poolIn,
            decimal poolOut,
            decimal assetOut,
            uint tradingFee)
        {
            RequirePositive(poolIn, nameof(poolIn));
            RequirePositive(poolOut, nameof(poolOut));
            RequireNotNegative(assetOut, nameof(assetOut));
            RequireValidTradingFee(tradingFee);

            if (assetOut == 0m)
            {
                return 0m;
            }

            if (assetOut >= poolOut)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(assetOut),
                    $"A constant-product pool cannot be emptied: {assetOut} was asked of a balance " +
                    $"of {poolOut}, and the cost of the last unit is unbounded.");
            }

            return poolIn * assetOut / ((poolOut - assetOut) * (1m - TradingFeeFraction(tradingFee)));
        }

        /// <summary>
        /// Square root in <see cref="decimal"/>, by Newton's method.
        /// </summary>
        /// <remarks>
        /// <see cref="Math.Sqrt"/> works in <see cref="double"/>, whose 15 significant digits would
        /// discard the precision the rest of this class keeps. The iteration is seeded from the
        /// double result, which is already close, so it converges in a handful of steps; it stops
        /// when the estimate settles or begins alternating between two neighbouring values, which
        /// is how a decimal iteration ends when it can get no closer. Public because consumers that
        /// compute on top of the pool formulas need the same root, at the same precision. If the
        /// iteration has not settled after 100 steps it returns the last estimate and does not throw.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
        public static decimal Sqrt(decimal value)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cannot take the square root of a negative number.");
            }

            if (value == 0m)
            {
                return 0m;
            }

            decimal guess;
            try
            {
                guess = (decimal)Math.Sqrt((double)value);
            }
            catch (OverflowException)
            {
                guess = value;
            }

            if (guess <= 0m)
            {
                guess = value > 1m ? value / 2m : 1m;
            }

            decimal previous = 0m;
            for (int step = 0; step < 100; step++)
            {
                decimal next = (guess + value / guess) / 2m;
                if (next == guess || next == previous)
                {
                    return next;
                }

                previous = guess;
                guess = next;
            }

            return guess;
        }

        /// <summary>
        /// The larger root, which is the one rippled's <c>solveQuadraticEq</c> takes.
        /// </summary>
        private static decimal SolveQuadratic(decimal a, decimal b, decimal c)
            => (-b + Sqrt(b * b - 4m * a * c)) / (2m * a);

        private static void RequireValidTradingFee(uint tradingFee)
        {
            if (tradingFee > TradingFeeThreshold)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tradingFee),
                    $"A trading fee is in units of 1/{TradingFeeScale}, so it cannot exceed " +
                    $"{TradingFeeThreshold} - one per cent - but was {tradingFee}.");
            }
        }

        private static void RequirePositive(decimal value, string name)
        {
            if (value <= 0m)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be positive, but was {value}.");
            }
        }

        private static void RequireNotNegative(decimal value, string name)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must not be negative, but was {value}.");
            }
        }
    }
}
