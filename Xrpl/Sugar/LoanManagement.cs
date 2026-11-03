using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;
using Xrpl.Utils;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>What a <c>LoanManage</c> does to the loan, by the flag it carries.</summary>
    public enum LoanManageAction
    {
        /// <summary>
        /// No action flag: the node accepts the transaction and moves no amount; under
        /// <c>fixCleanup3_1_3</c> it only rounds the entries' amount fields to the vault asset.
        /// </summary>
        None,

        /// <summary><c>tfLoanImpair</c>: books the loan's exposure as the vault's unrealized loss.</summary>
        Impair,

        /// <summary><c>tfLoanUnimpair</c>: reverses the unrealized loss an impairment booked.</summary>
        Unimpair,

        /// <summary><c>tfLoanDefault</c>: liquidates the broker's cover and realizes the rest of the exposure as the vault's loss.</summary>
        Default,
    }

    /// <summary>
    /// Whether one <c>LoanManage</c> action is allowed now, and from when its time condition holds:
    /// part of <see cref="LoanManageAvailability"/>.
    /// </summary>
    public sealed class LoanManageCheck
    {
        /// <summary>The action checked.</summary>
        public LoanManageAction Action { get; init; }

        /// <summary>
        /// The result the node would give instead: <c>tecNO_PERMISSION</c> or <c>tecTOO_SOON</c>;
        /// null when the action is allowed now.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when the action is allowed now.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the node accepts the action now.</summary>
        public bool IsAllowed => Refusal == null;

        /// <summary>
        /// The first parent-ledger close time (UTC) at which the action's time condition holds: the
        /// transaction must land in a ledger whose parent closed at or after it. Null when the action
        /// has no time condition under the rules given, or the loan has no <c>NextPaymentDueDate</c>.
        /// It says nothing about the other conditions: a loan that cannot be changed at all still has it.
        /// </summary>
        public DateTime? AllowedAt { get; init; }
    }

    /// <summary>
    /// Which <c>LoanManage</c> actions an account may take on a loan at a given time: the result of
    /// <see cref="LoanManagement.Availability"/>.
    /// </summary>
    public sealed class LoanManageAvailability
    {
        /// <summary><c>tfLoanImpair</c>.</summary>
        public LoanManageCheck Impair { get; init; }

        /// <summary><c>tfLoanUnimpair</c>.</summary>
        public LoanManageCheck Unimpair { get; init; }

        /// <summary><c>tfLoanDefault</c>.</summary>
        public LoanManageCheck Default { get; init; }

        /// <summary>
        /// The first parent close time from which the loan can be impaired, under
        /// <c>fixCleanup3_4_0</c> one second after <c>NextPaymentDueDate</c>. Null before the
        /// amendment, when impairment has no time condition.
        /// </summary>
        public DateTime? ImpairableAt => Impair?.AllowedAt;

        /// <summary>
        /// The first parent close time from which the loan can be defaulted: <c>NextPaymentDueDate</c>
        /// plus <c>GracePeriod</c>, and one second more under <c>fixCleanup3_4_0</c>.
        /// </summary>
        public DateTime? DefaultableAt => Default?.AllowedAt;
    }

    /// <summary>
    /// What a <c>LoanManage</c> would do to the vault, the broker and the loan, or why the node would
    /// refuse it: the result of <see cref="LoanManagement.Impair"/>, <see cref="LoanManagement.Unimpair"/>
    /// and <see cref="LoanManagement.Default"/>. Amounts are in the vault asset's own unit - drops for
    /// XRP - as the <c>Vault</c> and <c>LoanBroker</c> fields are.
    /// </summary>
    /// <remarks>
    /// When the node would refuse the action, every "after" value equals its "before" value and
    /// <see cref="DefaultCovered"/> and <see cref="VaultLoss"/> are zero. <see cref="Exposure"/> is
    /// filled either way.
    /// </remarks>
    public sealed class LoanManageQuote
    {
        /// <summary>The action quoted.</summary>
        public LoanManageAction Action { get; init; }

        /// <summary>
        /// The loan's exposure (<see cref="LoanManagement.Exposure"/>): what an impairment books as
        /// unrealized loss, an unimpairment reverses and a default writes off.
        /// </summary>
        public decimal Exposure { get; init; }

        /// <summary>The part of a default the broker's first-loss cover pays to the vault; zero for the other actions.</summary>
        public decimal DefaultCovered { get; init; }

        /// <summary>
        /// The loss a default realizes for the vault's depositors: the exposure less
        /// <see cref="DefaultCovered"/>, rounded down to the scale of <c>AssetsTotal</c>. Zero for the
        /// other actions, which move only the unrealized loss.
        /// </summary>
        public decimal VaultLoss { get; init; }

        /// <summary>The vault's <c>AssetsTotal</c> before the transaction.</summary>
        public decimal AssetsTotalBefore { get; init; }

        /// <summary>The vault's <c>AssetsTotal</c> after the transaction.</summary>
        public decimal AssetsTotalAfter { get; init; }

        /// <summary>The vault's <c>AssetsAvailable</c> before the transaction.</summary>
        public decimal AssetsAvailableBefore { get; init; }

        /// <summary>The vault's <c>AssetsAvailable</c> after the transaction.</summary>
        public decimal AssetsAvailableAfter { get; init; }

        /// <summary>The vault's <c>LossUnrealized</c> before the transaction.</summary>
        public decimal LossUnrealizedBefore { get; init; }

        /// <summary>The vault's <c>LossUnrealized</c> after the transaction.</summary>
        public decimal LossUnrealizedAfter { get; init; }

        /// <summary>The broker's <c>DebtTotal</c> before a default; null for the other actions, which do not touch the broker.</summary>
        public decimal? DebtTotalBefore { get; init; }

        /// <summary>The broker's <c>DebtTotal</c> after a default; null for the other actions.</summary>
        public decimal? DebtTotalAfter { get; init; }

        /// <summary>The broker's <c>CoverAvailable</c> before a default; null for the other actions.</summary>
        public decimal? CoverAvailableBefore { get; init; }

        /// <summary>The broker's <c>CoverAvailable</c> after a default; null for the other actions.</summary>
        public decimal? CoverAvailableAfter { get; init; }

        /// <summary>
        /// The loan's <c>NextPaymentDueDate</c> after the transaction (UTC). Before
        /// <c>fixCleanup3_4_0</c> an impairment moves a date not yet passed to the parent close time
        /// and an unimpairment recomputes it; under the amendment neither touches it. Null after a
        /// default, which removes the field.
        /// </summary>
        public DateTime? NextPaymentDueDateAfter { get; init; }

        /// <summary>
        /// What one share is worth before the transaction, in the vault asset's unit:
        /// (<c>AssetsTotal</c> - <c>LossUnrealized</c>) / the shares outstanding, the price a
        /// withdrawal is computed at before it is rounded to the asset. Null when the shares
        /// outstanding were not given or are zero. A sole shareholder withdraws without bearing the
        /// unrealized loss under <c>fixCleanup3_2_0</c>, so for them a share is worth more.
        /// </summary>
        public decimal? ShareValueBefore { get; init; }

        /// <summary>What one share is worth after the transaction; see <see cref="ShareValueBefore"/>.</summary>
        public decimal? ShareValueAfter { get; init; }

        /// <summary>
        /// The result the node would give instead, such as <c>tecNO_PERMISSION</c>,
        /// <c>tecTOO_SOON</c> or <c>tecLIMIT_EXCEEDED</c>; null when these checks pass.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when these checks pass.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the transaction passes the checks <see cref="LoanManagement"/> repeats.</summary>
        public bool IsAccepted => Refusal == null;
    }

    /// <summary>
    /// A loan broker's <c>LoanManage</c> computed offline from the <c>Loan</c>, <c>LoanBroker</c> and
    /// <c>Vault</c> entries, the way rippled 3.4.1's <c>LoanManage</c> transactor and
    /// <c>loanVaultExposure</c> compute it: when impairing, unimpairing and defaulting a loan is
    /// allowed, and what each does to the vault, the broker's cover and the depositors' share value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exposure depends on the vault's accounting basis. A vault created under
    /// <c>LendingProtocolV1_1</c> carries <c>LEVersion</c> 1 (cash basis), and its exposure is the
    /// loan's <c>PrincipalOutstanding</c>. Every other vault keeps accrual accounting, whatever the
    /// amendment's state later, and its exposure is <c>TotalValueOutstanding</c> less
    /// <c>ManagementFeeOutstanding</c>.
    /// </para>
    /// <para>
    /// <c>fixCleanup3_4_0</c> changes the time conditions. Under it a loan can be impaired only once
    /// the parent close time is past <c>NextPaymentDueDate</c>, and defaulted only once it is past
    /// <c>NextPaymentDueDate</c> + <c>GracePeriod</c>; both boundaries are exclusive. Before it,
    /// impairment has no time condition and the default boundary is inclusive, and impairing and
    /// unimpairing move <c>NextPaymentDueDate</c>.
    /// </para>
    /// <para>
    /// Repeated here is everything that depends on the entries and the time: the state transitions,
    /// the owner check, the time conditions, the amounts and their rounding, and the refusals they lead
    /// to. Signatures, fees, the transfer of the cover between the pseudo-accounts and the
    /// <c>FixedPrecision</c> vault version of rippled develop (<c>LEVersion</c> 2, not in any release
    /// yet) are not: a vault of that version throws <see cref="NotSupportedException"/>.
    /// <see cref="PreviewSugar.PreviewLoanManage"/> runs the transaction through the node.
    /// </para>
    /// </remarks>
    public static class LoanManagement
    {
        private static readonly DateTime RippleEpoch = DateTimeUtils.RippleEpoch.UtcDateTime;

        /// <summary>
        /// The loan's exposure for the vault (<c>loanVaultExposure</c>): <c>PrincipalOutstanding</c>
        /// for a cash-basis vault (<c>LEVersion</c> 1), <c>TotalValueOutstanding</c> less
        /// <c>ManagementFeeOutstanding</c> otherwise. In the vault asset's unit.
        /// </summary>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="vault">The vault of the loan's broker.</param>
        /// <param name="rules">The amendments in force, for the <c>Number</c> arithmetic; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset.</exception>
        /// <exception cref="NotSupportedException">The vault's <c>LEVersion</c> is above 1: the <c>FixedPrecision</c> vault of rippled develop, which this port does not cover.</exception>
        /// <exception cref="OverflowException">The exposure is beyond <see cref="decimal"/>.</exception>
        public static decimal Exposure(LOLoan loan, LOVault vault, LedgerRules rules = null)
        {
            ManageState state = ManageState.Of(loan, null, vault, rules);
            return LoanSchedule.ToDecimal(state.Exposure);
        }

        /// <summary>
        /// Which actions <paramref name="account"/> may take on the loan in a ledger whose parent closed
        /// at <paramref name="parentCloseTime"/>, with the result <c>LoanManage::preclaim</c> and
        /// <c>LoanManage::doApply</c> give otherwise, checked in their order.
        /// </summary>
        /// <remarks>
        /// A defaulted or fully paid loan cannot be changed (<c>tecNO_PERMISSION</c>); an impaired loan
        /// cannot be impaired again and only an impaired one can be unimpaired
        /// (<c>tecNO_PERMISSION</c>); only the broker's owner may act (<c>tecNO_PERMISSION</c>). The
        /// time conditions give <c>tecTOO_SOON</c>; the node checks the default's in preclaim, before
        /// the owner, and the impairment's in doApply, where the fee is still charged.
        /// </remarks>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="broker">The loan's <c>LoanBroker</c> entry.</param>
        /// <param name="account">The account that would submit the transaction; null skips the owner check.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in. A local time is
        /// converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The broker is not the loan's.</exception>
        public static LoanManageAvailability Availability(
            LOLoan loan,
            LOLoanBroker broker,
            string account,
            DateTime parentCloseTime,
            LedgerRules rules = null)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (broker == null)
                throw new ArgumentNullException(nameof(broker));

            CheckBroker(loan, broker);
            rules ??= new LedgerRules();

            (string Result, string Reason)? ownership = account != null && !string.Equals(broker.Owner, account, StringComparison.Ordinal)
                ? ("tecNO_PERMISSION", "Only the owner of the loan's broker can manage the loan.")
                : null;

            return new LoanManageAvailability
            {
                Impair = Check(LoanManageAction.Impair, loan, parentCloseTime, rules, ownership),
                Unimpair = Check(LoanManageAction.Unimpair, loan, parentCloseTime, rules, ownership),
                Default = Check(LoanManageAction.Default, loan, parentCloseTime, rules, ownership),
            };
        }

        /// <summary>
        /// A <c>LoanManage</c> with <c>tfLoanImpair</c>: the exposure added to the vault's
        /// <c>LossUnrealized</c>, rounded to the scale of <c>AssetsTotal</c>.
        /// </summary>
        /// <remarks>
        /// Besides the state transitions and, under <c>fixCleanup3_4_0</c>, the late-payment condition
        /// (<c>tecTOO_SOON</c>), the node refuses an impairment that would take <c>LossUnrealized</c>
        /// past the assets the vault has lent out, <c>AssetsTotal</c> - <c>AssetsAvailable</c>
        /// (<c>tecLIMIT_EXCEEDED</c>). The owner is not checked; <see cref="Availability"/> does that.
        /// </remarks>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="vault">The vault of the loan's broker.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in. A local time is
        /// converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="sharesOutstanding">The vault share issuance's <c>OutstandingAmount</c>, for the share value; null leaves it out.</param>
        /// <exception cref="ArgumentException">The vault carries no asset.</exception>
        /// <exception cref="NotSupportedException">The vault's <c>LEVersion</c> is above 1: the <c>FixedPrecision</c> vault of rippled develop, which this port does not cover.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanManageQuote Impair(
            LOLoan loan,
            LOVault vault,
            DateTime parentCloseTime,
            LedgerRules rules = null,
            ulong? sharesOutstanding = null)
        {
            ManageState state = ManageState.Of(loan, null, vault, rules);
            NumberContext c = state.Context;
            const LoanManageAction action = LoanManageAction.Impair;

            if (StateRefusal(action, loan) is { } refusal)
                return Refused(action, state, sharesOutstanding, refusal);

            bool late = LoanPayments.IsPaymentLate(loan, CloseTime(parentCloseTime), dueTimeIsLate: !state.Rules.FixCleanup3_4_0);
            if (state.Rules.FixCleanup3_4_0 && !late)
                return Refused(action, state, sharesOutstanding, ("tecTOO_SOON", "Only a loan whose payment is late can be impaired."));

            XrplNumber loss = Adjust(state.LossUnrealized, state.Exposure, state);
            if (loss > XrplNumber.Subtract(state.AssetsTotal, state.AssetsAvailable, c))
            {
                return Refused(action, state, sharesOutstanding,
                    ("tecLIMIT_EXCEEDED", "The unrealized loss would exceed the assets the vault has lent out."));
            }

            DateTime? nextDue = loan.NextPaymentDueDate;
            if (!state.Rules.FixCleanup3_4_0 && !late)
                nextDue = CloseTime(parentCloseTime);

            return Accepted(action, state, sharesOutstanding, new VaultAfter(state.AssetsTotal, state.AssetsAvailable, loss), nextDue);
        }

        /// <summary>
        /// A <c>LoanManage</c> with <c>tfLoanUnimpair</c>: the exposure taken back out of the vault's
        /// <c>LossUnrealized</c>, rounded to the scale of <c>AssetsTotal</c>.
        /// </summary>
        /// <remarks>
        /// Before <c>fixCleanup3_4_0</c> the node also recomputes <c>NextPaymentDueDate</c>: the
        /// regular due date (<c>PreviousPaymentDueDate</c> or <c>StartDate</c>, whichever is later,
        /// plus <c>PaymentInterval</c>) while it has not passed, else the parent close time plus
        /// <c>PaymentInterval</c>. The owner is not checked; <see cref="Availability"/> does that.
        /// </remarks>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="vault">The vault of the loan's broker.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in, for the due date
        /// before <c>fixCleanup3_4_0</c>. A local time is converted to UTC; an unspecified one is
        /// taken as UTC.
        /// </param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="sharesOutstanding">The vault share issuance's <c>OutstandingAmount</c>, for the share value; null leaves it out.</param>
        /// <exception cref="ArgumentException">The vault carries no asset.</exception>
        /// <exception cref="NotSupportedException">The vault's <c>LEVersion</c> is above 1: the <c>FixedPrecision</c> vault of rippled develop, which this port does not cover.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanManageQuote Unimpair(
            LOLoan loan,
            LOVault vault,
            DateTime parentCloseTime,
            LedgerRules rules = null,
            ulong? sharesOutstanding = null)
        {
            ManageState state = ManageState.Of(loan, null, vault, rules);
            const LoanManageAction action = LoanManageAction.Unimpair;

            if (StateRefusal(action, loan) is { } refusal)
                return Refused(action, state, sharesOutstanding, refusal);

            if (state.LossUnrealized < state.Exposure)
            {
                return Refused(action, state, sharesOutstanding,
                    ("tefBAD_LEDGER", "The vault's unrealized loss is less than the loan's exposure."));
            }

            XrplNumber loss = Adjust(state.LossUnrealized, -state.Exposure, state);

            DateTime? nextDue = loan.NextPaymentDueDate;
            if (!state.Rules.FixCleanup3_4_0)
            {
                uint interval = loan.PaymentInterval ?? 0;
                DateTime start = loan.StartDate ?? RippleEpoch;
                DateTime previous = loan.PreviousPaymentDueDate ?? RippleEpoch;
                DateTime regular = (previous > start ? previous : start).AddSeconds(interval);
                DateTime now = CloseTime(parentCloseTime);
                nextDue = now >= regular ? now.AddSeconds(interval) : regular;
            }

            return Accepted(action, state, sharesOutstanding, new VaultAfter(state.AssetsTotal, state.AssetsAvailable, loss), nextDue);
        }

        /// <summary>
        /// A <c>LoanManage</c> with <c>tfLoanDefault</c>: what the broker's first-loss cover pays to
        /// the vault and what the vault's depositors lose.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The cover pays <c>CoverRateLiquidation</c> of the minimum cover (<c>CoverRateMinimum</c> of
        /// <c>DebtTotal</c>), at most the exposure, rounded up at the loan's scale and capped at
        /// <c>CoverAvailable</c>. The rest of the exposure, rounded down at the scale of
        /// <c>AssetsTotal</c>, comes off <c>AssetsTotal</c>; the cover paid is added to
        /// <c>AssetsAvailable</c>. An impaired loan also takes its exposure out of
        /// <c>LossUnrealized</c>. The broker's <c>DebtTotal</c> drops by the exposure and its
        /// <c>CoverAvailable</c> by the cover paid.
        /// </para>
        /// <para>
        /// The time condition is checked only when <paramref name="parentCloseTime"/> is given; the
        /// owner is not checked; <see cref="Availability"/> does both.
        /// </para>
        /// </remarks>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="broker">The loan's <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in, for the grace period;
        /// null skips that check. A local time is converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="sharesOutstanding">The vault share issuance's <c>OutstandingAmount</c>, for the share value; null leaves it out.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or the entries do not belong together.</exception>
        /// <exception cref="NotSupportedException">The vault's <c>LEVersion</c> is above 1: the <c>FixedPrecision</c> vault of rippled develop, which this port does not cover.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanManageQuote Default(
            LOLoan loan,
            LOLoanBroker broker,
            LOVault vault,
            LedgerRules rules = null,
            DateTime? parentCloseTime = null,
            ulong? sharesOutstanding = null)
        {
            if (broker == null)
                throw new ArgumentNullException(nameof(broker));

            ManageState state = ManageState.Of(loan, broker, vault, rules);
            NumberContext c = state.Context;
            const LoanManageAction action = LoanManageAction.Default;

            if (StateRefusal(action, loan) is { } refusal)
                return Refused(action, state, sharesOutstanding, refusal);

            if (parentCloseTime is { } time && TimeRefusal(action, loan, time, state.Rules) is { } tooSoon)
                return Refused(action, state, sharesOutstanding, tooSoon);

            XrplNumber exposure = state.Exposure;

            // The minimum cover and the liquidated part are both rounded up, inside one upward guard.
            NumberContext up = c.WithRounding(NumberRounding.Upward);
            XrplNumber minimumCover = LendingMath.TenthBipsOf(state.DebtTotal, state.CoverRateMinimum, up);
            XrplNumber liquidated = Min(LendingMath.TenthBipsOf(minimumCover, state.CoverRateLiquidation, up), exposure);
            XrplNumber covered = Min(AssetRounding.Round(liquidated, state.Integral, state.LoanScale, up), state.Cover);

            XrplNumber vaultDefault = XrplNumber.Subtract(exposure, covered, c);
            if (state.AssetsTotal < vaultDefault)
            {
                return Refused(action, state, sharesOutstanding,
                    ("tefBAD_LEDGER", "The vault's total assets are less than the loss the default realizes."));
            }

            XrplNumber loss = AssetRounding.Round(vaultDefault, state.Integral, state.VaultScale, c.WithRounding(NumberRounding.Downward));
            XrplNumber total = XrplNumber.Subtract(state.AssetsTotal, loss, c);
            XrplNumber available = XrplNumber.Add(state.AssetsAvailable, covered, c);
            if (available > total && !state.Integral)
            {
                // A difference more than 13 orders of magnitude below the available assets is dust
                // left by the two roundings; the node raises the total to match.
                XrplNumber difference = XrplNumber.Subtract(available, total, c);
                if ((long)available.Exponent - difference.Exponent > 13)
                    total = available;
            }

            if (available > total)
            {
                return Refused(action, state, sharesOutstanding,
                    ("tecINTERNAL", "The vault's available assets would exceed its total assets."));
            }

            XrplNumber lossUnrealized = state.LossUnrealized;
            if (IsImpaired(loan))
            {
                if (lossUnrealized < exposure)
                {
                    return Refused(action, state, sharesOutstanding,
                        ("tefBAD_LEDGER", "The vault's unrealized loss is less than the loan's exposure."));
                }

                lossUnrealized = Adjust(lossUnrealized, -exposure, state);
            }

            BrokerAfter brokerAfter = new BrokerAfter(
                Adjust(state.DebtTotal, -exposure, state),
                XrplNumber.Subtract(state.Cover, covered, c),
                covered,
                loss);
            return Accepted(action, state, sharesOutstanding, new VaultAfter(total, available, lossUnrealized), nextDue: null, brokerAfter);
        }

        /// <summary>The checks of one action, in the order the node makes them.</summary>
        private static LoanManageCheck Check(
            LoanManageAction action,
            LOLoan loan,
            DateTime parentCloseTime,
            LedgerRules rules,
            (string Result, string Reason)? ownership)
        {
            // preclaim: the state transitions, then the default's time, then the owner; doApply: the
            // impairment's time.
            (string Result, string Reason)? refusal = StateRefusal(action, loan);
            if (refusal == null && action == LoanManageAction.Default)
                refusal = TimeRefusal(action, loan, parentCloseTime, rules);
            refusal ??= ownership;
            if (refusal == null && action == LoanManageAction.Impair)
                refusal = TimeRefusal(action, loan, parentCloseTime, rules);

            return new LoanManageCheck
            {
                Action = action,
                Refusal = refusal?.Result,
                RefusalReason = refusal?.Reason,
                AllowedAt = AllowedAt(action, loan, rules),
            };
        }

        /// <summary>The state transitions <c>LoanManage::preclaim</c> allows.</summary>
        private static (string Result, string Reason)? StateRefusal(LoanManageAction action, LOLoan loan)
        {
            if (IsDefaulted(loan))
                return ("tecNO_PERMISSION", "A defaulted loan cannot be changed.");
            if (action == LoanManageAction.Impair && IsImpaired(loan))
                return ("tecNO_PERMISSION", "The loan is already impaired.");
            if (action == LoanManageAction.Unimpair && !IsImpaired(loan))
                return ("tecNO_PERMISSION", "Only an impaired loan can be unimpaired.");
            if ((loan.PaymentRemaining ?? 0) == 0)
                return ("tecNO_PERMISSION", "A fully paid loan cannot be changed.");

            return null;
        }

        /// <summary>The time condition of an impairment (under <c>fixCleanup3_4_0</c>) or a default.</summary>
        private static (string Result, string Reason)? TimeRefusal(LoanManageAction action, LOLoan loan, DateTime parentCloseTime, LedgerRules rules)
        {
            bool fix = rules.FixCleanup3_4_0;
            // A close time is whole seconds: a fraction past an exclusive boundary is not past it for the node.
            DateTime closeTime = CloseTime(parentCloseTime);
            switch (action)
            {
                case LoanManageAction.Impair when fix && !LoanPayments.IsPaymentLate(loan, closeTime, dueTimeIsLate: false):
                    return ("tecTOO_SOON", "Only a loan whose payment is late can be impaired.");
                case LoanManageAction.Default when !LoanPayments.IsPastGracePeriod(loan, closeTime, boundaryIsPast: !fix):
                    return ("tecTOO_SOON", "A loan can be defaulted only after its grace period.");
                default:
                    return null;
            }
        }

        /// <summary>
        /// The first parent close time the time condition holds at: the boundary itself when it is
        /// inclusive, the second after it when it is exclusive.
        /// </summary>
        private static DateTime? AllowedAt(LoanManageAction action, LOLoan loan, LedgerRules rules)
        {
            if (loan.NextPaymentDueDate is not { } due)
                return null;

            int exclusive = rules.FixCleanup3_4_0 ? 1 : 0;
            DateTime boundary = CloseTime(due);
            return action switch
            {
                LoanManageAction.Impair when rules.FixCleanup3_4_0 => boundary.AddSeconds(exclusive),
                LoanManageAction.Default => boundary.AddSeconds((loan.GracePeriod ?? 0) + exclusive),
                _ => null,
            };
        }

        private static void CheckBroker(LOLoan loan, LOLoanBroker broker)
        {
            string brokerId = broker.Index ?? broker.LedgerIndex;
            if (loan.LoanBrokerID != null && brokerId != null && !string.Equals(loan.LoanBrokerID, brokerId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The broker {brokerId} is not the loan's broker {loan.LoanBrokerID}.", nameof(broker));
        }

        /// <summary>
        /// <c>adjustImpreciseNumber</c>: the value plus the adjustment, rounded to the asset at the
        /// scale of the vault's <c>AssetsTotal</c>, and never below zero.
        /// </summary>
        private static XrplNumber Adjust(XrplNumber value, XrplNumber adjustment, ManageState state)
        {
            XrplNumber adjusted = AssetRounding.Round(XrplNumber.Add(value, adjustment, state.Context), state.Integral, state.VaultScale, state.Context);
            return adjusted < XrplNumber.Zero ? XrplNumber.Zero : adjusted;
        }

        /// <summary>
        /// The value as the entry stores it. Under <c>fixCleanup3_1_3</c> a <c>LoanManage</c> that
        /// applies associates every amount field with the vault asset, which rounds it to the asset.
        /// </summary>
        private static XrplNumber Stored(XrplNumber value, ManageState state) =>
            state.Rules.FixCleanup3_1_3 ? AssetRounding.ToAmount(value, state.Integral, state.Context) : value;

        private static LoanManageQuote Accepted(
            LoanManageAction action,
            ManageState state,
            ulong? sharesOutstanding,
            VaultAfter after,
            DateTime? nextDue,
            BrokerAfter? broker = null)
        {
            XrplNumber total = Stored(after.AssetsTotal, state);
            XrplNumber available = Stored(after.AssetsAvailable, state);
            XrplNumber loss = Stored(after.LossUnrealized, state);
            bool defaulted = broker.HasValue;
            BrokerAfter b = broker.GetValueOrDefault();
            return new LoanManageQuote
            {
                Action = action,
                Exposure = LoanSchedule.ToDecimal(state.Exposure),
                DefaultCovered = defaulted ? LoanSchedule.ToDecimal(b.Covered) : 0m,
                VaultLoss = defaulted ? LoanSchedule.ToDecimal(b.Realized) : 0m,
                DebtTotalBefore = defaulted ? LoanSchedule.ToDecimal(state.DebtTotal) : null,
                DebtTotalAfter = defaulted ? LoanSchedule.ToDecimal(Stored(b.DebtTotal, state)) : null,
                CoverAvailableBefore = defaulted ? LoanSchedule.ToDecimal(state.Cover) : null,
                CoverAvailableAfter = defaulted ? LoanSchedule.ToDecimal(Stored(b.Cover, state)) : null,
                AssetsTotalBefore = LoanSchedule.ToDecimal(state.AssetsTotal),
                AssetsTotalAfter = LoanSchedule.ToDecimal(total),
                AssetsAvailableBefore = LoanSchedule.ToDecimal(state.AssetsAvailable),
                AssetsAvailableAfter = LoanSchedule.ToDecimal(available),
                LossUnrealizedBefore = LoanSchedule.ToDecimal(state.LossUnrealized),
                LossUnrealizedAfter = LoanSchedule.ToDecimal(loss),
                NextPaymentDueDateAfter = nextDue,
                ShareValueBefore = ShareValue(state.AssetsTotal, state.LossUnrealized, sharesOutstanding, state.Context),
                ShareValueAfter = ShareValue(total, loss, sharesOutstanding, state.Context),
            };
        }

        private static LoanManageQuote Refused(LoanManageAction action, ManageState state, ulong? sharesOutstanding, (string Result, string Reason) refusal)
        {
            decimal total = LoanSchedule.ToDecimal(state.AssetsTotal);
            decimal available = LoanSchedule.ToDecimal(state.AssetsAvailable);
            decimal loss = LoanSchedule.ToDecimal(state.LossUnrealized);
            decimal? shareValue = ShareValue(state.AssetsTotal, state.LossUnrealized, sharesOutstanding, state.Context);
            decimal? debt = state.HasBroker ? LoanSchedule.ToDecimal(state.DebtTotal) : null;
            decimal? cover = state.HasBroker ? LoanSchedule.ToDecimal(state.Cover) : null;
            return new LoanManageQuote
            {
                Action = action,
                Exposure = LoanSchedule.ToDecimal(state.Exposure),
                AssetsTotalBefore = total,
                AssetsTotalAfter = total,
                AssetsAvailableBefore = available,
                AssetsAvailableAfter = available,
                LossUnrealizedBefore = loss,
                LossUnrealizedAfter = loss,
                DebtTotalBefore = debt,
                DebtTotalAfter = debt,
                CoverAvailableBefore = cover,
                CoverAvailableAfter = cover,
                NextPaymentDueDateAfter = state.NextPaymentDueDate,
                ShareValueBefore = shareValue,
                ShareValueAfter = shareValue,
                Refusal = refusal.Result,
                RefusalReason = refusal.Reason,
            };
        }

        /// <summary>(<c>AssetsTotal</c> - <c>LossUnrealized</c>) per share; null without shares.</summary>
        private static decimal? ShareValue(XrplNumber total, XrplNumber loss, ulong? shares, NumberContext c)
        {
            if (shares is not { } outstanding || outstanding == 0)
                return null;

            XrplNumber net = XrplNumber.Subtract(total, loss, c);
            return LoanSchedule.ToDecimal(XrplNumber.Divide(net, (XrplNumber)checked((long)outstanding), c));
        }

        private static bool IsDefaulted(LOLoan loan) => ((loan.Flags ?? 0) & LoanFlags.lsfLoanDefault) != 0;

        private static bool IsImpaired(LOLoan loan) => ((loan.Flags ?? 0) & LoanFlags.lsfLoanImpaired) != 0;

        /// <summary>A time as a ledger close time: UTC, whole seconds.</summary>
        private static DateTime CloseTime(DateTime time) => RippleEpoch.AddSeconds(LendingMath.RippleSeconds(time));

        private static XrplNumber Min(XrplNumber a, XrplNumber b) => b < a ? b : a;

        private readonly struct VaultAfter
        {
            public VaultAfter(XrplNumber assetsTotal, XrplNumber assetsAvailable, XrplNumber lossUnrealized)
            {
                AssetsTotal = assetsTotal;
                AssetsAvailable = assetsAvailable;
                LossUnrealized = lossUnrealized;
            }

            public XrplNumber AssetsTotal { get; }

            public XrplNumber AssetsAvailable { get; }

            public XrplNumber LossUnrealized { get; }
        }

        /// <summary>What a default leaves of the broker, with the cover it pays and the loss it realizes.</summary>
        private readonly struct BrokerAfter
        {
            public BrokerAfter(XrplNumber debtTotal, XrplNumber cover, XrplNumber covered, XrplNumber realized)
            {
                DebtTotal = debtTotal;
                Cover = cover;
                Covered = covered;
                Realized = realized;
            }

            public XrplNumber DebtTotal { get; }

            public XrplNumber Cover { get; }

            public XrplNumber Covered { get; }

            public XrplNumber Realized { get; }
        }

        /// <summary>The loan, broker and vault fields the computations read.</summary>
        private sealed class ManageState
        {
            public LedgerRules Rules { get; private init; }

            public NumberContext Context { get; private init; }

            public bool Integral { get; private init; }

            public XrplNumber Exposure { get; private init; }

            public int LoanScale { get; private init; }

            /// <summary><c>getAssetsTotalScale</c>: the scale of <c>AssetsTotal</c> before the transaction.</summary>
            public int VaultScale { get; private init; }

            public XrplNumber AssetsTotal { get; private init; }

            public XrplNumber AssetsAvailable { get; private init; }

            public XrplNumber LossUnrealized { get; private init; }

            public DateTime? NextPaymentDueDate { get; private init; }

            public bool HasBroker { get; private init; }

            public XrplNumber DebtTotal { get; private init; }

            public XrplNumber Cover { get; private init; }

            public uint CoverRateMinimum { get; private init; }

            public uint CoverRateLiquidation { get; private init; }

            public static ManageState Of(LOLoan loan, LOLoanBroker broker, LOVault vault, LedgerRules rules)
            {
                if (loan == null)
                    throw new ArgumentNullException(nameof(loan));
                if (vault == null)
                    throw new ArgumentNullException(nameof(vault));
                if (vault.Asset is not IssuedCurrency asset)
                    throw new ArgumentException("The vault carries no Asset.", nameof(vault));

                // rippled develop's FixedPrecision vault (LEVersion 2) defaults and impairs through
                // adjustVaultBalances with a cover taken from PrincipalOutstanding; the accrual
                // formulas below would give wrong numbers for it.
                if (vault.LEVersion > (uint)VaultVersion.CashBasis)
                    throw new NotSupportedException($"Vault version {vault.LEVersion} is not supported; only legacy (0) and cash-basis (1) vaults are.");

                if (broker != null)
                {
                    CheckBroker(loan, broker);
                    string vaultId = vault.Index ?? vault.LedgerIndex;
                    if (broker.VaultID != null && vaultId != null && !string.Equals(broker.VaultID, vaultId, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException($"The vault {vaultId} is not the broker's vault {broker.VaultID}.", nameof(vault));
                }

                rules ??= new LedgerRules();
                NumberContext c = rules.Context;
                bool integral = LoanPayments.IsIntegral(asset);
                XrplNumber assetsTotal = vault.AssetsTotal ?? XrplNumber.Zero;

                // loanVaultExposure: the accounting basis is fixed at the vault's creation.
                XrplNumber exposure = vault.LEVersion == (uint)VaultVersion.CashBasis
                    ? loan.PrincipalOutstanding ?? XrplNumber.Zero
                    : XrplNumber.Subtract(loan.TotalValueOutstanding ?? XrplNumber.Zero, loan.ManagementFeeOutstanding ?? XrplNumber.Zero, c);

                return new ManageState
                {
                    Rules = rules,
                    Context = c,
                    Integral = integral,
                    Exposure = exposure,
                    LoanScale = loan.LoanScale ?? 0,
                    VaultScale = AssetRounding.Scale(assetsTotal, integral, c),
                    AssetsTotal = assetsTotal,
                    AssetsAvailable = vault.AssetsAvailable ?? XrplNumber.Zero,
                    LossUnrealized = vault.LossUnrealized ?? XrplNumber.Zero,
                    NextPaymentDueDate = loan.NextPaymentDueDate,
                    HasBroker = broker != null,
                    DebtTotal = broker?.DebtTotal ?? XrplNumber.Zero,
                    Cover = broker?.CoverAvailable ?? XrplNumber.Zero,
                    CoverRateMinimum = broker?.CoverRateMinimum ?? 0,
                    CoverRateLiquidation = broker?.CoverRateLiquidation ?? 0,
                };
            }
        }
    }
}
