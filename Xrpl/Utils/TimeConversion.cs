using System;
using System.Globalization;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/timeConversion.ts

namespace Xrpl.Utils
{
    public static class DateTimeUtils
    {
        /// <summary>Unix seconds of the Ripple epoch, 2000-01-01T00:00:00Z.</summary>
        public const long RippleEpochUnixSeconds = 946_684_800;

        private const int RIPPLE_EPOCH_DIFF = (int)RippleEpochUnixSeconds;

        /// <summary>The Ripple epoch, 2000-01-01T00:00:00Z: ledger time counts seconds from here.</summary>
        public static readonly DateTimeOffset RippleEpoch = DateTimeOffset.FromUnixTimeSeconds(RippleEpochUnixSeconds);

        public static long RippleTimeToUnixTime(long rpepoch)
        {
            return (rpepoch + RIPPLE_EPOCH_DIFF) * 1000;
        }

        /// <summary>
        /// Ripple-epoch seconds of a Unix time in milliseconds, rounded to the nearest second, ties to
        /// even (<see cref="Math.Round(decimal)"/>: 0.5 s is 0, 2.5 s is 2; xrpl.js rounds ties up). For an expiration or any deadline use <see cref="ToRippleTime(DateTimeOffset)"/>, which
        /// rounds down: rounding 0.7 s up would put the deadline a second later than the instant asked for.
        /// </summary>
        public static long UnixTimeToRippleTime(long timestamp)
        {
            return (long)Math.Round((decimal)timestamp / 1000) - RIPPLE_EPOCH_DIFF;
        }

        public static string RippleTimeToISOTime(long rippleTime)
        {
            var timestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddMilliseconds(RippleTimeToUnixTime(rippleTime)).ToUniversalTime();
            return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        public static long ISOTimeToRippleTime(string iso8601)
        {
            // Parsed straight to UTC instead of into the machine's zone and back, so the result
            // never passes through local time.
            DateTime date = DateTime.ParseExact(iso8601, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            return ISOTimeToRippleTime(date);
        }

        /// <summary>
        /// Ripple-epoch seconds of <paramref name="date"/>.
        /// </summary>
        /// <param name="date">A <see cref="DateTimeKind.Local"/> value is converted to UTC first;
        /// an <see cref="DateTimeKind.Unspecified"/> one is taken as UTC.</param>
        public static long ISOTimeToRippleTime(DateTime date)
        {
            DateTime utc = date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date;
            long milliseconds = (long)utc.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return UnixTimeToRippleTime(milliseconds);
        }

        /// <summary>
        /// Ledger (Ripple-epoch) time of <paramref name="time"/>: whole seconds since 2000-01-01T00:00:00Z,
        /// rounded down. A time before the epoch gives 0 and one past <see cref="uint.MaxValue"/> seconds
        /// (year 2136) gives <see cref="uint.MaxValue"/>, the range of an <c>Expiration</c> or
        /// <c>CloseTime</c> field.
        /// </summary>
        public static uint ToRippleTime(DateTimeOffset time)
        {
            long ticks = time.UtcTicks - RippleEpoch.UtcTicks;
            if (ticks < 0)
                return 0;

            long seconds = ticks / TimeSpan.TicksPerSecond;
            return seconds >= uint.MaxValue ? uint.MaxValue : (uint)seconds;
        }

        /// <inheritdoc cref="ToRippleTime(DateTimeOffset)"/>
        /// <param name="time">A <see cref="DateTimeKind.Local"/> value is converted to UTC first;
        /// an <see cref="DateTimeKind.Unspecified"/> one is taken as UTC.</param>
        public static uint ToRippleTime(DateTime time)
        {
            DateTime utc = time.Kind switch
            {
                DateTimeKind.Local => time.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
                _ => time,
            };
            return ToRippleTime(new DateTimeOffset(utc));
        }

        /// <summary>The UTC instant of a ledger time: <see cref="RippleEpoch"/> plus <paramref name="rippleTime"/> seconds.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The result is outside the <see cref="DateTimeOffset"/> range.</exception>
        public static DateTimeOffset FromRippleTime(long rippleTime) => RippleEpoch.AddSeconds(rippleTime);
    }
}