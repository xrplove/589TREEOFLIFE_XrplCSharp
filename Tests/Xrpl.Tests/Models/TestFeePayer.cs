using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models;

// rippled: STTx::getFeePayerID and Transactor::getFeePayer. A prepaid sponsorship is preferred over
// the sponsor's account whenever the object exists, so the metadata decides between the two.
[TestClass]
public class TestUFeePayer
{
    private const string Account = "rAccount";
    private const string Delegate = "rDelegate";
    private const string Sponsor = "rSponsor";

    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void Plain_ThePayerIsTheAccount()
    {
        Assert.AreEqual(Account, Transaction().GetFeePayer());
    }

    [TestMethod]
    public void Delegated_ThePayerIsTheDelegate()
    {
        Assert.AreEqual(Delegate, Transaction(delegateAccount: Delegate).GetFeePayer());
    }

    [TestMethod]
    public void SponsoredFee_ThePayerIsTheSponsor()
    {
        Assert.AreEqual(Sponsor, Transaction(sponsor: Sponsor, flags: SponsorCoverage.spfSponsorFee).GetFeePayer());
    }

    [TestMethod]
    public void SponsoredFeeAndReserve_ThePayerIsTheSponsor()
    {
        SponsorCoverage both = SponsorCoverage.spfSponsorFee | SponsorCoverage.spfSponsorReserve;

        Assert.AreEqual(Sponsor, Transaction(sponsor: Sponsor, flags: both).GetFeePayer());
    }

    [TestMethod]
    public void SponsoredReserveOnly_ThePayerIsTheAccount()
    {
        Assert.AreEqual(Account, Transaction(sponsor: Sponsor, flags: SponsorCoverage.spfSponsorReserve).GetFeePayer());
    }

    [TestMethod]
    public void SponsoredFeeOfADelegatedTransaction_ThePayerIsTheSponsor()
    {
        TransactionResponse tx = Transaction(delegateAccount: Delegate, sponsor: Sponsor, flags: SponsorCoverage.spfSponsorFee);

        Assert.AreEqual(Sponsor, tx.GetFeePayer());
    }

    [TestMethod]
    public void SponsorWithoutFlags_ThePayerIsTheAccount()
    {
        Assert.AreEqual(Account, Transaction(sponsor: Sponsor).GetFeePayer());
    }

    [TestMethod]
    public void FlagsWithoutSponsor_ThePayerIsTheAccount()
    {
        Assert.AreEqual(Account, Transaction(flags: SponsorCoverage.spfSponsorFee).GetFeePayer());
    }

    [TestMethod]
    public void NullTransaction_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => FeePayerExtensions.GetFeePayer(null));
        Assert.ThrowsExactly<ArgumentNullException>(() => FeePayerExtensions.GetFeePayer(null, new Meta()));
    }

    [TestMethod]
    public void PrepaidSponsorship_NoAccountPays()
    {
        Meta meta = Metadata(SponsorshipNode("ModifiedNode", Sponsor, Account, @"""FeeAmount"":""4999988"""));

        Assert.IsNull(Sponsored().GetFeePayer(meta));
    }

    [TestMethod]
    public void PrepaidSponsorshipEmptiedByTheFee_NoAccountPays()
    {
        // sfFeeAmount is optional: at zero rippled removes it, so only PreviousFields names it.
        Meta meta = Metadata(SponsorshipNode("ModifiedNode", Sponsor, Account, null));

        Assert.IsNull(Sponsored().GetFeePayer(meta));
    }

    [TestMethod]
    public void PrepaidSponsorshipDeletedByTheTransaction_NoAccountPays()
    {
        Meta meta = Metadata(SponsorshipNode("DeletedNode", Sponsor, Account, null));

        Assert.IsNull(Sponsored().GetFeePayer(meta));
    }

    [TestMethod]
    public void PrepaidSponsorshipOfADelegate_IsTheOneBetweenTheSponsorAndTheDelegate()
    {
        TransactionResponse tx = Transaction(delegateAccount: Delegate, sponsor: Sponsor, flags: SponsorCoverage.spfSponsorFee);

        Assert.IsNull(tx.GetFeePayer(Metadata(SponsorshipNode("ModifiedNode", Sponsor, Delegate, null))));
        Assert.AreEqual(Sponsor, tx.GetFeePayer(Metadata(SponsorshipNode("ModifiedNode", Sponsor, Account, null))));
    }

    [TestMethod]
    public void CoSignedSponsor_TheSponsorPays()
    {
        Meta meta = Metadata(@"{""ModifiedNode"":{""LedgerEntryType"":""AccountRoot"",""LedgerIndex"":""AB"",
            ""FinalFields"":{""Account"":""rSponsor"",""Balance"":""99""},""PreviousFields"":{""Balance"":""111""}}}");

        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(meta));
    }

    [TestMethod]
    public void SponsorshipOfAnotherPair_TheSponsorPays()
    {
        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(Metadata(SponsorshipNode("ModifiedNode", "rOther", Account, null))));
        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(Metadata(SponsorshipNode("ModifiedNode", Sponsor, "rOther", null))));
    }

    [TestMethod]
    public void SponsorshipChangedWithoutItsFeeAmount_TheSponsorPays()
    {
        Meta meta = Metadata(@"{""ModifiedNode"":{""LedgerEntryType"":""Sponsorship"",""LedgerIndex"":""AB"",
            ""FinalFields"":{""Owner"":""rSponsor"",""Sponsee"":""rAccount"",""RemainingOwnerCount"":1},
            ""PreviousFields"":{""RemainingOwnerCount"":2}}}");

        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(meta));
    }

    [TestMethod]
    public void SponsorshipCreatedByTheTransaction_TheSponsorPays()
    {
        // The fee is charged before the transaction applies: the new object cannot have paid it.
        Meta meta = Metadata(@"{""CreatedNode"":{""LedgerEntryType"":""Sponsorship"",""LedgerIndex"":""AB"",
            ""NewFields"":{""Owner"":""rSponsor"",""Sponsee"":""rAccount"",""FeeAmount"":""5""}}}");

        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(meta));
    }

    [TestMethod]
    public void NoMetadata_ReadsTheTransactionAlone()
    {
        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(null));
        Assert.AreEqual(Sponsor, Sponsored().GetFeePayer(new Meta()));
    }

    [TestMethod]
    public void NotSponsored_ASponsorshipInTheMetadataChangesNothing()
    {
        Meta meta = Metadata(SponsorshipNode("ModifiedNode", Sponsor, Account, null));

        Assert.AreEqual(Account, Transaction().GetFeePayer(meta));
    }

    [TestMethod]
    public void LedgerEntry_ReadsTheSponsorOfItsReserve()
    {
        BaseLedgerEntry entry = JsonSerializer.Deserialize<BaseLedgerEntry>(
            @"{""LedgerEntryType"":""Ticket"",""Account"":""rAccount"",""Sponsor"":""rSponsor"",""index"":""AB""}", Options);

        Assert.IsInstanceOfType(entry, typeof(LOTicket));
        Assert.AreEqual(Sponsor, entry.Sponsor);
        Assert.IsTrue(entry.UnknownFields is null || !entry.UnknownFields.ContainsKey("Sponsor"));
    }

    [TestMethod]
    public void LedgerEntry_OfATypeTheSdkDoesNotKnow_ReadsTheSponsor()
    {
        BaseLedgerEntry entry = JsonSerializer.Deserialize<BaseLedgerEntry>(
            @"{""LedgerEntryType"":""FutureObject"",""Sponsor"":""rSponsor"",""index"":""AB""}", Options);

        Assert.AreEqual(Sponsor, entry.Sponsor);
    }

    [TestMethod]
    public void LedgerEntry_WritesTheSponsorBack()
    {
        LOTicket entry = new LOTicket { Sponsor = Sponsor };

        JsonNode written = JsonNode.Parse(JsonSerializer.Serialize<BaseLedgerEntry>(entry, Options));

        Assert.AreEqual(Sponsor, written["Sponsor"].GetValue<string>());
    }

    [TestMethod]
    public void LedgerEntry_WithoutASponsor_HasNone()
    {
        LOTicket entry = new LOTicket();

        JsonNode written = JsonNode.Parse(JsonSerializer.Serialize<BaseLedgerEntry>(entry, Options));

        Assert.IsNull(entry.Sponsor);
        Assert.IsNull(written["Sponsor"]);
    }

    [TestMethod]
    public void MetadataNode_ReadsAChangedSponsorFromPreviousFields()
    {
        ModifiedNode node = JsonSerializer.Deserialize<ModifiedNode>(
            @"{""LedgerEntryType"":""Ticket"",""LedgerIndex"":""AB"",
               ""FinalFields"":{""Account"":""rAccount"",""Sponsor"":""rNew""},""PreviousFields"":{""Sponsor"":""rOld""}}", Options);

        Assert.AreEqual("rNew", node.FinalFields.Sponsor);
        Assert.AreEqual("rOld", node.PreviousFields.Sponsor);
    }

    private static TransactionResponse Sponsored() => Transaction(sponsor: Sponsor, flags: SponsorCoverage.spfSponsorFee);

    private static TransactionResponse Transaction(string delegateAccount = null, string sponsor = null, SponsorCoverage? flags = null)
        => new TransactionResponse { Account = Account, Delegate = delegateAccount, Sponsor = sponsor, SponsorFlags = flags };

    private static string SponsorshipNode(string kind, string owner, string sponsee, string feeAmountLeft)
    {
        string left = feeAmountLeft == null ? string.Empty : "," + feeAmountLeft;

        return $@"{{""{kind}"":{{""LedgerEntryType"":""Sponsorship"",""LedgerIndex"":""AB"",
            ""FinalFields"":{{""Owner"":""{owner}"",""Sponsee"":""{sponsee}""{left}}},
            ""PreviousFields"":{{""FeeAmount"":""5000000""}}}}}}";
    }

    private static Meta Metadata(string node)
        => JsonSerializer.Deserialize<Meta>($@"{{""AffectedNodes"":[{node}],""TransactionIndex"":0,""TransactionResult"":""tesSUCCESS""}}", Options);
}
