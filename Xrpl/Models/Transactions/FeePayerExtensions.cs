using System;

using Xrpl.Models.Ledger;

namespace Xrpl.Models.Transactions;

/// <summary>
/// Who the ledger charges a transaction's network fee to.
/// </summary>
/// <remarks>
/// Not always <see cref="ITransactionCommon.Account"/>: <see cref="ITransactionCommon.Delegate"/>
/// pays for a transaction it submits, and a sponsor pays when
/// <see cref="ITransactionCommon.SponsorFlags"/> has <see cref="SponsorCoverage.spfSponsorFee"/>
/// (requires the Sponsor amendment, XLS-68). With a <c>Fee</c> of zero nothing is charged: the
/// result is then who would have paid. That is the case of a Batch inner transaction, whose fee is
/// paid by the outer <c>Batch</c>.
/// </remarks>
public static class FeePayerExtensions
{
    /// <summary>
    /// The account the fee of <paramref name="tx"/> is charged to, read from the transaction alone:
    /// the sponsor when it covers the fee, otherwise the delegate, otherwise the account
    /// (rippled <c>STTx::getFeePayerID</c>).
    /// </summary>
    /// <remarks>
    /// A prepaid sponsorship pays from its <see cref="LOSponsorship"/> object, not from the sponsor's
    /// balance, and the transaction cannot say which of the two it was. Where the metadata is at hand
    /// use <see cref="GetFeePayer(ITransactionCommon, ITransactionMetadata)"/>.
    /// </remarks>
    /// <param name="tx">The transaction.</param>
    /// <returns>The classic address of the payer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tx"/> is <see langword="null"/>.</exception>
    public static string GetFeePayer(this ITransactionCommon tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        return CoversFee(tx) ? tx.Sponsor : Initiator(tx);
    }

    /// <summary>
    /// The account whose balance the fee of <paramref name="tx"/> came out of, or
    /// <see langword="null"/> when it came out of a prepaid <see cref="LOSponsorship"/> object and no
    /// account's balance carries it (rippled <c>Transactor::getFeePayer</c>,
    /// <c>FeePayerType::SponsorPreFunded</c>).
    /// </summary>
    /// <remarks>
    /// The object is told by its node in the metadata: the sponsorship between the sponsor and the
    /// transaction's initiator, changed or deleted with <c>FeeAmount</c> in its previous fields. A
    /// sponsorship this transaction created does not count, because the fee is charged before the
    /// transaction applies. Without such a node the fee came out of the sponsor's own account.
    /// </remarks>
    /// <param name="tx">The transaction.</param>
    /// <param name="meta">Its metadata; without it the result is <see cref="GetFeePayer(ITransactionCommon)"/>.</param>
    /// <returns>The classic address of the payer, or <see langword="null"/> for a prepaid sponsorship.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tx"/> is <see langword="null"/>.</exception>
    public static string GetFeePayer(this ITransactionCommon tx, ITransactionMetadata meta)
    {
        string payer = tx.GetFeePayer();

        return CoversFee(tx) && DebitsSponsorship(meta, tx.Sponsor, Initiator(tx)) ? null : payer;
    }

    private static bool CoversFee(ITransactionCommon tx)
        => !string.IsNullOrEmpty(tx.Sponsor)
           && tx.SponsorFlags is { } flags
           && (flags & SponsorCoverage.spfSponsorFee) != 0;

    private static string Initiator(ITransactionCommon tx)
        => string.IsNullOrEmpty(tx.Delegate) ? tx.Account : tx.Delegate;

    private static bool DebitsSponsorship(ITransactionMetadata meta, string sponsor, string sponsee)
    {
        if (meta?.AffectedNodes is not { } nodes)
        {
            return false;
        }

        foreach (AffectedNode node in nodes)
        {
            (BaseLedgerEntry final, BaseLedgerEntry previous) = node switch
            {
                { ModifiedNode: { LedgerEntryType: LedgerEntryType.Sponsorship } modified } => (modified.FinalFields, modified.PreviousFields),
                { DeletedNode: { LedgerEntryType: LedgerEntryType.Sponsorship } deleted } => (deleted.FinalFields, deleted.PreviousFields),
                _ => (null, null),
            };

            // At zero rippled removes the optional FeeAmount, so PreviousFields is the only place
            // that always shows the debit.
            if (previous is LOSponsorship { FeeAmount: not null }
                && final is LOSponsorship sponsorship
                && string.Equals(sponsorship.Owner, sponsor, StringComparison.Ordinal)
                && string.Equals(sponsorship.Sponsee, sponsee, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
