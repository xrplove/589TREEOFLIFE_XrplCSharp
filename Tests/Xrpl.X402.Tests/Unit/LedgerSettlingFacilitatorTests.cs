using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

/// <summary>
/// How the ledger-settling facilitator answers each outcome of a settlement - above all one whose
/// outcome the client could not tell, where the payer's funds may already have moved - and what it
/// refuses before and after submitting: a transaction that does not pay the requirement, a delivery
/// short of it, and a blob already settled.
/// </summary>
[TestClass]
public class LedgerSettlingFacilitatorTests
{
    private const string Hash = "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7";
    private const uint LastLedger = 120;
    private const uint ValidatedLedger = 100;
    private const string RlusdHex = "524C555344000000000000000000000000000000";

    private static readonly XrplWallet Payer = XrplWallet.Generate();
    private static readonly string PayTo = XrplWallet.Generate().ClassicAddress;
    private static readonly string Issuer = XrplWallet.Generate().ClassicAddress;
    private static readonly string OtherIssuer = XrplWallet.Generate().ClassicAddress;

    private static Dictionary<string, object> XrpPayment(string drops = "1000000") => new()
    {
        { "TransactionType", "Payment" },
        { "Account", Payer.ClassicAddress },
        { "Destination", PayTo },
        { "Amount", drops },
        { "Fee", "12" },
        { "Sequence", 1u },
        { "LastLedgerSequence", LastLedger },
    };

    private static Dictionary<string, object> TokenPayment(string currency, string issuer, string value)
    {
        Dictionary<string, object> tx = XrpPayment();
        tx["Amount"] = new Dictionary<string, object> { { "currency", currency }, { "issuer", issuer }, { "value", value } };
        return tx;
    }

    private static PaymentRequirement XrpRequirement() =>
        new() { Scheme = "exact", Network = "xrpl:1", Asset = "XRP", PayTo = PayTo, Amount = "1000000" };

    private static PaymentRequirement TokenRequirement(string asset = "RLUSD") => new()
    {
        Scheme = "exact",
        Network = "xrpl:1",
        Asset = asset,
        PayTo = PayTo,
        Amount = "2.5",
        Extra = { ["issuer"] = JsonSerializer.SerializeToElement(Issuer) }
    };

    private static PaymentSignatureEnvelope Envelope(Dictionary<string, object>? tx = null, PaymentRequirement? requirement = null) => new()
    {
        Accepted = requirement ?? XrpRequirement(),
        Payload = new SignedPayload { SignedTxBlob = Payer.Sign(tx ?? XrpPayment()).TxBlob }
    };

    private static TransactionSummary Validated(string result, Currency? delivered = null) =>
        new()
        {
            Validated = true,
            Meta = new Meta
            {
                TransactionResult = result,
                ActuallyDeliveredAmount = delivered ?? new Currency { CurrencyCode = "XRP", Value = "1000000" }
            }
        };

    private static TransactionOutcomeUnknownException Unknown() =>
        new("outcome unknown", Hash, LastLedger, ValidatedLedger, "tesSUCCESS");

    private static LedgerSettlingFacilitator Facilitator(
        Func<TransactionSummary> submit,
        Func<TransactionSummary>? resolve = null,
        List<TransactionOutcomeUnknownException>? resolved = null,
        ISettledPaymentStore? store = null,
        uint validatedLedger = ValidatedLedger) =>
        new(
            (_, _) => Task.FromResult(submit()),
            (unknown, _) =>
            {
                resolved?.Add(unknown);
                return Task.FromResult((resolve ?? throw new AssertFailedException("resolve was not expected"))());
            },
            _ => Task.FromResult(validatedLedger),
            store ?? new InMemorySettledPaymentStore());

    private static TransactionSummary NotSubmitted() => throw new AssertFailedException("submit was not expected");

    [TestMethod]
    public async Task TestUAValidatedSuccessSettles()
    {
        PaymentResponseEnvelope response = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope());

        Assert.IsTrue(response.Success);
        Assert.AreEqual(Payer.ClassicAddress, response.Payer);
    }

    /// <summary>
    /// The client lost track of the submission, and waiting once more finds the payment validated:
    /// the resource is served, not refused while the payer's funds moved.
    /// </summary>
    [TestMethod]
    public async Task TestUAnUnknownOutcomeThatTurnsOutSettledIsServed()
    {
        TransactionOutcomeUnknownException unknown = Unknown();
        List<TransactionOutcomeUnknownException> resolved = new();

        PaymentResponseEnvelope response = await Facilitator(
            () => throw unknown,
            () => Validated("tesSUCCESS"),
            resolved).VerifyAndSettleAsync(Envelope());

        Assert.IsTrue(response.Success);
        Assert.HasCount(1, resolved);
        Assert.AreSame(unknown, resolved[0], "The wait resumes with what the unknown outcome carried.");
    }

    [TestMethod]
    public async Task TestUAnOutcomeThatStaysUnknownIsRefusedWithItsHash()
    {
        PaymentResponseEnvelope response = await Facilitator(
            () => throw Unknown(),
            () => throw Unknown()).VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(response.Success);
        Assert.AreEqual("settlement_unknown", response.ErrorReason);
        Assert.AreEqual(Hash, response.Transaction, "The hash is what reconciling the payment takes.");
        Assert.AreEqual(Payer.ClassicAddress, response.Payer);
    }

    /// <summary>A store that fails to drop the claim does not take the hash out of the response.</summary>
    [TestMethod]
    public async Task TestUAStoreThatCannotReleaseKeepsTheUnknownOutcome()
    {
        PaymentResponseEnvelope response = await Facilitator(
            () => throw Unknown(),
            () => throw Unknown(),
            store: new UnreleasableStore()).VerifyAndSettleAsync(Envelope());

        Assert.AreEqual("settlement_unknown", response.ErrorReason);
        Assert.AreEqual(Hash, response.Transaction);
    }

    private sealed class UnreleasableStore : ISettledPaymentStore
    {
        private readonly InMemorySettledPaymentStore _inner = new();

        public ValueTask<bool> TryReserveAsync(string transactionHash, uint lastLedgerSequence, uint validatedLedgerIndex, CancellationToken cancellationToken = default) =>
            _inner.TryReserveAsync(transactionHash, lastLedgerSequence, validatedLedgerIndex, cancellationToken);

        public ValueTask ReleaseAsync(string transactionHash, CancellationToken cancellationToken = default) =>
            throw new System.TimeoutException("store unavailable");
    }

    [TestMethod]
    public async Task TestUAnUnknownOutcomeThatTurnsOutExpiredFails()
    {
        PaymentResponseEnvelope response = await Facilitator(
            () => throw Unknown(),
            () => throw new TransactionExpiredException("expired", Hash, LastLedger, ValidatedLedger, "tesSUCCESS", searchedAll: true)).VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(response.Success);
        Assert.AreEqual("settlement_failed", response.ErrorReason);
    }

    [TestMethod]
    public async Task TestUAFinalFailureFails()
    {
        PaymentResponseEnvelope failed = await Facilitator(
            () => throw new TransactionFailedException("tec", "tecUNFUNDED_PAYMENT", Hash, Validated("tecUNFUNDED_PAYMENT"))).VerifyAndSettleAsync(Envelope());
        PaymentResponseEnvelope expired = await Facilitator(
            () => throw new TransactionExpiredException("expired", Hash, LastLedger, ValidatedLedger, "terPRE_SEQ", searchedAll: false)).VerifyAndSettleAsync(Envelope());

        Assert.AreEqual("settlement_failed", failed.ErrorReason);
        Assert.AreEqual("settlement_failed", expired.ErrorReason);
    }

    [TestMethod]
    public async Task TestUAnyOtherErrorIsASettlementError()
    {
        PaymentResponseEnvelope response = await Facilitator(() => throw new InvalidOperationException("boom")).VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(response.Success);
        Assert.AreEqual("settlement_error", response.ErrorReason);
    }

    [TestMethod]
    public async Task TestUTheCallersCancellationPropagates()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        LedgerSettlingFacilitator facilitator = new(
            (_, token) => throw new TransactionWaitCanceledException("cancelled", Hash, LastLedger, ValidatedLedger, null!, new OperationCanceledException(token), token),
            (_, _) => throw new AssertFailedException("resolve was not expected"),
            _ => Task.FromResult(ValidatedLedger),
            new InMemorySettledPaymentStore());

        await Assert.ThrowsAsync<OperationCanceledException>(() => facilitator.VerifyAndSettleAsync(Envelope(), cancel.Token));
    }

    /// <summary>A CheckCreate has a Destination and ends in tesSUCCESS, yet pays nothing.</summary>
    [TestMethod]
    public async Task TestUATransactionOtherThanAPaymentIsRefused()
    {
        Dictionary<string, object> check = XrpPayment();
        check["TransactionType"] = "CheckCreate";
        check.Remove("Amount");
        check["SendMax"] = "1000000";

        PaymentResponseEnvelope response = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(check));

        Assert.IsFalse(response.Success);
        Assert.AreEqual("invalid_transaction_type", response.ErrorReason);
    }

    [TestMethod]
    public async Task TestUAPaymentToAnotherAccountIsRefused()
    {
        Dictionary<string, object> tx = XrpPayment();
        tx["Destination"] = Issuer;

        PaymentResponseEnvelope response = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(tx));

        Assert.AreEqual("invalid_destination", response.ErrorReason);
    }

    /// <summary>A partial payment can deliver any fraction of its Amount and still succeed.</summary>
    [TestMethod]
    public async Task TestUAPartialPaymentIsRefused()
    {
        Dictionary<string, object> tx = XrpPayment();
        tx["Flags"] = (uint)PaymentFlags.tfPartialPayment;

        PaymentResponseEnvelope response = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(tx));

        Assert.AreEqual("invalid_partial_payment", response.ErrorReason);
    }

    [TestMethod]
    public async Task TestUOtherFlagsAreAccepted()
    {
        Dictionary<string, object> tx = XrpPayment();
        tx["Flags"] = (uint)PaymentFlags.tfNoDirectRipple;

        PaymentResponseEnvelope response = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(tx));

        Assert.IsTrue(response.Success);
    }

    [TestMethod]
    public async Task TestUAnIssuedLowercaseXrpIsNotPaidInDrops()
    {
        PaymentRequirement requirement = TokenRequirement("xrp");
        requirement.Amount = "1000000";
        Currency delivered = new() { CurrencyCode = "xrp", Issuer = Issuer, Value = "1000000" };

        PaymentResponseEnvelope drops = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(XrpPayment("1000000"), requirement));
        PaymentResponseEnvelope token = await Facilitator(() => Validated("tesSUCCESS", delivered))
            .VerifyAndSettleAsync(Envelope(TokenPayment("xrp", Issuer, "1000000"), requirement));

        Assert.AreEqual("invalid_asset", drops.ErrorReason);
        Assert.IsTrue(token.Success);
    }

    [TestMethod]
    public async Task TestUAPaymentInAnotherAssetIsRefused()
    {
        PaymentResponseEnvelope otherCurrency = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(TokenPayment("USD", Issuer, "2.5"), TokenRequirement()));
        PaymentResponseEnvelope otherIssuer = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, OtherIssuer, "2.5"), TokenRequirement()));
        PaymentResponseEnvelope tokenForXrp = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(TokenPayment("USD", Issuer, "1000000"), XrpRequirement()));
        PaymentResponseEnvelope xrpForToken = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(XrpPayment("2500000"), TokenRequirement()));

        Assert.AreEqual("invalid_asset", otherCurrency.ErrorReason);
        Assert.AreEqual("invalid_asset", otherIssuer.ErrorReason);
        Assert.AreEqual("invalid_asset", tokenForXrp.ErrorReason);
        Assert.AreEqual("invalid_asset", xrpForToken.ErrorReason);
    }

    /// <summary>
    /// The requirement may name a non-standard code as text or as hex, and a standard code as
    /// three letters or as hex; the decoder renders each in only one of those forms.
    /// </summary>
    [TestMethod]
    public async Task TestUACurrencyMatchesWhicheverWayItIsWritten()
    {
        Currency deliveredRlusd = new() { CurrencyCode = RlusdHex, Issuer = Issuer, Value = "2.5" };
        Currency deliveredUsd = new() { CurrencyCode = "USD", Issuer = Issuer, Value = "2.5" };

        PaymentResponseEnvelope rlusdAsText = await Facilitator(() => Validated("tesSUCCESS", deliveredRlusd))
            .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, Issuer, "2.5"), TokenRequirement("RLUSD")));
        PaymentResponseEnvelope rlusdAsHex = await Facilitator(() => Validated("tesSUCCESS", deliveredRlusd))
            .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, Issuer, "2.5"), TokenRequirement(RlusdHex.ToLowerInvariant())));
        PaymentResponseEnvelope usdAsHex = await Facilitator(() => Validated("tesSUCCESS", deliveredUsd))
            .VerifyAndSettleAsync(Envelope(TokenPayment("USD", Issuer, "2.5"), TokenRequirement("0000000000000000000000005553440000000000")));

        Assert.IsTrue(rlusdAsText.Success, rlusdAsText.ErrorReason);
        Assert.IsTrue(rlusdAsHex.Success, rlusdAsHex.ErrorReason);
        Assert.IsTrue(usdAsHex.Success, usdAsHex.ErrorReason);
    }

    [TestMethod]
    public async Task TestUAnAmountOtherThanTheRequiredOneIsRefused()
    {
        PaymentResponseEnvelope shortDrops = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(XrpPayment("999999")));
        PaymentResponseEnvelope oneDrop = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(XrpPayment("1")));
        PaymentResponseEnvelope shortToken = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, Issuer, "2.4"), TokenRequirement()));

        Assert.AreEqual("invalid_amount", shortDrops.ErrorReason);
        Assert.AreEqual("invalid_amount", oneDrop.ErrorReason);
        Assert.AreEqual("invalid_amount", shortToken.ErrorReason);
    }

    /// <summary>Without a LastLedgerSequence a blob stays valid forever, past any record of its hash.</summary>
    [TestMethod]
    public async Task TestUAPaymentWithoutALastLedgerSequenceIsRefused()
    {
        Dictionary<string, object> tx = XrpPayment();
        tx.Remove("LastLedgerSequence");

        PaymentResponseEnvelope response = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(tx));

        Assert.AreEqual("invalid_last_ledger_sequence", response.ErrorReason);
    }

    /// <summary>
    /// A blob past its window cannot enter a new ledger: a success reported for it is an earlier
    /// settlement replayed, even one no store remembers.
    /// </summary>
    [TestMethod]
    public async Task TestUAPaymentWhoseWindowHasPassedIsRefused()
    {
        PaymentResponseEnvelope atTheEnd = await Facilitator(NotSubmitted, validatedLedger: LastLedger).VerifyAndSettleAsync(Envelope());
        PaymentResponseEnvelope past = await Facilitator(NotSubmitted, validatedLedger: LastLedger + 50).VerifyAndSettleAsync(Envelope());

        Assert.AreEqual("payment_expired", atTheEnd.ErrorReason);
        Assert.AreEqual("payment_expired", past.ErrorReason);
    }

    /// <summary>
    /// A path payment can validate with tesSUCCESS and deliver less than its Amount only when it is
    /// partial, but the facilitator reads what the metadata says arrived, not what was asked.
    /// </summary>
    [TestMethod]
    public async Task TestUADeliveryShortOfTheRequirementIsRefused()
    {
        InMemorySettledPaymentStore store = new();
        PaymentSignatureEnvelope envelope = Envelope();

        PaymentResponseEnvelope shortXrp = await Facilitator(
            () => Validated("tesSUCCESS", new Currency { CurrencyCode = "XRP", Value = "1" }), store: store).VerifyAndSettleAsync(envelope);
        PaymentResponseEnvelope shortToken = await Facilitator(
            () => Validated("tesSUCCESS", new Currency { CurrencyCode = RlusdHex, Issuer = Issuer, Value = "2.49" }))
            .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, Issuer, "2.5"), TokenRequirement()));
        PaymentResponseEnvelope otherIssuer = await Facilitator(
            () => Validated("tesSUCCESS", new Currency { CurrencyCode = RlusdHex, Issuer = OtherIssuer, Value = "2.5" }))
            .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, Issuer, "2.5"), TokenRequirement()));
        PaymentResponseEnvelope missing = await Facilitator(
            () => new TransactionSummary { Validated = true, Meta = new Meta { TransactionResult = "tesSUCCESS" } })
            .VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(shortXrp.Success);
        Assert.AreEqual("delivered_amount_mismatch", shortXrp.ErrorReason);
        Assert.AreEqual("delivered_amount_mismatch", shortToken.ErrorReason);
        Assert.AreEqual("delivered_amount_mismatch", otherIssuer.ErrorReason);
        Assert.AreEqual("delivered_amount_mismatch", missing.ErrorReason);

        PaymentResponseEnvelope again = await Facilitator(NotSubmitted, store: store).VerifyAndSettleAsync(envelope);
        Assert.AreEqual("duplicate_payment", again.ErrorReason, "Funds moved once; the blob is not settled a second time.");
    }

    /// <summary>
    /// Resubmitting a validated blob makes the node answer tefPAST_SEQ, and the wait finds the
    /// original tesSUCCESS: only the facilitator's record stops the same payment unlocking twice.
    /// </summary>
    [TestMethod]
    public async Task TestUASettledPaymentIsNotSettledAgain()
    {
        InMemorySettledPaymentStore store = new();
        PaymentSignatureEnvelope envelope = Envelope();
        int submissions = 0;
        LedgerSettlingFacilitator facilitator = Facilitator(() => { submissions++; return Validated("tesSUCCESS"); }, store: store);

        PaymentResponseEnvelope first = await facilitator.VerifyAndSettleAsync(envelope);
        PaymentResponseEnvelope replay = await facilitator.VerifyAndSettleAsync(envelope);
        PaymentResponseEnvelope elsewhere = await Facilitator(NotSubmitted, store: store).VerifyAndSettleAsync(envelope);

        Assert.IsTrue(first.Success);
        Assert.IsFalse(replay.Success);
        Assert.AreEqual("duplicate_payment", replay.ErrorReason);
        Assert.AreEqual("duplicate_payment", elsewhere.ErrorReason, "Facilitators sharing a store share what was settled.");
        Assert.AreEqual(1, submissions);
    }

    [TestMethod]
    public async Task TestUConcurrentPresentationsOfOneBlobSettleOnce()
    {
        InMemorySettledPaymentStore store = new();
        PaymentSignatureEnvelope envelope = Envelope();
        TaskCompletionSource<TransactionSummary> ledger = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int submissions = 0;
        LedgerSettlingFacilitator facilitator = new(
            (_, _) => { Interlocked.Increment(ref submissions); return ledger.Task; },
            (_, _) => throw new AssertFailedException("resolve was not expected"),
            _ => Task.FromResult(ValidatedLedger),
            store);

        Task<PaymentResponseEnvelope> first = facilitator.VerifyAndSettleAsync(envelope);
        PaymentResponseEnvelope second = await facilitator.VerifyAndSettleAsync(envelope);
        ledger.SetResult(Validated("tesSUCCESS"));

        Assert.IsTrue((await first).Success);
        Assert.AreEqual("duplicate_payment", second.ErrorReason);
        Assert.AreEqual(1, submissions);
    }

    /// <summary>
    /// A settlement that failed or could not be told moved nothing the facilitator can count on, so
    /// the payer may present the same blob again - the reconciliation path for settlement_unknown.
    /// </summary>
    [TestMethod]
    public async Task TestUAnUnsettledPaymentCanBePresentedAgain()
    {
        InMemorySettledPaymentStore store = new();
        PaymentSignatureEnvelope envelope = Envelope();

        PaymentResponseEnvelope unknown = await Facilitator(() => throw Unknown(), () => throw Unknown(), store: store).VerifyAndSettleAsync(envelope);
        PaymentResponseEnvelope error = await Facilitator(() => throw new InvalidOperationException("boom"), store: store).VerifyAndSettleAsync(envelope);
        PaymentResponseEnvelope retried = await Facilitator(() => Validated("tesSUCCESS"), store: store).VerifyAndSettleAsync(envelope);

        Assert.AreEqual("settlement_unknown", unknown.ErrorReason);
        Assert.AreEqual("settlement_error", error.ErrorReason);
        Assert.IsTrue(retried.Success);
        Assert.AreEqual(1, store.Count);
    }

    private static PaymentRequirement WithExtra(PaymentRequirement requirement, string key, object value)
    {
        requirement.Extra[key] = JsonSerializer.SerializeToElement(value);
        return requirement;
    }

    private static string InvoiceHash(string invoiceId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invoiceId)));

    private static Dictionary<string, object> WithMemo(Dictionary<string, object> tx, string invoiceId)
    {
        tx["Memos"] = new List<object>
        {
            new Dictionary<string, object> { { "Memo", new Dictionary<string, object> { { "MemoData", Convert.ToHexString(Encoding.UTF8.GetBytes(invoiceId)) } } } }
        };
        return tx;
    }

    /// <summary>
    /// The t54 payer binds the invoice as the InvoiceID hash, as a memo, or both; a blob paid under
    /// one invoice must not settle another of the same price and payee.
    /// </summary>
    [TestMethod]
    public async Task TestUAnInvoiceIdMustBeBoundIntoTheTransaction()
    {
        PaymentRequirement Invoiced() => WithExtra(XrpRequirement(), "invoiceId", "inv-1");
        Dictionary<string, object> byField = XrpPayment();
        byField["InvoiceID"] = InvoiceHash("inv-1");
        Dictionary<string, object> otherInvoice = XrpPayment();
        otherInvoice["InvoiceID"] = InvoiceHash("inv-2");

        PaymentResponseEnvelope field = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(byField, Invoiced()));
        PaymentResponseEnvelope memo = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(WithMemo(XrpPayment(), "inv-1"), Invoiced()));
        PaymentResponseEnvelope unbound = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(XrpPayment(), Invoiced()));
        PaymentResponseEnvelope other = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(otherInvoice, Invoiced()));
        PaymentResponseEnvelope otherMemo = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(WithMemo(XrpPayment(), "inv-2"), Invoiced()));

        Assert.IsTrue(field.Success, field.ErrorReason);
        Assert.IsTrue(memo.Success, memo.ErrorReason);
        Assert.AreEqual("invalid_invoice_id", unbound.ErrorReason);
        Assert.AreEqual("invalid_invoice_id", other.ErrorReason);
        Assert.AreEqual("invalid_invoice_id", otherMemo.ErrorReason);
    }

    /// <summary>An exchange credits by the tag, so the tag an X-address or extra.destinationTag names is required.</summary>
    [TestMethod]
    public async Task TestUADestinationTagThePayeeNamesIsRequired()
    {
        PaymentRequirement ToXAddress()
        {
            PaymentRequirement requirement = XrpRequirement();
            requirement.PayTo = XrplAddressCodec.ClassicAddressToXAddress(PayTo, 7, isTest: false);
            return requirement;
        }

        Dictionary<string, object> Tagged(uint tag)
        {
            Dictionary<string, object> tx = XrpPayment();
            tx["DestinationTag"] = tag;
            return tx;
        }

        PaymentResponseEnvelope xAddress = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(Tagged(7), ToXAddress()));
        PaymentResponseEnvelope xAddressOtherTag = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(Tagged(8), ToXAddress()));
        PaymentResponseEnvelope xAddressNoTag = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(XrpPayment(), ToXAddress()));
        PaymentResponseEnvelope extraTag = await Facilitator(() => Validated("tesSUCCESS"))
            .VerifyAndSettleAsync(Envelope(Tagged(5), WithExtra(XrpRequirement(), "destinationTag", 5)));
        PaymentResponseEnvelope extraOtherTag = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(Tagged(6), WithExtra(XrpRequirement(), "destinationTag", 5)));
        PaymentResponseEnvelope conflicting = await Facilitator(NotSubmitted)
            .VerifyAndSettleAsync(Envelope(Tagged(7), WithExtra(ToXAddress(), "destinationTag", 5)));
        PaymentResponseEnvelope untaggedPayee = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(Tagged(9)));

        Assert.IsTrue(xAddress.Success, xAddress.ErrorReason);
        Assert.AreEqual("invalid_destination", xAddressOtherTag.ErrorReason);
        Assert.AreEqual("invalid_destination", xAddressNoTag.ErrorReason);
        Assert.IsTrue(extraTag.Success, extraTag.ErrorReason);
        Assert.AreEqual("invalid_destination", extraOtherTag.ErrorReason);
        Assert.AreEqual("invalid_destination", conflicting.ErrorReason);
        Assert.IsTrue(untaggedPayee.Success, "A payee that names no tag accepts any.");
    }

    /// <summary>The window bounds how long a settled hash is kept; a blob valid for ever would be kept for ever.</summary>
    [TestMethod]
    public async Task TestUAWindowLongerThanTheTimeoutAllowsIsRefused()
    {
        Dictionary<string, object> Until(uint lastLedger)
        {
            Dictionary<string, object> tx = XrpPayment();
            tx["LastLedgerSequence"] = lastLedger;
            return tx;
        }

        PaymentRequirement TenMinutes()
        {
            PaymentRequirement requirement = XrpRequirement();
            requirement.MaxTimeoutSeconds = 600;
            return requirement;
        }

        PaymentResponseEnvelope forever = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(Until(uint.MaxValue)));
        PaymentResponseEnvelope autofillWindow = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(Until(ValidatedLedger + 21)));
        PaymentResponseEnvelope autofillOnANodeAhead = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(Until(ValidatedLedger + 31)));
        PaymentResponseEnvelope pastAutofillWindow = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(Until(ValidatedLedger + 32)));
        PaymentResponseEnvelope signerWindow = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(Until(ValidatedLedger + 151), TenMinutes()));
        PaymentResponseEnvelope pastTimeout = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(Until(ValidatedLedger + 212), TenMinutes()));

        Assert.AreEqual("invalid_last_ledger_sequence", forever.ErrorReason);
        Assert.IsTrue(autofillWindow.Success, autofillWindow.ErrorReason);
        Assert.IsTrue(autofillOnANodeAhead.Success, "Autofill on a payer's node 10 validated ledgers ahead of the facilitator's.");
        Assert.AreEqual("invalid_last_ledger_sequence", pastAutofillWindow.ErrorReason);
        Assert.IsTrue(signerWindow.Success, "XrplWalletX402Signer's window for 600 s: the open ledger plus 150.");
        Assert.AreEqual("invalid_last_ledger_sequence", pastTimeout.ErrorReason);
    }

    [TestMethod]
    public async Task TestUAWindowEndingInTheNextLedgerIsStillOpen()
    {
        Dictionary<string, object> tx = XrpPayment();
        tx["LastLedgerSequence"] = ValidatedLedger + 1;

        PaymentResponseEnvelope response = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope(tx));

        Assert.IsTrue(response.Success, response.ErrorReason);
    }

    /// <summary>The ledger writes a token value in any equal form: trailing zeros, an exponent.</summary>
    [TestMethod]
    public async Task TestUATokenAmountIsComparedAsANumber()
    {
        PaymentRequirement OneRlusd()
        {
            PaymentRequirement requirement = TokenRequirement();
            requirement.Amount = "1";
            return requirement;
        }

        foreach (string written in new[] { "1", "1.0", "1e0", "0.1e1" })
        {
            Currency delivered = new() { CurrencyCode = RlusdHex, Issuer = Issuer, Value = written };
            PaymentResponseEnvelope response = await Facilitator(() => Validated("tesSUCCESS", delivered))
                .VerifyAndSettleAsync(Envelope(TokenPayment(RlusdHex, Issuer, written), OneRlusd()));

            Assert.IsTrue(response.Success, $"{written}: {response.ErrorReason}");
        }
    }

    /// <summary>
    /// rippled writes "unavailable" for a delivery it cannot tell (ledgers before 2014); an MPT
    /// delivery is not the XRP or issued currency the requirement names.
    /// </summary>
    [TestMethod]
    public async Task TestUADeliveryThatCannotBeReadIsRefused()
    {
        PaymentResponseEnvelope unavailable = await Facilitator(
            () => Validated("tesSUCCESS", new Currency { CurrencyCode = "XRP", Value = "unavailable" })).VerifyAndSettleAsync(Envelope());
        PaymentResponseEnvelope mpt = await Facilitator(
            () => Validated("tesSUCCESS", new Currency { MPTokenIssuanceID = "0000000100000000000000000000000000000000000000AB", Value = "1000000" }))
            .VerifyAndSettleAsync(Envelope());
        PaymentResponseEnvelope noMeta = await Facilitator(() => new TransactionSummary { Validated = true }).VerifyAndSettleAsync(Envelope());

        Assert.AreEqual("delivered_amount_mismatch", unavailable.ErrorReason);
        Assert.AreEqual("delivered_amount_mismatch", mpt.ErrorReason);
        Assert.AreEqual("settlement_failed", noMeta.ErrorReason, "Without metadata there is no result to call a success.");
    }

    [TestMethod]
    public async Task TestUAnMptAmountIsRefused()
    {
        Dictionary<string, object> tx = XrpPayment();
        tx["Amount"] = new Dictionary<string, object> { { "mpt_issuance_id", "0000000100000000000000000000000000000000000000AB" }, { "value", "2" } };

        PaymentResponseEnvelope response = await Facilitator(NotSubmitted).VerifyAndSettleAsync(Envelope(tx, TokenRequirement()));

        Assert.AreEqual("invalid_asset", response.ErrorReason);
    }

    [TestMethod]
    public async Task TestUTheStoreForgetsAClaimOnlyWellPastItsWindow()
    {
        InMemorySettledPaymentStore store = new(retentionMarginLedgers: 10);

        Assert.IsTrue(await store.TryReserveAsync(Hash, lastLedgerSequence: 120, validatedLedgerIndex: 100));
        Assert.IsFalse(await store.TryReserveAsync(Hash.ToLowerInvariant(), 120, 100), "Hashes compare ignoring case.");
        Assert.IsFalse(await store.TryReserveAsync(Hash, 120, 130), "Still inside the margin.");
        Assert.IsTrue(await store.TryReserveAsync(Hash, 200, 131), "Past LastLedgerSequence + margin the claim is dropped.");
    }

    [TestMethod]
    public void TestUTheServiceCollectionRegistersOneFacilitatorWithAStore()
    {
        using XrplClient client = new("ws://127.0.0.1:1");
        ServiceCollection services = new();
        services.AddSingleton<IXrplClient>(client);
        services.AddLedgerSettlingFacilitator();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<InMemorySettledPaymentStore>(provider.GetRequiredService<ISettledPaymentStore>());
        Assert.AreSame(provider.GetRequiredService<LedgerSettlingFacilitator>(), provider.GetRequiredService<IX402Facilitator>());

        ISettledPaymentStore shared = new InMemorySettledPaymentStore();
        ServiceCollection withShared = new();
        withShared.AddSingleton<IXrplClient>(client);
        withShared.AddSingleton(shared);
        withShared.AddLedgerSettlingFacilitator();
        using ServiceProvider sharedProvider = withShared.BuildServiceProvider();

        Assert.AreSame(shared, sharedProvider.GetRequiredService<ISettledPaymentStore>(), "A store registered first is kept.");
    }
}
