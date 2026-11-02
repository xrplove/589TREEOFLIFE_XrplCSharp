using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Xrpl.Models.Common;

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// Reads a <see cref="LORippleState"/> from one account's point of view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trust line is one ledger entry shared by two accounts. rippled puts the account with the
    /// numerically smaller 160-bit AccountID on the Low side and the other on the High side (not the
    /// smaller base58 text: the address text does not sort like the ID), stores
    /// <see cref="LORippleState.Balance"/> from the Low account's point of view, and keeps the limits,
    /// qualities and the paired <c>lsfLow*</c>/<c>lsfHigh*</c> flags per side.
    /// </para>
    /// <para>
    /// The side of an account is read from <see cref="LORippleState.LowLimit"/> and
    /// <see cref="LORippleState.HighLimit"/>: rippled sets the issuer of each limit to the account that
    /// owns that side, so the entry names its own sides and nothing has to be derived from the
    /// addresses. A helper that takes an account throws <see cref="ArgumentException"/> when the account
    /// is on neither side, because answering for an account that is not on the line would be a guess.
    /// </para>
    /// <para>
    /// Every method here is safe on a sparse entry (the <c>FinalFields</c> of a deleted node, a
    /// <c>PreviousFields</c> projection): an absent flag word reads as "no flag set", an absent
    /// quality as <see langword="null"/>, and an absent balance as zero. None of them throws
    /// <see cref="Xrpl.Client.Exceptions.AmountOutOfRangeException"/> for a balance or a limit above
    /// the range of <see cref="decimal"/>.
    /// </para>
    /// </remarks>
    public static class RippleStateExtensions
    {
        private const NumberStyles AmountStyles =
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

        /// <summary>
        /// Tests a flag on a flag word that may be absent.
        /// </summary>
        /// <remarks>
        /// An absent word reads as "not set". A RippleState in the ledger always carries
        /// <c>Flags</c>, so a null here is a sparse metadata projection: <c>PreviousFields</c> lists only
        /// the fields a transaction changed, and an unlisted flag word means "unchanged". Combine such a
        /// projection with the entry it belongs to through <see cref="OverlayPreviousFields"/> before testing.
        /// Writing the test as <c>(flags &amp; flag) != 0</c> on the nullable instead would read every
        /// flag as set for a null word, because C# lifts <c>null != 0</c> to <see langword="true"/>.
        /// </remarks>
        /// <param name="flags">The flag word, or null when the entry did not carry one.</param>
        /// <param name="flag">The flag to test. A combination of flags is set only when all of its bits are.</param>
        /// <returns><see langword="true"/> when <paramref name="flags"/> is present and has the flag set.</returns>
        public static bool IsFlagSet(this RippleStateFlags? flags, RippleStateFlags flag)
            => flags.HasValue && (flags.Value & flag) == flag;

        /// <summary>
        /// Whether <paramref name="account"/> is the Low side of this trust line.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns><see langword="true"/> for the Low side, <see langword="false"/> for the High side.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="account"/> is not the issuer of <see cref="LORippleState.LowLimit"/> or
        /// <see cref="LORippleState.HighLimit"/>. On a sparse entry the account's own limit has to be
        /// present: the peer's limit alone does not say which side the account is.
        /// </exception>
        public static bool IsLowSide(this LORippleState state, string account)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(account);

            if (string.Equals(state.LowLimit?.Issuer, account, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(state.HighLimit?.Issuer, account, StringComparison.Ordinal))
            {
                return false;
            }

            throw new ArgumentException(
                $"Account {account} is neither the Low ({state.LowLimit?.Issuer}) nor the High ({state.HighLimit?.Issuer}) side of this RippleState.",
                nameof(account));
        }

        /// <summary>
        /// The other account on the trust line.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>Classic address of the counterparty.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        /// <exception cref="InvalidOperationException">The counterparty's limit is absent from a sparse entry, so its address is unknown.</exception>
        public static string GetPeerOf(this LORippleState state, string account)
        {
            Currency peerLimit = state.GetPeerLimitOf(account);

            return peerLimit?.Issuer
                ?? throw new InvalidOperationException("The counterparty's limit is absent from this RippleState, so its account is unknown.");
        }

        /// <summary>
        /// The limit <paramref name="account"/> has set on the line. Its issuer is <paramref name="account"/>.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The limit, or null when a sparse entry does not carry it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static Currency GetLimitOf(this LORippleState state, string account)
            => state.IsLowSide(account) ? state.LowLimit : state.HighLimit;

        /// <summary>
        /// The limit the counterparty of <paramref name="account"/> has set on the line.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The limit, or null when a sparse entry does not carry it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static Currency GetPeerLimitOf(this LORippleState state, string account)
            => state.IsLowSide(account) ? state.HighLimit : state.LowLimit;

        /// <summary>
        /// The balance from the point of view of <paramref name="account"/>: positive when the account
        /// holds the token, negative when it has issued it to the counterparty.
        /// </summary>
        /// <remarks>
        /// The ledger stores the balance from the Low side, so the High side gets it negated. The value
        /// saturates at <see cref="decimal.MinValue"/>/<see cref="decimal.MaxValue"/> for a balance
        /// beyond <see cref="decimal"/> (an issuer's supply can be) and is meant for comparisons and
        /// display. Use <see cref="GetBalanceValueFor"/> for an amount that goes back to the ledger.
        /// </remarks>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The balance; zero when a sparse entry does not carry one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        /// <exception cref="FormatException">The stored balance is not a number.</exception>
        public static decimal GetBalanceFor(this LORippleState state, string account)
        {
            // Validate before reading the balance, so a null state, a null or off-line account and a
            // malformed balance are reported in that order and the first two as documented.
            bool isLow = state.IsLowSide(account);
            decimal stored = state.Balance.ToDecimalClamped();

            return isLow ? stored : -stored;
        }

        /// <summary>
        /// The balance from the point of view of <paramref name="account"/> as the ledger's own text,
        /// with every digit kept. The counterpart of <see cref="GetBalanceFor"/> for an amount sent back
        /// to the ledger, where a saturated or rounded <see cref="decimal"/> would leave part of the
        /// balance behind.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>
        /// The stored text for the Low side; the text with its sign flipped for the High side (a zero
        /// stays unsigned). Null when the entry has no balance, and for the High side when the stored
        /// text is not a number.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static string GetBalanceValueFor(this LORippleState state, string account)
        {
            bool isLow = state.IsLowSide(account);
            string stored = state.Balance?.Value;

            return isLow ? stored : NegateAmountText(stored);
        }

        /// <summary>
        /// Whether the line is in the owner count of <paramref name="account"/>: the reserve flag of its
        /// side is set (<c>lsfLowReserve</c> for the Low side, <c>lsfHighReserve</c> for the High side).
        /// </summary>
        /// <remarks>
        /// A line can sit in the owner count of one side only, of both, or, once a side has cleared its
        /// flag, of neither. The flag says whose owner count carries the line; a sponsor
        /// (<see cref="LORippleState.LowSponsor"/>/<see cref="LORippleState.HighSponsor"/>) may pay the
        /// reserve for that side instead of the account itself.
        /// </remarks>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>True when the line counts toward the account's owner reserve; false when it does not or the entry has no flag word.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static bool IsReserveSetFor(this LORippleState state, string account)
            => state.GetFlagOf(account, RippleStateFlags.lsfLowReserve, RippleStateFlags.lsfHighReserve);

        /// <summary>
        /// Whether the line is in the owner count of the counterparty of <paramref name="account"/>.
        /// The mirror of <see cref="IsReserveSetFor"/>.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>True when the line counts toward the counterparty's owner reserve; false when it does not or the entry has no flag word.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static bool IsPeerReserveSetFor(this LORippleState state, string account)
            => state.GetPeerFlagOf(account, RippleStateFlags.lsfLowReserve, RippleStateFlags.lsfHighReserve);

        /// <summary>
        /// Whether a flag on the side of <paramref name="account"/> is set: pass the Low and the High
        /// variant of the same flag (for example <c>lsfLowFreeze</c> and <c>lsfHighFreeze</c>) and the
        /// one that belongs to the account is tested.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <param name="lowFlag">The flag as set by the Low account.</param>
        /// <param name="highFlag">The flag as set by the High account.</param>
        /// <returns>True when the flag is set; false when it is not or the entry has no flag word.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static bool GetFlagOf(this LORippleState state, string account, RippleStateFlags lowFlag, RippleStateFlags highFlag)
            => state.IsLowSide(account) ? state.Flags.IsFlagSet(lowFlag) : state.Flags.IsFlagSet(highFlag);

        /// <summary>
        /// Whether a flag on the side of the counterparty of <paramref name="account"/> is set. The
        /// mirror of <see cref="GetFlagOf"/>.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <param name="lowFlag">The flag as set by the Low account.</param>
        /// <param name="highFlag">The flag as set by the High account.</param>
        /// <returns>True when the flag is set; false when it is not or the entry has no flag word.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static bool GetPeerFlagOf(this LORippleState state, string account, RippleStateFlags lowFlag, RippleStateFlags highFlag)
            => state.IsLowSide(account) ? state.Flags.IsFlagSet(highFlag) : state.Flags.IsFlagSet(lowFlag);

        /// <summary>
        /// The inbound quality <paramref name="account"/> has set, in billionths. The raw ledger field:
        /// null or 0 means face value, and nothing is substituted for an absent field.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The quality, or null when the field is absent.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static uint? GetQualityInOf(this LORippleState state, string account)
            => state.IsLowSide(account) ? state.LowQualityIn : state.HighQualityIn;

        /// <summary>
        /// The outbound quality <paramref name="account"/> has set. Same encoding as <see cref="GetQualityInOf"/>.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The quality, or null when the field is absent.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static uint? GetQualityOutOf(this LORippleState state, string account)
            => state.IsLowSide(account) ? state.LowQualityOut : state.HighQualityOut;

        /// <summary>
        /// The inbound quality the counterparty of <paramref name="account"/> has set.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The quality, or null when the field is absent.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static uint? GetPeerQualityInOf(this LORippleState state, string account)
            => state.IsLowSide(account) ? state.HighQualityIn : state.LowQualityIn;

        /// <summary>
        /// The outbound quality the counterparty of <paramref name="account"/> has set.
        /// </summary>
        /// <param name="state">The trust line.</param>
        /// <param name="account">Classic address of an account on the line.</param>
        /// <returns>The quality, or null when the field is absent.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="account"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="account"/> is not on the line (see <see cref="IsLowSide"/>).</exception>
        public static uint? GetPeerQualityOutOf(this LORippleState state, string account)
            => state.IsLowSide(account) ? state.HighQualityOut : state.LowQualityOut;

        /// <summary>
        /// The trust line as it was before a transaction: the <c>PreviousFields</c> of a metadata node laid
        /// over its <c>FinalFields</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>PreviousFields</c> lists only the fields the transaction changed, so every field it leaves out
        /// comes from <paramref name="final"/>. That includes <c>Flags</c>: a null
        /// <see cref="LORippleState.Flags"/> in <paramref name="previous"/> is "unchanged", while a
        /// <c>Flags</c> of 0 is a real value (the transaction cleared every flag) and wins.
        /// </para>
        /// <para>
        /// A field the transaction added (a quality set for the first time) is not in
        /// <c>PreviousFields</c> either, and the merge cannot tell that from "unchanged": it keeps the
        /// final value.
        /// </para>
        /// </remarks>
        /// <param name="final">The entry's <c>FinalFields</c>.</param>
        /// <param name="previous">The entry's <c>PreviousFields</c>, or null when the node has none.</param>
        /// <returns>
        /// <paramref name="final"/> itself when <paramref name="previous"/> is null, otherwise a new
        /// entry; neither input is changed.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="final"/> is null.</exception>
        public static LORippleState OverlayPreviousFields(this LORippleState final, LORippleState previous)
        {
            ArgumentNullException.ThrowIfNull(final);

            if (previous is null)
            {
                return final;
            }

            return new LORippleState
            {
                // ?? and not a comparison with 0: Flags and the other value-typed fields are nullable.
                Flags = previous.Flags ?? final.Flags,
                Balance = previous.Balance ?? final.Balance,
                LowLimit = previous.LowLimit ?? final.LowLimit,
                HighLimit = previous.HighLimit ?? final.HighLimit,
                LowNode = previous.LowNode ?? final.LowNode,
                HighNode = previous.HighNode ?? final.HighNode,
                LowQualityIn = previous.LowQualityIn ?? final.LowQualityIn,
                LowQualityOut = previous.LowQualityOut ?? final.LowQualityOut,
                HighQualityIn = previous.HighQualityIn ?? final.HighQualityIn,
                HighQualityOut = previous.HighQualityOut ?? final.HighQualityOut,
                LowSponsor = previous.LowSponsor ?? final.LowSponsor,
                HighSponsor = previous.HighSponsor ?? final.HighSponsor,
                PreviousTxnID = previous.PreviousTxnID ?? final.PreviousTxnID,
                PreviousTxnLgrSeq = previous.PreviousTxnLgrSeq ?? final.PreviousTxnLgrSeq,
                // The identity of the entry is the final one's: PreviousFields never carries it.
                LedgerEntryType = final.LedgerEntryType ?? previous.LedgerEntryType,
                Index = final.Index,
                LedgerIndex = final.LedgerIndex,
                UnknownFields = MergeUnknownFields(final.UnknownFields, previous.UnknownFields),
            };
        }

        private static Dictionary<string, JsonElement> MergeUnknownFields(
            Dictionary<string, JsonElement> final,
            Dictionary<string, JsonElement> previous)
        {
            if (final is null && previous is null)
            {
                return null;
            }

            Dictionary<string, JsonElement> merged = final is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(final, StringComparer.Ordinal);

            if (previous is not null)
            {
                foreach (KeyValuePair<string, JsonElement> field in previous)
                {
                    merged[field.Key] = field.Value;
                }
            }

            return merged;
        }

        /// <summary>
        /// The same amount text with its sign flipped and every digit kept. A zero has no sign and stays
        /// a zero. Null for null and for text that is not a finite number.
        /// </summary>
        private static string NegateAmountText(string value)
        {
            if (value is null)
            {
                return null;
            }

            string text = value.Trim();

            // double spans the ledger's whole range (1e-96 to 1e96), so it is used only to tell a number
            // from a non-number, never for the value itself.
            if (!double.TryParse(text, AmountStyles, CultureInfo.InvariantCulture, out double parsed)
                || !double.IsFinite(parsed))
            {
                return null;
            }

            bool negative = text[0] == '-';
            string unsigned = text[0] is '-' or '+' ? text[1..] : text;

            int exponent = unsigned.IndexOfAny(['e', 'E']);
            string mantissa = exponent < 0 ? unsigned : unsigned[..exponent];
            if (mantissa.AsSpan().Trim("0.").IsEmpty)
            {
                return unsigned;
            }

            return negative ? unsigned : "-" + unsigned;
        }
    }
}
