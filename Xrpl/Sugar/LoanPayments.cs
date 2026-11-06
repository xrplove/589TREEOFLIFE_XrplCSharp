using System;
using System.Numerics;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What a <c>Loan</c> entry says about the next payment, without asking the node.
    /// </summary>
    /// <remarks>
    /// <see cref="RegularPaymentCap"/> bounds the next regular payment from the entry alone.
    /// <see cref="LatePaymentDue"/> and <see cref="FullPaymentDue"/> repeat rippled's own
    /// computation for a given parent close time, and <see cref="PaymentForAmount"/> what a regular
    /// <c>LoanPay</c> does with a given <c>Amount</c> - several payments, an overpayment;
    /// <see cref="LoanSchedule"/> projects the regular payments.
    /// </remarks>
    public static class LoanPayments
    {
        /// <summary>
        /// The most the next regular payment can take, in the asset's own unit (drops for XRP): an
        /// amount that always settles it. On the final payment this is exact -
        /// <c>TotalValueOutstanding</c> plus <c>LoanServiceFee</c>. Before that it is
        /// <c>PeriodicPayment</c> rounded up to the asset (whole units for XRP and MPT, the loan's
        /// <c>LoanScale</c> for an issued currency) plus <c>LoanServiceFee</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// rippled charges a regular payment as the step from the loan's current state to the
        /// rounded theoretical state after it, capped at the rounded <c>PeriodicPayment</c>
        /// (<c>computePaymentComponents</c>). The step can come out a rounding unit lower - a 90 MPT
        /// loan over three payments with <c>PeriodicPayment</c> 30.00002 is charged 30, not 31 - and
        /// the node takes only what it charges, not the whole <c>Amount</c>. Paying this cap is
        /// therefore safe; the exact figure is what <see cref="PreviewSugar.PreviewLoanPay"/> reports.
        /// </para>
        /// <para>
        /// A late payment adds late interest and <c>LatePaymentFee</c> on top, which this does not
        /// cover; preview it.
        /// </para>
        /// </remarks>
        /// <returns>null when the entry carries no periodic payment, or a value beyond <see cref="decimal"/>.</returns>
        public static decimal? RegularPaymentCap(LOLoan loan, IssuedCurrency asset)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            bool integral = IsIntegral(asset);
            int scale = loan.LoanScale ?? 0;

            // Every part is rounded up before it becomes a decimal, the fee and the final remainder
            // included, so no conversion can leave the sum below what the node charges.
            if (RoundUpToAsset(loan.LoanServiceFee ?? XrplNumber.Zero, integral, scale) is not { } serviceFee)
                return null;

            XrplNumber? payment = loan.PaymentRemaining == 1 ? loan.TotalValueOutstanding : loan.PeriodicPayment;
            if (payment is not { } value || RoundUpToAsset(value, integral, scale) is not { } rounded)
                return null;

            try
            {
                return rounded + serviceFee;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether a payment made in a ledger whose parent closed at <paramref name="parentCloseTime"/>
        /// counts as late.
        /// </summary>
        /// <param name="loan">The loan entry.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the payment lands in. A local time is converted
        /// to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="dueTimeIsLate">
        /// Whether a payment exactly at <c>NextPaymentDueDate</c> is late. rippled counted it late
        /// until <c>fixCleanup3_4_0</c>, and on time since.
        /// </param>
        public static bool IsPaymentLate(LOLoan loan, DateTime parentCloseTime, bool dueTimeIsLate)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));

            return loan.NextPaymentDueDate is { } due && HasPassed(parentCloseTime, due, dueTimeIsLate);
        }

        /// <summary>
        /// Whether the broker may default the loan (<c>LoanManage</c> with <c>tfLoanDefault</c>):
        /// the grace period after <c>NextPaymentDueDate</c> has run out.
        /// </summary>
        /// <param name="loan">The loan entry.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in. A local time is
        /// converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="boundaryIsPast">
        /// Whether the exact end of the grace period already counts. rippled counted it until
        /// <c>fixCleanup3_4_0</c>, and not since.
        /// </param>
        public static bool IsPastGracePeriod(LOLoan loan, DateTime parentCloseTime, bool boundaryIsPast)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));

            return loan.NextPaymentDueDate is { } due
                && HasPassed(parentCloseTime, due.AddSeconds(loan.GracePeriod ?? 0), boundaryIsPast);
        }

        /// <summary>
        /// What a late payment (<c>LoanPay</c> with <c>tfLoanLatePayment</c>) costs in a ledger whose
        /// parent closed at <paramref name="parentCloseTime"/>: the regular payment, the service fee,
        /// <c>LatePaymentFee</c>, and late interest on the outstanding principal for the time past
        /// <c>NextPaymentDueDate</c>, computed the way rippled's <c>computeLatePayment</c> computes it.
        /// </summary>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="asset">The loan asset: the asset of the broker's vault.</param>
        /// <param name="managementFeeRate">The broker's <c>ManagementFeeRate</c>, in 1/10th of a basis point.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the payment lands in. A local time is
        /// converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="options">The amendments in force; the current rules when null.</param>
        /// <returns>
        /// null when the payment is not late at that time (the node answers <c>tecTOO_SOON</c>) or
        /// the loan is paid off.
        /// </returns>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanPaymentDue LatePaymentDue(
            LOLoan loan,
            IssuedCurrency asset,
            ushort managementFeeRate,
            DateTime parentCloseTime,
            LedgerRules options = null)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            options ??= new LedgerRules();
            uint remaining = loan.PaymentRemaining ?? 0;
            if (remaining == 0
                || loan.NextPaymentDueDate is not { } nextDue
                || !IsPaymentLate(loan, parentCloseTime, dueTimeIsLate: !options.FixCleanup3_4_0))
            {
                return null;
            }

            LoanTerms terms = LoanTerms.Of(loan, asset, managementFeeRate, options);
            NumberContext c = terms.Context;
            LoanPaymentParts regular = LendingMath.RegularPayment(LoanState.Of(loan, c), remaining, terms);

            XrplNumber lateInterest = LendingMath.LatePaymentInterest(
                loan.PrincipalOutstanding ?? XrplNumber.Zero,
                loan.LateInterestRate ?? 0,
                LendingMath.RippleSeconds(parentCloseTime),
                LendingMath.RippleSeconds(nextDue),
                c);
            (XrplNumber interest, XrplNumber fee) = LendingMath.RoundAndSplitInterest(lateInterest, terms, NumberRounding.ToNearest);

            XrplNumber untrackedFee = XrplNumber.Add(
                XrplNumber.Add(loan.LoanServiceFee ?? XrplNumber.Zero, loan.LatePaymentFee ?? XrplNumber.Zero, c),
                fee,
                c);
            return LoanPaymentDue.From(regular, interest, untrackedFee, c);
        }

        /// <summary>
        /// What closing the loan early (<c>LoanPay</c> with <c>tfLoanFullPayment</c>) costs in a
        /// ledger whose parent closed at <paramref name="parentCloseTime"/>: the outstanding
        /// principal and management fee, interest accrued since the last payment on the theoretical
        /// principal, the prepayment penalty (<c>CloseInterestRate</c>) and <c>ClosePaymentFee</c>,
        /// computed the way rippled's <c>computeFullPayment</c> computes it.
        /// </summary>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="asset">The loan asset: the asset of the broker's vault.</param>
        /// <param name="managementFeeRate">The broker's <c>ManagementFeeRate</c>, in 1/10th of a basis point.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the payment lands in. A local time is
        /// converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="options">The amendments in force; the current rules when null.</param>
        /// <returns>
        /// null when a full payment is not accepted: one payment or none remains (the node answers
        /// <c>tecKILLED</c>), or the next payment is already late (<c>tecEXPIRED</c>).
        /// </returns>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanPaymentDue FullPaymentDue(
            LOLoan loan,
            IssuedCurrency asset,
            ushort managementFeeRate,
            DateTime parentCloseTime,
            LedgerRules options = null)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            options ??= new LedgerRules();
            uint remaining = loan.PaymentRemaining ?? 0;
            if (remaining <= 1 || IsPaymentLate(loan, parentCloseTime, dueTimeIsLate: !options.FixCleanup3_4_0))
                return null;

            LoanTerms terms = LoanTerms.Of(loan, asset, managementFeeRate, options);
            NumberContext c = terms.Context;
            XrplNumber theoreticalPrincipal = LendingMath.PrincipalFromPeriodicPayment(terms, remaining);
            XrplNumber rawInterest = LendingMath.FullPaymentInterest(
                theoreticalPrincipal,
                terms.PeriodicRate,
                LendingMath.RippleSeconds(parentCloseTime),
                loan.PaymentInterval ?? 0,
                loan.PreviousPaymentDueDate is { } previous ? LendingMath.RippleSeconds(previous) : 0,
                loan.StartDate is { } start ? LendingMath.RippleSeconds(start) : 0,
                loan.CloseInterestRate ?? 0,
                c);
            (XrplNumber interest, XrplNumber fee) = LendingMath.RoundAndSplitInterest(rawInterest, terms, NumberRounding.Downward);

            LoanState state = LoanState.Of(loan, c);
            XrplNumber closeFee = AssetRounding.Round(loan.ClosePaymentFee ?? XrplNumber.Zero, terms.Integral, terms.Scale, c);
            LoanPaymentParts tracked = new LoanPaymentParts(
                XrplNumber.Add(XrplNumber.Add(state.Principal, state.Interest, c), state.ManagementFee, c),
                state.Principal,
                state.ManagementFee,
                isFinal: true);

            // The untracked parts can be negative: they are measured against what the loan still tracked.
            XrplNumber untrackedFee = XrplNumber.Subtract(XrplNumber.Add(closeFee, fee, c), state.ManagementFee, c);
            XrplNumber untrackedInterest = XrplNumber.Subtract(interest, state.Interest, c);
            return LoanPaymentDue.From(tracked, untrackedInterest, untrackedFee, c);
        }

        /// <summary>
        /// What a <c>LoanPay</c> without the late or full payment flag does with
        /// <paramref name="amount"/> in a ledger whose parent closed at
        /// <paramref name="parentCloseTime"/>: as many regular payments as the amount covers, up to
        /// 100, then - with <paramref name="overpayment"/> (<c>tfLoanOverpayment</c>) on a loan that
        /// allows it - the rest as an overpayment that repays principal and re-amortizes the loan.
        /// Computed the way rippled's <c>makeRegularPayment</c> computes it.
        /// </summary>
        /// <param name="loan">The <c>Loan</c> entry.</param>
        /// <param name="asset">The loan asset: the asset of the broker's vault.</param>
        /// <param name="managementFeeRate">The broker's <c>ManagementFeeRate</c>, in 1/10th of a basis point.</param>
        /// <param name="amount">The transaction's <c>Amount</c>, in the asset's own unit (drops for XRP).</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the payment lands in. A local time is
        /// converted to UTC; an unspecified one is taken as UTC.
        /// </param>
        /// <param name="overpayment">Whether the transaction carries <c>tfLoanOverpayment</c>.</param>
        /// <param name="options">The amendments in force; the current rules when null.</param>
        /// <remarks>
        /// The node takes what the payments cost, not the whole <c>Amount</c>: an amount the
        /// overpayment cannot use - one its fee and penalty interest would eat, or one that would
        /// leave the loan unable to amortize - is left with the payer, and the result then shows
        /// no overpayment. An impaired loan is taken as it stands; the node unimpairs it first.
        /// </remarks>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanAmountPayment PaymentForAmount(
            LOLoan loan,
            IssuedCurrency asset,
            ushort managementFeeRate,
            XrplNumber amount,
            DateTime parentCloseTime,
            bool overpayment = false,
            LedgerRules options = null)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            options ??= new LedgerRules();
            bool overpaymentAllowed = loan.Flags is { } flags && flags.HasFlag(LoanFlags.lsfLoanOverpayment);
            uint remaining = loan.PaymentRemaining ?? 0;

            if (amount <= XrplNumber.Zero)
                return LoanAmountPayment.Refused("temBAD_AMOUNT", "The amount is not positive.");
            if (overpayment && !overpaymentAllowed)
            {
                return LoanAmountPayment.Refused(
                    options.FixCleanup3_1_3 ? "tecNO_PERMISSION" : "temINVALID_FLAG",
                    "The loan was not created with tfLoanOverpayment.");
            }
            if (remaining == 0 || (loan.PrincipalOutstanding ?? XrplNumber.Zero).IsZero)
                return LoanAmountPayment.Refused("tecKILLED", "The loan is paid off.");
            if (IsPaymentLate(loan, parentCloseTime, dueTimeIsLate: !options.FixCleanup3_4_0))
                return LoanAmountPayment.Refused("tecEXPIRED", "The payment is late; it takes tfLoanLatePayment.");

            LoanTerms terms = LoanTerms.Of(loan, asset, managementFeeRate, options);
            NumberContext c = terms.Context;
            XrplNumber serviceFee = loan.LoanServiceFee ?? XrplNumber.Zero;
            LoanState state = LoanState.Of(loan, c);
            DateTime? previousDue = loan.PreviousPaymentDueDate;
            DateTime? nextDue = loan.NextPaymentDueDate;
            uint interval = loan.PaymentInterval ?? 0;

            XrplNumber principal = XrplNumber.Zero;
            XrplNumber interest = XrplNumber.Zero;
            XrplNumber fee = XrplNumber.Zero;
            XrplNumber totalPaid = XrplNumber.Zero;
            uint paymentsMade = 0;

            LoanPaymentParts periodic = LendingMath.RegularPayment(state, remaining, terms);
            XrplNumber totalDue = XrplNumber.Add(periodic.Value, serviceFee, c);
            while (amount >= XrplNumber.Add(totalPaid, totalDue, c) && remaining > 0 && paymentsMade < MaximumPaymentsPerTransaction)
            {
                totalPaid = XrplNumber.Add(totalPaid, totalDue, c);
                principal = XrplNumber.Add(principal, periodic.Principal, c);
                interest = XrplNumber.Add(interest, periodic.Interest(c), c);
                fee = XrplNumber.Add(fee, XrplNumber.Add(periodic.ManagementFee, serviceFee, c), c);
                state = state.After(periodic, c);
                paymentsMade++;

                previousDue = nextDue;
                if (periodic.IsFinal)
                {
                    remaining = 0;
                    nextDue = null;
                    break;
                }

                remaining--;
                nextDue = nextDue?.AddSeconds(interval);
                periodic = LendingMath.RegularPayment(state, remaining, terms);
                totalDue = XrplNumber.Add(periodic.Value, serviceFee, c);
            }

            if (paymentsMade == 0)
            {
                return LoanAmountPayment.Refused(
                    "tecINSUFFICIENT_PAYMENT",
                    $"The amount does not cover the next payment of {totalDue}.");
            }

            // The amount is truncated to the loan's scale first, so dust does not become an overpayment.
            XrplNumber roundedAmount = options.FixCleanup3_1_3
                ? AssetRounding.Round(amount, terms.Integral, terms.Scale, c.WithRounding(NumberRounding.TowardsZero))
                : amount;
            XrplNumber? periodicPayment = loan.PeriodicPayment;
            bool overpaid = false;
            if (overpayment
                && overpaymentAllowed
                && remaining > 0
                && totalPaid < roundedAmount
                && paymentsMade < MaximumPaymentsPerTransaction)
            {
                XrplNumber rest = XrplNumber.Subtract(roundedAmount, totalPaid, c);
                XrplNumber extra = state.Value < rest ? state.Value : rest;
                if (options.FixCleanup3_2_0)
                    extra = AssetRounding.Round(extra, terms.Integral, terms.Scale, c.WithRounding(NumberRounding.Downward));

                if ((!options.FixCleanup3_2_0 || extra > XrplNumber.Zero)
                    && LendingMath.Overpay(state, remaining, terms, extra, loan.OverpaymentInterestRate ?? 0, loan.OverpaymentFee ?? 0, options) is { } extraPaid)
                {
                    principal = XrplNumber.Add(principal, extraPaid.Principal, c);
                    interest = XrplNumber.Add(interest, extraPaid.Interest, c);
                    fee = XrplNumber.Add(fee, extraPaid.Fee, c);
                    state = extraPaid.After;
                    periodicPayment = extraPaid.PeriodicPayment;
                    overpaid = true;
                }
            }

            return new LoanAmountPayment
            {
                Due = new LoanPaymentDue
                {
                    Principal = LoanSchedule.ToDecimal(principal),
                    InterestToVault = LoanSchedule.ToDecimal(interest),
                    PaidToBroker = LoanSchedule.ToDecimal(fee),
                    Total = LoanSchedule.ToDecimal(XrplNumber.Add(XrplNumber.Add(principal, interest, c), fee, c)),
                },
                PaymentsMade = paymentsMade,
                IsOverpaid = overpaid,
                LoanAfter = WithPayments(loan, state, remaining, periodicPayment, previousDue, nextDue),
            };
        }

        /// <summary>XRP and MPT amounts are whole numbers of their unit; issued currencies are not.</summary>
        internal static bool IsIntegral(IssuedCurrency asset) => asset.IsXrp() || asset.MptIssuanceId != null;

        /// <summary>
        /// Rounds up to a whole unit for XRP and MPT, and to a multiple of 10^<paramref name="scale"/>
        /// for an issued currency, as rippled rounds a periodic payment. The rounding is exact - done
        /// on the value itself, before it becomes a <see cref="decimal"/> - so nothing the conversion
        /// drops can make the result smaller than the value.
        /// </summary>
        /// <returns>null when the rounded value is beyond <see cref="decimal"/>.</returns>
        internal static decimal? RoundUpToAsset(XrplNumber value, bool integralAsset, int scale)
        {
            // A step below 10^-28 is not a decimal; rounding up to 10^-28 instead stays an upper bound.
            int step = integralAsset ? 0 : Math.Max(scale, -28);
            return CeilingToPowerOfTen(value, step).TryToDecimal(out decimal rounded) ? rounded : null;
        }

        /// <summary>The smallest multiple of 10^<paramref name="exponent"/> not below a positive value.</summary>
        internal static XrplNumber CeilingToPowerOfTen(XrplNumber value, int exponent)
        {
            if (value.Sign <= 0 || value.Exponent >= exponent)
                return value;

            // A mantissa has at most 19 digits: dropping more than 19 leaves nothing but the carry.
            int dropped = (int)Math.Min((long)exponent - value.Exponent, 20);
            BigInteger quotient = BigInteger.DivRem(value.Mantissa, BigInteger.Pow(10, dropped), out BigInteger remainder);
            if (!remainder.IsZero)
                quotient += 1;

            return new XrplNumber((long)quotient, exponent);
        }

        /// <summary>rippled's <c>kLoanMaximumPaymentsPerTransaction</c>.</summary>
        private const uint MaximumPaymentsPerTransaction = 100;

        /// <summary>The loan entry with the tracked values and schedule a payment leaves.</summary>
        private static LOLoan WithPayments(
            LOLoan loan,
            LoanState state,
            uint remaining,
            XrplNumber? periodicPayment,
            DateTime? previousDue,
            DateTime? nextDue) => new LOLoan
        {
            LedgerEntryType = loan.LedgerEntryType,
            Index = loan.Index,
            LedgerIndex = loan.LedgerIndex,
            Flags = loan.Flags,
            Sponsor = loan.Sponsor,
            Borrower = loan.Borrower,
            LoanBrokerID = loan.LoanBrokerID,
            LoanSequence = loan.LoanSequence,
            InterestRate = loan.InterestRate,
            LateInterestRate = loan.LateInterestRate,
            CloseInterestRate = loan.CloseInterestRate,
            OverpaymentInterestRate = loan.OverpaymentInterestRate,
            OverpaymentFee = loan.OverpaymentFee,
            PrincipalOutstanding = state.Principal,
            TotalValueOutstanding = state.Value,
            PeriodicPayment = periodicPayment,
            ManagementFeeOutstanding = state.ManagementFee,
            LoanOriginationFee = loan.LoanOriginationFee,
            LoanServiceFee = loan.LoanServiceFee,
            LatePaymentFee = loan.LatePaymentFee,
            ClosePaymentFee = loan.ClosePaymentFee,
            StartDate = loan.StartDate,
            PaymentInterval = loan.PaymentInterval,
            GracePeriod = loan.GracePeriod,
            PreviousPaymentDueDate = previousDue,
            NextPaymentDueDate = nextDue,
            PaymentRemaining = remaining,
            LoanScale = loan.LoanScale,
            OwnerNode = loan.OwnerNode,
            LoanBrokerNode = loan.LoanBrokerNode,
        };

        /// <summary>
        /// Compares in UTC. Ledger times are UTC; a local time is converted, and an unspecified one
        /// is taken as UTC.
        /// </summary>
        private static bool HasPassed(DateTime now, DateTime boundary, bool inclusive)
        {
            DateTime nowUtc = ToUtc(now);
            DateTime boundaryUtc = ToUtc(boundary);
            return inclusive ? nowUtc >= boundaryUtc : nowUtc > boundaryUtc;
        }

        private static DateTime ToUtc(DateTime time) => time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
            _ => time,
        };
    }

    /// <summary>
    /// What a loan payment costs and where the money goes, as rippled splits it. Amounts are in the
    /// loan asset's own unit - drops for XRP - and compare directly with
    /// <see cref="LoanPaymentOutcome"/>.
    /// </summary>
    public sealed class LoanPaymentDue
    {
        /// <summary>Principal the payment repays.</summary>
        public decimal Principal { get; init; }

        /// <summary>Interest the vault receives, late or accrued interest and any penalty included.</summary>
        public decimal InterestToVault { get; init; }

        /// <summary>What the broker receives: management, service, late and close fees.</summary>
        public decimal PaidToBroker { get; init; }

        /// <summary>The amount due: the least <c>Amount</c> the node accepts, and what it takes.</summary>
        public decimal Total { get; init; }

        /// <summary>
        /// <c>ExtendedPaymentComponents</c>: the tracked parts plus the untracked interest and fees;
        /// <c>totalDue</c> is their sum.
        /// </summary>
        internal static LoanPaymentDue From(LoanPaymentParts tracked, XrplNumber untrackedInterest, XrplNumber untrackedFee, NumberContext c)
        {
            XrplNumber total = XrplNumber.Add(XrplNumber.Add(tracked.Value, untrackedInterest, c), untrackedFee, c);
            return new LoanPaymentDue
            {
                Principal = LoanSchedule.ToDecimal(tracked.Principal),
                InterestToVault = LoanSchedule.ToDecimal(XrplNumber.Add(tracked.Interest(c), untrackedInterest, c)),
                PaidToBroker = LoanSchedule.ToDecimal(XrplNumber.Add(tracked.ManagementFee, untrackedFee, c)),
                Total = LoanSchedule.ToDecimal(total),
            };
        }
    }

    /// <summary>
    /// What a regular <c>LoanPay</c> does with its <c>Amount</c>: see
    /// <see cref="LoanPayments.PaymentForAmount"/>.
    /// </summary>
    public sealed class LoanAmountPayment
    {
        /// <summary>What the transaction pays and where it goes; null when refused.</summary>
        public LoanPaymentDue Due { get; init; }

        /// <summary>Regular payments settled: the drop in <c>PaymentRemaining</c>.</summary>
        public uint PaymentsMade { get; init; }

        /// <summary>Whether an overpayment repaid principal beyond the regular payments.</summary>
        public bool IsOverpaid { get; init; }

        /// <summary>
        /// The <c>Loan</c> as the transaction leaves it: <c>PrincipalOutstanding</c>,
        /// <c>TotalValueOutstanding</c>, <c>ManagementFeeOutstanding</c>, <c>PeriodicPayment</c>
        /// (re-amortized after an overpayment), <c>PaymentRemaining</c> and the due dates. null
        /// when refused.
        /// </summary>
        public LOLoan LoanAfter { get; init; }

        /// <summary>
        /// The result the node would give instead, such as <c>tecINSUFFICIENT_PAYMENT</c> or
        /// <c>tecEXPIRED</c>; null when the payment goes through.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when the payment goes through.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the node takes the payment.</summary>
        public bool IsAccepted => Refusal == null;

        internal static LoanAmountPayment Refused(string result, string reason) =>
            new LoanAmountPayment { Refusal = result, RefusalReason = reason };
    }
}
