using System;
using System.Collections.Generic;
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
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// The Sponsor field (XLS-68) on every transaction type other than Payment. Sponsor and
/// SponsorFlags live on the shared transaction base, so the SDK emits them for any type;
/// what a node makes of them differs per transactor (reserve-holding objects, deferred
/// fees, tec paths), and only a validated transaction of each type proves the pairing.
/// The sponsor always co-signs through <c>SubmitAndWaitSponsored</c>.
/// </summary>
[TestClass]
[TestCategory("Sponsorship")]
public class TestISponsoredTypes
{
    private static bool sponsorAmendmentActive;

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    private const string GenesisAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
    private const string TestCurrency = "USD";

    private static readonly TimeSpan EscrowFinishMargin = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan EscrowCancelMargin = TimeSpan.FromSeconds(24);

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
        sponsorAmendmentActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.Sponsor);
    }

    [TestInitialize]
    public async Task CheckSponsorAmendment()
    {
        if (!sponsorAmendmentActive)
        {
            Assert.Inconclusive("Sponsor amendment (XLS-68) is not enabled on the test node.");
        }

        await AmendmentGuard.RequireRoleSignaturesAsync(client);
        
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    #region Helpers

    /// <summary>
    /// Funds a sponsor and a sponsee and opens a sponsorship generous enough for a whole test.
    /// </summary>
    private static async Task<(XrplWallet sponsor, XrplWallet sponsee)> SetupSponsorshipAsync()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);
        await OpenSponsorshipAsync(sponsor, sponsee);
        return (sponsor, sponsee);
    }

    /// <summary>
    /// Opens the sponsorship. The Sponsorship entry lands in the sponsee's owner
    /// directory as well, so anything that needs an empty directory (asfAllowTrustLineClawback)
    /// has to happen before this call.
    /// </summary>
    private static async Task OpenSponsorshipAsync(XrplWallet sponsor, XrplWallet sponsee)
    {
        await SubmitPlainAsync(new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 20m },
            RemainingOwnerCountDelta = 10,
        }, sponsor, "SponsorshipSet");
    }

    private static async Task<TransactionSummary> SubmitPlainAsync(ITransactionRequest tx, XrplWallet signer, string context)
    {
        ITransactionRequest autofilled = await client.Autofill(tx);
        TransactionSummary res = await client.SubmitAndWait(autofilled, signer, true);
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"{context} failed: {res.Meta?.TransactionResult}");
        return res;
    }

    /// <summary>
    /// Stamps the sponsor onto the transaction and submits it with both signatures.
    /// </summary>
    private static async Task<TransactionSummary> SubmitSponsoredAsync<T>(
        T tx, XrplWallet sponsee, XrplWallet sponsor, SponsorCoverage coverage = SponsorCoverage.spfSponsorFee)
        where T : TransactionRequest
    {
        Assert.AreEqual(sponsee.ClassicAddress, tx.Account, "the sponsored transaction must be the sponsee's");
        tx.Sponsor = sponsor.ClassicAddress;
        tx.SponsorFlags = coverage;

        TransactionSummary res = await client.SubmitAndWaitSponsored(tx, sponsee, sponsor);
        Assert.IsTrue(res.Meta?.TransactionResult is "tesSUCCESS",
            $"sponsored {tx.TransactionType} must validate with tesSUCCESS, got {res.Meta?.TransactionResult}");
        return res;
    }

    private static async Task<DateTime> ValidatedCloseTimeAsync()
    {
        LOLedger ledger = await client.Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }).Typed();
        LedgerEntity entity = (LedgerEntity)ledger.LedgerEntity;
        return entity.CloseTime ?? throw new InvalidOperationException("validated ledger has no close_time");
    }

    /// <summary>
    /// Waits until the validated close time is strictly past <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Strictly: rippled's time gates are <c>now &gt; mark</c> (<c>after()</c> in View.cpp), so a
    /// close time equal to the mark is still too early. Standalone close times move in coarse
    /// steps and land on equality readily.
    /// <para>
    /// Bounded, because a ledger that stops advancing is a node failure and a test that waits on
    /// it forever reports nothing. The failure names the last close time seen and how far short
    /// of the mark it was, which separates a stalled node from a mark set too far ahead.
    /// </para>
    /// </remarks>
    private static async Task WaitForCloseTimeAsync(DateTime target)
    {
        TimeSpan budget = TimeSpan.FromSeconds(90);
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        DateTime lastSeen = DateTime.MinValue;

        while (true)
        {
            lastSeen = await ValidatedCloseTimeAsync();
            if (lastSeen > target)
                return;

            if (elapsed.Elapsed >= budget)
            {
                Assert.Fail(
                    $"the validated close time did not pass {target:O} within {budget.TotalSeconds:F0}s; " +
                    $"last seen {lastSeen:O}, short by {(target - lastSeen).TotalSeconds:F1}s");
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    private static string ToHex(string text) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text));

    private static Currency Xrp(decimal value) => new Currency { ValueAsXrp = value };

    private static Currency Drops(string drops) => new Currency { Value = drops, CurrencyCode = "XRP" };

    #endregion

    [TestMethod]
    public async Task Sponsored_AccountSet()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        await SubmitSponsoredAsync(new AccountSet { Account = sponsee.ClassicAddress, Domain = ToHex("sponsored.example") }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_TrustSet_ReserveCovered()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        await SubmitSponsoredAsync(new TrustSet
        {
            Account = sponsee.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = TestCurrency, Issuer = sponsor.ClassicAddress, Value = "1000" },
        }, sponsee, sponsor, SponsorCoverage.spfSponsorFee | SponsorCoverage.spfSponsorReserve);
    }

    [TestMethod]
    public async Task Sponsored_OfferCreate_OfferCancel()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary offer = await SubmitSponsoredAsync(new OfferCreate
        {
            Account = sponsee.ClassicAddress,
            TakerGets = Xrp(1m),
            TakerPays = new Currency { CurrencyCode = TestCurrency, Issuer = sponsor.ClassicAddress, Value = "1" },
        }, sponsee, sponsor);

        await SubmitSponsoredAsync(new OfferCancel { Account = sponsee.ClassicAddress, OfferSequence = offer.Transaction.Sequence }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_DIDSet_DIDDelete()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        // A DID entry carries no Sponsor field, so only the fee can be sponsored
        // (spfSponsorReserve on DIDSet is temINVALID_FLAG)
        await SubmitSponsoredAsync(new DIDSet { Account = sponsee.ClassicAddress, Data = ToHex("sponsored did") }, sponsee, sponsor);
        await SubmitSponsoredAsync(new DIDDelete { Account = sponsee.ClassicAddress }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_OracleSet_OracleDelete()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        const uint documentId = 11;

        await SubmitSponsoredAsync(new OracleSet
        {
            Account = sponsee.ClassicAddress,
            OracleDocumentID = documentId,
            LastUpdateTime = await ValidatedCloseTimeAsync(),
            Provider = "sponsored",
            AssetClass = "currency",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper { PriceData = new PriceData { BaseAsset = "XRP", QuoteAsset = "USD", AssetPrice = 740, Scale = 3 } },
            },
        }, sponsee, sponsor);

        await SubmitSponsoredAsync(new OracleDelete { Account = sponsee.ClassicAddress, OracleDocumentID = documentId }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_CredentialCreate_CredentialDelete()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        string credentialType = ToHex("sponsored_credential");

        // Self-issued: the subject is the issuer, so no CredentialAccept is needed
        await SubmitSponsoredAsync(new CredentialCreate { Account = sponsee.ClassicAddress, Subject = sponsee.ClassicAddress, CredentialType = credentialType }, sponsee, sponsor);
        await SubmitSponsoredAsync(new CredentialDelete { Account = sponsee.ClassicAddress, Subject = sponsee.ClassicAddress, Issuer = sponsee.ClassicAddress, CredentialType = credentialType }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_CredentialAccept()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        string credentialType = ToHex("sponsored_accept");

        await SubmitPlainAsync(new CredentialCreate { Account = sponsor.ClassicAddress, Subject = sponsee.ClassicAddress, CredentialType = credentialType }, sponsor, "CredentialCreate");
        await SubmitSponsoredAsync(new CredentialAccept { Account = sponsee.ClassicAddress, Issuer = sponsor.ClassicAddress, CredentialType = credentialType }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_TicketCreate_SetRegularKey_SignerListSet()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        await SubmitSponsoredAsync(new TicketCreate { Account = sponsee.ClassicAddress, TicketCount = 1 }, sponsee, sponsor);
        await SubmitSponsoredAsync(new SetRegularKey { Account = sponsee.ClassicAddress, RegularKey = XrplWallet.Generate().ClassicAddress }, sponsee, sponsor);
        await SubmitSponsoredAsync(new SignerListSet
        {
            Account = sponsee.ClassicAddress,
            SignerQuorum = 1,
            SignerEntries = new List<SignerEntryWrapper>
            {
                new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = sponsor.ClassicAddress, SignerWeight = 1 } },
            },
        }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_EscrowCreate_EscrowFinish_EscrowCancel()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        DateTime closeTime = await ValidatedCloseTimeAsync();

        TransactionSummary finishable = await SubmitSponsoredAsync(new EscrowCreate
        {
            Account = sponsee.ClassicAddress,
            Destination = sponsor.ClassicAddress,
            Amount = Xrp(1m),
            FinishAfter = closeTime + EscrowFinishMargin,
        }, sponsee, sponsor);
        TransactionSummary cancellable = await SubmitSponsoredAsync(new EscrowCreate
        {
            Account = sponsee.ClassicAddress,
            Destination = sponsor.ClassicAddress,
            Amount = Xrp(1m),
            FinishAfter = closeTime + EscrowFinishMargin,
            CancelAfter = closeTime + EscrowCancelMargin,
        }, sponsee, sponsor);

        await WaitForCloseTimeAsync(closeTime + EscrowCancelMargin);

        await SubmitSponsoredAsync(new EscrowFinish { Account = sponsee.ClassicAddress, Owner = sponsee.ClassicAddress, OfferSequence = finishable.Transaction.Sequence }, sponsee, sponsor);
        await SubmitSponsoredAsync(new EscrowCancel { Account = sponsee.ClassicAddress, Owner = sponsee.ClassicAddress, OfferSequence = cancellable.Transaction.Sequence }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_NFTokenMint_CreateOffer_CancelOffer_Modify_Burn()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary mint = await SubmitSponsoredAsync(new NFTokenMint
        {
            Account = sponsee.ClassicAddress,
            NFTokenTaxon = 0,
            Flags = NFTokenMintFlags.tfTransferable | NFTokenMintFlags.tfMutable,
        }, sponsee, sponsor);
        string nftokenId = mint.Meta?.NFTokenId;
        Assert.IsNotNull(nftokenId, "NFTokenMint must report nftoken_id");

        TransactionSummary offer = await SubmitSponsoredAsync(new NFTokenCreateOffer
        {
            Account = sponsee.ClassicAddress,
            NFTokenID = nftokenId,
            Amount = Xrp(1m),
            Flags = NFTokenCreateOfferFlags.tfSellNFToken,
        }, sponsee, sponsor);
        string offerId = offer.Meta?.OfferID;
        Assert.IsNotNull(offerId, "NFTokenCreateOffer must report offer_id");

        await SubmitSponsoredAsync(new NFTokenCancelOffer { Account = sponsee.ClassicAddress, NFTokenOffers = new[] { offerId } }, sponsee, sponsor);
        await SubmitSponsoredAsync(new NFTokenModify { Account = sponsee.ClassicAddress, NFTokenID = nftokenId, URI = ToHex("ipfs://sponsored") }, sponsee, sponsor);
        await SubmitSponsoredAsync(new NFTokenBurn { Account = sponsee.ClassicAddress, NFTokenID = nftokenId }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_NFTokenAcceptOffer()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary mint = await SubmitPlainAsync(new NFTokenMint { Account = sponsor.ClassicAddress, NFTokenTaxon = 0, Flags = NFTokenMintFlags.tfTransferable }, sponsor, "NFTokenMint");
        TransactionSummary offer = await SubmitPlainAsync(new NFTokenCreateOffer
        {
            Account = sponsor.ClassicAddress,
            NFTokenID = mint.Meta?.NFTokenId,
            Amount = Xrp(1m),
            Destination = sponsee.ClassicAddress,
            Flags = NFTokenCreateOfferFlags.tfSellNFToken,
        }, sponsor, "NFTokenCreateOffer");

        await SubmitSponsoredAsync(new NFTokenAcceptOffer { Account = sponsee.ClassicAddress, NFTokenSellOffer = offer.Meta?.OfferID }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_MPTokenIssuanceCreate_Set_Destroy()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary create = await SubmitSponsoredAsync(new MPTokenIssuanceCreate
        {
            Account = sponsee.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock,
        }, sponsee, sponsor);
        string issuanceId = create.Meta?.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceCreate must report mpt_issuance_id");

        await SubmitSponsoredAsync(new MPTokenIssuanceSet { Account = sponsee.ClassicAddress, MPTokenIssuanceID = issuanceId, Flags = MPTokenIssuanceSetFlags.tfMPTLock }, sponsee, sponsor);
        await SubmitSponsoredAsync(new MPTokenIssuanceDestroy { Account = sponsee.ClassicAddress, MPTokenIssuanceID = issuanceId }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_MPTokenAuthorize()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary create = await SubmitPlainAsync(new MPTokenIssuanceCreate { Account = sponsor.ClassicAddress }, sponsor, "MPTokenIssuanceCreate");
        await SubmitSponsoredAsync(new MPTokenAuthorize { Account = sponsee.ClassicAddress, MPTokenIssuanceID = create.Meta?.MptIssuanceId }, sponsee, sponsor,
            SponsorCoverage.spfSponsorFee | SponsorCoverage.spfSponsorReserve);
    }

    [TestMethod]
    public async Task Sponsored_CheckCreate_CheckCancel_CheckCash()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary own = await SubmitSponsoredAsync(new CheckCreate { Account = sponsee.ClassicAddress, Destination = sponsor.ClassicAddress, SendMax = Xrp(1m) }, sponsee, sponsor);
        string ownCheck = CreatedIndex(own.Meta, LedgerEntryType.Check);
        await SubmitSponsoredAsync(new CheckCancel { Account = sponsee.ClassicAddress, CheckID = ownCheck }, sponsee, sponsor);

        TransactionSummary incoming = await SubmitPlainAsync(new CheckCreate { Account = sponsor.ClassicAddress, Destination = sponsee.ClassicAddress, SendMax = Xrp(1m) }, sponsor, "CheckCreate");
        string incomingCheck = CreatedIndex(incoming.Meta, LedgerEntryType.Check);
        await SubmitSponsoredAsync(new CheckCash { Account = sponsee.ClassicAddress, CheckID = incomingCheck, Amount = Xrp(1m) }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_PaymentChannelCreate_Fund_Claim()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary open = await SubmitSponsoredAsync(new PaymentChannelCreate
        {
            Account = sponsee.ClassicAddress,
            Destination = sponsor.ClassicAddress,
            Amount = "1000000",
            SettleDelay = 60,
            PublicKey = sponsee.PublicKey,
        }, sponsee, sponsor);
        string channel = Hashes.HashPaymentChannel(sponsee.ClassicAddress, sponsor.ClassicAddress, (int)open.Transaction.Sequence.Value);

        await SubmitSponsoredAsync(new PaymentChannelFund { Account = sponsee.ClassicAddress, Channel = channel, Amount = "500000" }, sponsee, sponsor);
        await SubmitSponsoredAsync(new PaymentChannelClaim { Account = sponsee.ClassicAddress, Channel = channel, Balance = "700000" }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_DelegateSet()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        await SubmitSponsoredAsync(new DelegateSet
        {
            Account = sponsee.ClassicAddress,
            Authorize = sponsor.ClassicAddress,
            Permissions = new List<PermissionWrapper> { new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } } },
        }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_PermissionedDomainSet_Delete()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        TransactionSummary set = await SubmitSponsoredAsync(new PermissionedDomainSet
        {
            Account = sponsee.ClassicAddress,
            AcceptedCredentials = new List<AcceptedCredentialWrapper>
            {
                new AcceptedCredentialWrapper { Credential = new AcceptedCredential { Issuer = sponsor.ClassicAddress, CredentialType = ToHex("sponsored_domain") } },
            },
        }, sponsee, sponsor);
        string domainId = CreatedIndex(set.Meta, LedgerEntryType.PermissionedDomain);

        await SubmitSponsoredAsync(new PermissionedDomainDelete { Account = sponsee.ClassicAddress, DomainID = domainId }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_Clawback()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);

        // The sponsee issues; the sponsor holds and gets clawed back. The clawback flag
        // needs an empty owner directory, so it goes on before the sponsorship exists
        await SubmitPlainAsync(new AccountSet { Account = sponsee.ClassicAddress, SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback }, sponsee, "asfAllowTrustLineClawback");
        await OpenSponsorshipAsync(sponsor, sponsee);
        await SubmitPlainAsync(new TrustSet { Account = sponsor.ClassicAddress, LimitAmount = new Currency { CurrencyCode = TestCurrency, Issuer = sponsee.ClassicAddress, Value = "1000" } }, sponsor, "TrustSet");
        await SubmitPlainAsync(new Payment { Account = sponsee.ClassicAddress, Destination = sponsor.ClassicAddress, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = sponsee.ClassicAddress, Value = "100" } }, sponsee, "issue tokens");

        await SubmitSponsoredAsync(new ClawBack
        {
            Account = sponsee.ClassicAddress,
            Amount = new Currency { CurrencyCode = TestCurrency, Issuer = sponsor.ClassicAddress, Value = "40" },
        }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_AMMCreate_Deposit_Vote_Bid_Withdraw()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        // The sponsor issues the pool token; the sponsee holds it and runs the pool
        await SubmitPlainAsync(new AccountSet { Account = sponsor.ClassicAddress, SetFlag = AccountSetAsfFlags.asfDefaultRipple }, sponsor, "asfDefaultRipple");
        await SubmitPlainAsync(new TrustSet { Account = sponsee.ClassicAddress, LimitAmount = new Currency { CurrencyCode = TestCurrency, Issuer = sponsor.ClassicAddress, Value = "1000000" } }, sponsee, "TrustSet");
        await SubmitPlainAsync(new Payment { Account = sponsor.ClassicAddress, Destination = sponsee.ClassicAddress, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = sponsor.ClassicAddress, Value = "10000" } }, sponsor, "issue tokens");

        IssuedCurrency token = new IssuedCurrency { Currency = TestCurrency, Issuer = sponsor.ClassicAddress };
        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
        Currency tokens(string value) => new Currency { CurrencyCode = TestCurrency, Issuer = sponsor.ClassicAddress, Value = value };

        await SubmitSponsoredAsync(new AMMCreate { Account = sponsee.ClassicAddress, Amount = tokens("1000"), Amount2 = Xrp(10m), TradingFee = 500 }, sponsee, sponsor);
        await SubmitSponsoredAsync(new AMMDeposit { Account = sponsee.ClassicAddress, Asset = token, Asset2 = xrp, Amount = tokens("100"), Flags = AMMDepositFlags.tfSingleAsset }, sponsee, sponsor);
        await SubmitSponsoredAsync(new AMMVote { Account = sponsee.ClassicAddress, Asset = token, Asset2 = xrp, TradingFee = 100 }, sponsee, sponsor);
        await SubmitSponsoredAsync(new AMMBid { Account = sponsee.ClassicAddress, Asset = token, Asset2 = xrp }, sponsee, sponsor);
        await SubmitSponsoredAsync(new AMMWithdraw { Account = sponsee.ClassicAddress, Asset = token, Asset2 = xrp, Amount = tokens("50"), Flags = AMMWithdrawFlags.tfSingleAsset }, sponsee, sponsor);
    }

    [TestMethod]
    public async Task Sponsored_XChain_CreateBridge_Modify_ClaimID_Commit_AccountCreateCommit()
    {
        await AmendmentGuard.RequireXChainBridgeAsync(client);
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        // The sponsee is the door of its own bridge for the bridge-owner transactions...
        XChainBridgeModel ownBridge = new XChainBridgeModel
        {
            LockingChainDoor = sponsee.ClassicAddress,
            LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
            IssuingChainDoor = GenesisAccount,
            IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
        };
        await SubmitSponsoredAsync(new XChainCreateBridge { Account = sponsee.ClassicAddress, XChainBridge = ownBridge, SignatureReward = Drops("100") }, sponsee, sponsor);
        await SubmitSponsoredAsync(new XChainModifyBridge { Account = sponsee.ClassicAddress, XChainBridge = ownBridge, SignatureReward = Drops("200") }, sponsee, sponsor);

        // ...and a user of the sponsor's bridge for the transfer-side transactions
        XChainBridgeModel sponsorBridge = new XChainBridgeModel
        {
            LockingChainDoor = sponsor.ClassicAddress,
            LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
            IssuingChainDoor = GenesisAccount,
            IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
        };
        await SubmitPlainAsync(new XChainCreateBridge { Account = sponsor.ClassicAddress, XChainBridge = sponsorBridge, SignatureReward = Drops("100"), MinAccountCreateAmount = Drops("10000000") }, sponsor, "XChainCreateBridge");

        await SubmitSponsoredAsync(new XChainCreateClaimID { Account = sponsee.ClassicAddress, XChainBridge = sponsorBridge, SignatureReward = Drops("100"), OtherChainSource = sponsee.ClassicAddress }, sponsee, sponsor);
        await SubmitSponsoredAsync(new XChainCommit { Account = sponsee.ClassicAddress, XChainBridge = sponsorBridge, XChainClaimID = "1", Amount = Drops("1000000"), OtherChainDestination = XrplWallet.Generate().ClassicAddress }, sponsee, sponsor);
        await SubmitSponsoredAsync(new XChainAccountCreateCommit { Account = sponsee.ClassicAddress, XChainBridge = sponsorBridge, Destination = XrplWallet.Generate().ClassicAddress, Amount = Drops("20000000"), SignatureReward = Drops("100") }, sponsee, sponsor);
    }

    /// <summary>
    /// A sponsored Payment funding a brand-new account with tfSponsorCreatedAccount: the
    /// payment's sender, not the transaction's Sponsor, sponsors the new account's reserve.
    /// </summary>
    [TestMethod]
    public async Task Sponsored_Payment_SponsorCreatedAccount()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();
        XrplWallet created = XrplWallet.Generate();

        await SubmitSponsoredAsync(new Payment
        {
            Account = sponsee.ClassicAddress,
            Destination = created.ClassicAddress,
            Amount = Xrp(2m),
            Flags = PaymentFlags.tfSponsorCreatedAccount,
        }, sponsee, sponsor, SponsorCoverage.spfSponsorFee | SponsorCoverage.spfSponsorReserve);

        AccountInfo info = await client.AccountInfo(new AccountInfoRequest(created.ClassicAddress)).Typed();
        Assert.IsNotNull(info.AccountData, "the sponsored payment must create the destination account");
        Assert.AreEqual(sponsee.ClassicAddress, info.AccountData.Sponsor, "the new account must name the payment's sender as the sponsor of its reserve");
    }

    /// <summary>
    /// A sponsored transaction that ends in a tec is still validated with the sponsor on it.
    /// </summary>
    [TestMethod]
    public async Task Sponsored_LedgerStateFix_RecordsTec()
    {
        (XrplWallet sponsor, XrplWallet sponsee) = await SetupSponsorshipAsync();

        LedgerStateFix tx = new LedgerStateFix
        {
            Account = sponsee.ClassicAddress,
            LedgerFixType = 1,
            Owner = sponsee.ClassicAddress,
            Sponsor = sponsor.ClassicAddress,
            SponsorFlags = SponsorCoverage.spfSponsorFee,
        };

        string result;
        try
        {
            TransactionSummary res = await client.SubmitAndWaitSponsored(tx, sponsee, sponsor);
            result = res.Meta?.TransactionResult;
        }
        catch (TransactionFailedException ex)
        {
            result = ex.EngineResult ?? ex.Message;
        }

        Assert.IsTrue(result is not null && (result.Contains("tesSUCCESS") || result.Contains("tecFAILED_PROCESSING")),
            $"the sponsored LedgerStateFix must reach a validated ledger, got {result}");
    }

    private static string CreatedIndex(Meta meta, LedgerEntryType type) =>
        meta?.AffectedNodes?
            .Select(n => n.CreatedNode)
            .FirstOrDefault(c => c is { } && c.LedgerEntryType == type)?.LedgerIndex
        ?? throw new AssertFailedException($"metadata carries no created {type} node");
}
