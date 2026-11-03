using System;

namespace Xrpl.Amounts
{
    /// <summary>
    /// The amounts the ledger can hold, and how to land on one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XRP exists in whole drops: six decimal places. An issued amount keeps a mantissa in
    /// <c>[10^15, 10^16)</c> (rippled's <c>STAmount</c>), so up to sixteen significant digits. That
    /// is not a fixed number of decimal places: a value near a million has nine, one near 1e-9 has
    /// twenty-four, and from 1e16 the step grows past one (10, 100, ...). An amount that is off the
    /// grid is replaced by the ledger with a different one, or refused as malformed.
    /// </para>
    /// <para>
    /// Direction matters. A figure promised to be received is rounded down
    /// (<see cref="RoundDown"/>), as the ledger does; a figure that bounds a spend, such as
    /// <c>SendMax</c>, is rounded up (<see cref="RoundUp"/>), because the ledger rejects a payment
    /// whose <c>SendMax</c> is below what the delivery requires. Both work on the magnitude, so
    /// "down" is toward zero and "up" is away from it, and the grid is symmetric around zero.
    /// </para>
    /// <para>
    /// Everything is decimal arithmetic. A logarithm on a converted double lands on the wrong side of
    /// a power of ten often enough to give a digit too many or too few.
    /// </para>
    /// <para>
    /// MPT amounts are not covered: they are whole numbers up to 2^63-1, which neither the XRP grid
    /// (six places) nor the issued one (sixteen significant digits) describes. Use
    /// <see cref="Math.Truncate(decimal)"/> and <see cref="Math.Ceiling(decimal)"/> for them.
    /// </para>
    /// </remarks>
    public static class LedgerAmountGrid
    {
        /// <summary>Decimal places XRP has: one drop is 1e-6.</summary>
        public const int XrpDecimals = 6;

        /// <summary>
        /// Significant digits an issued amount keeps at most: the top of the mantissa range
        /// <c>[10^15, 10^16)</c>. A seventeenth digit is refused by the serializer, not trimmed.
        /// </summary>
        public const int IssuedSignificantDigits = 16;

        /// <summary>The smallest positive XRP amount: one drop.</summary>
        public const decimal SmallestXrp = 0.000001m;

        /// <summary>Most decimal places a <see cref="decimal"/> can hold at all.</summary>
        private const int MaxDecimalScale = 28;

        /// <summary>
        /// Digits kept past the grid step before rounding. Summing the contributions of a route
        /// leaves units in the last place; below a millionth of a step that residue is not a
        /// quantity, and rounding away from zero would turn it into a whole step (400000 coming back
        /// as 400000.000001).
        /// </summary>
        private const int ResidueDigits = 6;

        /// <summary>
        /// Rounds toward zero onto the grid: the form for anything presented as received. XRP and
        /// issued amounts only; an MPT amount is not on this grid.
        /// </summary>
        /// <param name="value">The amount; zero is returned as is.</param>
        /// <param name="isXrp"><see langword="true"/> for XRP (drops), <see langword="false"/> for an issued amount.</param>
        /// <returns>The grid value toward zero. Residue is removed first: the value is scaled to the
        /// grid and rounded to six places (<see cref="Math.Round(decimal, int)"/>, banker's rounding at
        /// the midpoint) before the cut, so the result may land up to 5e-7 of a step on the other side
        /// of the input, treating the difference as arithmetic residue. For XRP,
        /// <c>RoundDown(1.23456799999999m, isXrp: true)</c> is <c>1.234568</c>, while <c>1.2345679m</c> is cut to
        /// <c>1.234567</c>.</returns>
        public static decimal RoundDown(decimal value, bool isXrp) => Adjust(value, isXrp, awayFromZero: false);

        /// <summary>
        /// Rounds away from zero onto the grid: the form for anything that bounds a spend. XRP and
        /// issued amounts only; an MPT amount is not on this grid.
        /// </summary>
        /// <param name="value">The amount; zero is returned as is.</param>
        /// <param name="isXrp"><see langword="true"/> for XRP (drops), <see langword="false"/> for an issued amount.</param>
        /// <returns>The grid value away from zero. Residue is removed first, as in
        /// <see cref="RoundDown"/>: the result may land up to 5e-7 of a step on the other side of the
        /// input, treating the difference as arithmetic residue, so <c>400000 + 1e-16</c> stays
        /// <c>400000</c> for XRP.</returns>
        /// <exception cref="OverflowException">The result would exceed the range of <see cref="decimal"/>;
        /// only possible for an issued amount within one step of <see cref="decimal.MaxValue"/> or
        /// <see cref="decimal.MinValue"/>, since the sign is applied after the magnitude is rounded.
        /// An XRP amount that large is returned unchanged.</exception>
        public static decimal RoundUp(decimal value, bool isXrp) => Adjust(value, isXrp, awayFromZero: true);

        /// <summary>
        /// The distance between two neighbouring grid values at the magnitude of
        /// <paramref name="value"/>: 1e-6 for XRP; for an issued amount <c>10^(msd - 15)</c>, which is
        /// 10, 100, ... from 1e16 and is never smaller than 1e-28, the finest a <see cref="decimal"/>
        /// holds. The sign of <paramref name="value"/> is ignored. XRP and issued amounts only; an MPT
        /// amount has a step of one and is not covered.
        /// </summary>
        /// <param name="value">A nonzero amount.</param>
        /// <param name="isXrp"><see langword="true"/> for XRP (drops), <see langword="false"/> for an issued amount.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is zero: zero has no
        /// most significant digit and so no step. Applies to XRP as well, for one rule.</exception>
        public static decimal Step(decimal value, bool isXrp)
        {
            if (value == 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Zero has no step on the ledger's amount grid.");
            }

            int decimals = isXrp ? XrpDecimals : IssuedDecimalsFor(value);

            return decimals >= 0 ? new decimal(1, 0, 0, false, (byte)decimals) : Pow10(-decimals);
        }

        /// <summary>
        /// Whether a positive XRP amount is below one drop and so cannot be sent: it serializes as
        /// "0", which the ledger refuses as malformed instead of answering "no route". Issued amounts
        /// have no smallest magnitude, only a digit count, so they are never below it.
        /// </summary>
        public static bool IsBelowSmallest(decimal value, bool isXrp) => isXrp && value > 0m && value < SmallestXrp;

        private static decimal Adjust(decimal value, bool isXrp, bool awayFromZero)
        {
            if (value == 0m)
            {
                return value;
            }

            decimal magnitude = Math.Abs(value);
            int decimals = isXrp ? XrpDecimals : IssuedDecimalsFor(magnitude);

            decimal adjusted;

            if (decimals < 0)
            {
                // From 1e16 the mantissa is used up and only multiples of 10, 100, ... exist.
                decimal step = Pow10(-decimals);
                adjusted = Snap(magnitude / step, awayFromZero) * step;
            }
            else
            {
                decimal factor = Pow10(decimals);

                // A decimal holds 28 digits. Where the product with the factor would not fit, the
                // value has no more than six fractional digits already (XRP above about 7.9e22).
                if (magnitude > decimal.MaxValue / factor)
                {
                    return value;
                }

                adjusted = Snap(magnitude * factor, awayFromZero) / factor;
            }

            return value < 0m ? -adjusted : adjusted;
        }

        private static decimal Snap(decimal scaled, bool awayFromZero)
        {
            decimal cleaned = Math.Round(scaled, ResidueDigits);

            return awayFromZero ? Math.Ceiling(cleaned) : Math.Truncate(cleaned);
        }

        private static int IssuedDecimalsFor(decimal value) =>
            Math.Min((IssuedSignificantDigits - 1) - MostSignificantDigitPosition(Math.Abs(value)), MaxDecimalScale);

        /// <summary>
        /// Zero-based position of the most significant digit against the decimal point: 123.4 is 2,
        /// 9.99 is 0, 0.5 is -1, 1e-17 is -17. The value must be nonzero.
        /// </summary>
        private static int MostSignificantDigitPosition(decimal absolute)
        {
            int position = 0;

            if (absolute >= 1m)
            {
                decimal scaled = absolute;

                while (scaled >= 10m)
                {
                    scaled /= 10m;
                    position++;
                }

                return position;
            }

            decimal lifted = absolute;

            while (lifted < 1m)
            {
                lifted *= 10m;
                position--;
            }

            return position;
        }

        private static decimal Pow10(int exponent)
        {
            decimal result = 1m;

            for (int i = 0; i < exponent; i++)
            {
                result *= 10m;
            }

            return result;
        }
    }
}
