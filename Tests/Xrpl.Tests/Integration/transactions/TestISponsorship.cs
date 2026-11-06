using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("Sponsorship")]
public class TestISponsorship
{
    private static bool sponsorAmendmentActive;

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
        sponsorAmendmentActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.Sponsor);
    }

    [TestInitialize]
    public void CheckSponsorAmendment()
    {
        if (!sponsorAmendmentActive)
        {
            Assert.Inconclusive("Sponsor amendment (XLS-68) is not enabled on the test node; bump the nightly in .ci-config/Dockerfile.nightly and uncomment Sponsor in rippled.batchv11.cfg to run these tests.");
        }
        
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
    }

    private static async Task<LOSponsorship> GetSponsorshipObject(string ownerAddress)
    {
        AccountObjectsRequest request = new AccountObjectsRequest(ownerAddress)
        {
            Type = LedgerEntryType.Sponsorship,
        };
        AccountObjects response = await client.AccountObjects(request).Typed();

        return response?.AccountObjectList?
            .OfType<LOSponsorship>()
            .FirstOrDefault();
    }

    [TestMethod]
    public async Task TestSponsorshipSet_BySponsor_CreatesLedgerObject()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);

        SponsorshipSet tx = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 5m },
            RemainingOwnerCountDelta = 3,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, sponsor, true);
        ValidateResult(result);

        LOSponsorship sponsorship = await GetSponsorshipObject(sponsor.ClassicAddress);
        Assert.IsNotNull(sponsorship, "Sponsorship ledger object should exist after SponsorshipSet");
        Assert.AreEqual(sponsor.ClassicAddress, sponsorship.Owner);
        Assert.AreEqual(sponsee.ClassicAddress, sponsorship.Sponsee);
        Assert.AreEqual((uint)3, sponsorship.RemainingOwnerCount);
    }

    [TestMethod]
    public async Task TestSponsorshipSet_NegativeDeltas_ReduceTheBudget()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);

        SponsorshipSet create = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 5m },
            RemainingOwnerCountDelta = 3,
        };
        create = await client.Autofill(create);
        ValidateResult(await client.SubmitAndWait(create, sponsor, true));

        // Since 3.3.0 the transaction carries signed deltas rather than absolute values:
        // rippled adds them to what the Sponsorship object already holds, moving the XRP
        // back to the sponsor balance for a negative FeeAmountDelta
        SponsorshipSet reduce = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = -2m },
            RemainingOwnerCountDelta = -1,
        };
        reduce = await client.Autofill(reduce);
        ValidateResult(await client.SubmitAndWait(reduce, sponsor, true));

        LOSponsorship sponsorship = await GetSponsorshipObject(sponsor.ClassicAddress);
        Assert.IsNotNull(sponsorship, "Sponsorship ledger object should still exist after the reduction");
        Assert.AreEqual(3m, sponsorship.FeeAmount.ValueAsXrp, "FeeAmount should be 5 XRP + (-2 XRP)");
        Assert.AreEqual((uint)2, sponsorship.RemainingOwnerCount, "RemainingOwnerCount should be 3 + (-1)");
    }

    [TestMethod]
    public async Task TestSponsoredPayment_SponsorPaysFee()
    {
        // This one carries a SponsorSignature; the SponsorshipSet tests in this class do not
        await AmendmentGuard.RequireRoleSignaturesAsync(client);

        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        XrplWallet destination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee, destination);

        // Establish sponsorship
        SponsorshipSet setup = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 5m },
        };
        setup = await client.Autofill(setup);
        ValidateResult(await client.SubmitAndWait(setup, sponsor, true));

        // Sponsee sends a payment whose fee is covered by the sponsor;
        // the sponsor co-signs via SponsorSignature.
        Payment payment = new Payment
        {
            Account = sponsee.ClassicAddress,
            Destination = destination.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1m },
            Sponsor = sponsor.ClassicAddress,
            SponsorFlags = SponsorCoverage.spfSponsorFee,
        };
        payment = await client.Autofill(payment);

        System.Text.Json.Nodes.JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponsee);
        SignatureResult signed = SponsorSigningHelper.SignSponsored(prepared, sponsee, sponsor);

        Submit response = await client.SubmitRequest(signed.TxBlob, true);
        if (response is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"Sponsored payment failed: {response.EngineResult}");
    }

    private async Task<(XrplWallet sponsor, XrplWallet sponsee, XrplWallet destination)> SetupSponsorshipAsync(
        SponsorshipSetFlags? flags = null)
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        XrplWallet destination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee, destination);

        SponsorshipSet setup = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 5m },
            Flags = flags,
        };
        setup = await client.Autofill(setup);
        ValidateResult(await client.SubmitAndWait(setup, sponsor, true));
        return (sponsor, sponsee, destination);
    }

    private static Payment SponsoredPayment(XrplWallet sponsee, XrplWallet destination, XrplWallet sponsor) => new Payment
    {
        Account = sponsee.ClassicAddress,
        Destination = destination.ClassicAddress,
        Amount = new Currency { ValueAsXrp = 1m },
        Sponsor = sponsor.ClassicAddress,
        SponsorFlags = SponsorCoverage.spfSponsorFee,
    };

    /// <summary>
    /// Unified API (#43): no helpers, no flow choice — each side calls the
    /// standard Sign, the sponsee submits the standard way.
    /// </summary>
    [TestMethod]
    public async Task Unified_StandardSignBothSides_V3()
    {
        // This one carries a SponsorSignature; the SponsorshipSet tests in this class do not
        await AmendmentGuard.RequireRoleSignaturesAsync(client);

        var (sponsor, sponsee, destination) = await SetupSponsorshipAsync();

        Payment payment = await client.Autofill(SponsoredPayment(sponsee, destination, sponsor));
        System.Text.Json.Nodes.JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponsee);
        var preparedDict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
            prepared.ToJsonString(), global::Xrpl.Client.Json.XrplJsonOptions.Default);

        // Sponsor side: standard Sign routes to the sponsor co-signature
        SignatureResult sponsorPart = sponsor.Sign(preparedDict);

        // Sponsee side: standard Sign preserves the SponsorSignature
        var handedOver = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
            global::Xrpl.BinaryCodec.XrplBinaryCodec.Decode(sponsorPart.TxBlob).ToJsonString(),
            global::Xrpl.Client.Json.XrplJsonOptions.Default);
        SignatureResult final = sponsee.Sign(handedOver);

        Submit response = await client.SubmitRequest(final.TxBlob, true);
        if (response is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"Unified V3 sponsored payment failed: {response.EngineResult}");
    }

    /// <summary>
    /// Unified API (#43): smart Submit finalizes as the sponsor — the sponsee's
    /// signature is already present, the sponsor wallet composes and submits.
    /// </summary>
    [TestMethod]
    public async Task Unified_SmartSubmit_SponsorFinalizes()
    {
        // This one carries a SponsorSignature; the SponsorshipSet tests in this class do not
        await AmendmentGuard.RequireRoleSignaturesAsync(client);

        var (sponsor, sponsee, destination) = await SetupSponsorshipAsync();

        Payment payment = await client.Autofill(SponsoredPayment(sponsee, destination, sponsor));
        System.Text.Json.Nodes.JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponsee);
        var preparedDict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
            prepared.ToJsonString(), global::Xrpl.Client.Json.XrplJsonOptions.Default);

        // Sponsee signs with the standard Sign, hands the partially signed tx to the sponsor
        SignatureResult sponseePart = sponsee.Sign(preparedDict);
        var handedOver = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
            global::Xrpl.BinaryCodec.XrplBinaryCodec.Decode(sponseePart.TxBlob).ToJsonString(),
            global::Xrpl.Client.Json.XrplJsonOptions.Default);

        // The sponsor submits via the standard SubmitAndWait — composition happens inside
        TransactionSummary result = await client.SubmitAndWait(handedOver, sponsor, autofill: false);
        ValidateResult(result);
    }

    /// <summary>
    /// Unified API (#43): the one-call V1 flow with both wallets local.
    /// </summary>
    [TestMethod]
    public async Task Unified_SubmitAndWaitSponsored_OneCall()
    {
        // This one carries a SponsorSignature; the SponsorshipSet tests in this class do not
        await AmendmentGuard.RequireRoleSignaturesAsync(client);

        var (sponsor, sponsee, destination) = await SetupSponsorshipAsync();

        Payment payment = SponsoredPayment(sponsee, destination, sponsor);
        TransactionSummary result = await client.SubmitAndWaitSponsored(payment, sponsee, sponsor);
        ValidateResult(result);
    }

    /// <summary>
    /// A sponsorship with a fee budget pays from the object: the sponsor's balance does not move.
    /// </summary>
    [TestMethod]
    public async Task FeePayer_PrepaidSponsorship_NoAccountPays()
    {
        var (sponsor, sponsee, destination) = await SetupSponsorshipAsync();

        Payment payment = await client.Autofill(SponsoredPayment(sponsee, destination, sponsor));
        TransactionSummary result = await client.SubmitAndWait(payment, sponsee, true);
        ValidateResult(result);

        Assert.AreEqual(sponsor.ClassicAddress, result.Transaction.GetFeePayer());
        Assert.IsNull(result.Transaction.GetFeePayer(result.Meta));
    }

    /// <summary>
    /// With no Sponsorship object between the two, the co-signing sponsor pays from its account.
    /// </summary>
    [TestMethod]
    public async Task FeePayer_CoSignedWithoutASponsorship_TheSponsorPays()
    {
        // This one carries a SponsorSignature; the SponsorshipSet tests in this class do not
        await AmendmentGuard.RequireRoleSignaturesAsync(client);

        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        XrplWallet destination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee, destination);

        TransactionSummary result = await client.SubmitAndWaitSponsored(SponsoredPayment(sponsee, destination, sponsor), sponsee, sponsor);
        ValidateResult(result);

        Assert.AreEqual(sponsor.ClassicAddress, result.Transaction.GetFeePayer(result.Meta));
    }

    /// <summary>
    /// Unified API (#43): the ledger pre-check fails fast when the sponsorship
    /// requires the sponsor's co-signature and it is missing.
    /// </summary>
    [TestMethod]
    public async Task Unified_SmartSubmit_RequireSign_FailsFastWithoutSponsorSignature()
    {
        // This one carries a SponsorSignature; the SponsorshipSet tests in this class do not
        await AmendmentGuard.RequireRoleSignaturesAsync(client);

        var (sponsor, sponsee, destination) = await SetupSponsorshipAsync(
            SponsorshipSetFlags.tfSponsorshipSetRequireSignForFee);

        Payment payment = await client.Autofill(SponsoredPayment(sponsee, destination, sponsor));
        var txDict = payment.ToDictionary();

        ValidationException ex = await Assert.ThrowsExactlyAsync<ValidationException>(
            () => client.SubmitAndWait(txDict, sponsee, autofill: false));
        StringAssert.Contains(ex.Message, "not signed by all participants");

        // With the sponsor's co-signature the same transaction goes through
        TransactionSummary result = await client.SubmitAndWaitSponsored(
            SponsoredPayment(sponsee, destination, sponsor), sponsee, sponsor);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestSponsorshipSet_DeleteObject()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);

        SponsorshipSet create = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 2m },
        };
        create = await client.Autofill(create);
        ValidateResult(await client.SubmitAndWait(create, sponsor, true));
        Assert.IsNotNull(await GetSponsorshipObject(sponsor.ClassicAddress));

        SponsorshipSet delete = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            Flags = SponsorshipSetFlags.tfDeleteObject,
        };
        delete = await client.Autofill(delete);
        ValidateResult(await client.SubmitAndWait(delete, sponsor, true));

        Assert.IsNull(await GetSponsorshipObject(sponsor.ClassicAddress),
            "Sponsorship ledger object should be removed after tfDeleteObject");
    }
}
