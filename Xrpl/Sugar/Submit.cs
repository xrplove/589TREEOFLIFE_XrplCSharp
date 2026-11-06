using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using JsonSerializer = System.Text.Json.JsonSerializer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/submit.ts

namespace Xrpl.Sugar;

public static class SubmitSugar
{
    private const int LEDGER_CLOSE_TIME = 1000;

    /// <summary>
    /// Submits a signed/unsigned transaction.<br/>
    /// Steps performed on a transaction:<br/>
    /// 1.<br/>
    /// Autofill.<br/>
    /// 2.<br/>
    /// Sign and Encode.<br/>
    /// 3.<br/>
    /// Submit.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A promise that contains SubmitResponse</returns>
    public static async Task<Submit> Submit(
        this IXrplClient client,
        Dictionary<string, object> transaction,
        bool autofill = true,
        bool failHard = false,
        XrplWallet wallet = null,
        CancellationToken cancellationToken = default
    )
    {
        var (signedTx, _) = await client.GetSignedTx(transaction, autofill, wallet, cancellationToken);
        return await SubmitRequest(client, signedTx, failHard, cancellationToken);
    }

    /// <summary>
    /// Asynchronously submits a transaction and verifies that it has been included in a
    /// validated ledger(or has errored/will not be included for some reason).
    /// See[Reliable Transaction Submission] (https://xrpl.org/reliable-transaction-submission.html).
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A promise that contains TxResponse, that will return when the transaction has been validated.</returns>
    /// <remarks>
    /// The outcomes, and what each means for submitting again, are those of <see cref="SubmitRequestAndWait"/>.
    /// </remarks>
    public static Task<TransactionSummary> SubmitAndWait(
        this IXrplClient client,
        ITransactionRequest transaction,
        XrplWallet wallet = null,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default) =>
        SubmitAndWait(client, transaction.ToDictionary(), wallet, autofill, failHard, cancellationToken);
    /// <summary>
    /// Asynchronously submits a transaction and verifies that it has been included in a
    /// validated ledger(or has errored/will not be included for some reason).
    /// See[Reliable Transaction Submission] (https://xrpl.org/reliable-transaction-submission.html).
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A promise that contains TxResponse, that will return when the transaction has been validated.</returns>
    /// <remarks>
    /// The outcomes, and what each means for submitting again, are those of <see cref="SubmitRequestAndWait"/>.
    /// </remarks>
    public static async Task<TransactionSummary> SubmitAndWait(
        this IXrplClient client,
        Dictionary<string, object> transaction,
        XrplWallet wallet = null,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var (signedTx, tx) = await client.GetSignedTx(transaction, autofill, wallet, cancellationToken);
        var lastLedger = GetLastLedgerSequence(tx);
        if (lastLedger == null)
        {
            throw new ValidationException(
                "Transaction must contain a LastLedgerSequence value for reliable submission.");
        }

        return await SubmitSignedAndWait(client, signedTx, lastLedger.Value, failHard, cancellationToken);
    }

    /// <summary>
    /// Submits a signed transaction and waits until it is in a validated ledger or will never be.
    /// </summary>
    /// <remarks>
    /// Outcomes, and what each means for submitting again:
    /// <list type="bullet">
    /// <item><description>a <see cref="TransactionSummary"/>: validated with <c>tesSUCCESS</c>;</description></item>
    /// <item><description><see cref="TransactionFailedException"/>: final failure, applied with a <c>tec</c>
    /// (<see cref="TransactionFailedException.ReachedLedger"/>) or refused by the node;</description></item>
    /// <item><description><see cref="TransactionExpiredException"/>: never applied, a replacement is safe;</description></item>
    /// <item><description><see cref="TransactionOutcomeUnknownException"/>: possibly applied, find out with
    /// <see cref="WaitForTransactionOutcome"/> before sending a replacement. Any failure of the submit
    /// request other than an error answer from the node ends here, with the cause inside;</description></item>
    /// <item><description>an error answer to the submit request (<see cref="RippledException"/>), or any
    /// failure before it - autofill, signing, reading the validated ledger: nothing was submitted.</description></item>
    /// </list>
    /// A cancelled <paramref name="cancellationToken"/> raises <see cref="OperationCanceledException"/>.
    /// Once the submit request is sent it is a <see cref="TransactionWaitCanceledException"/>, whose
    /// <c>Hash</c>, <c>LastLedgerSequence</c> and <c>MinLedger</c> resume the wait with
    /// <see cref="WaitForTransactionOutcome"/>.
    /// </remarks>
    public static async Task<TransactionSummary> SubmitRequestAndWait(this IXrplClient client, object signedTransaction, bool failHard, CancellationToken cancellationToken = default)
    {
        var signedTx = GetTxBlob(signedTransaction);
        var decoded = XrplBinaryCodec.Decode(signedTx).ToString();
        var tx = JsonNode.Parse(decoded)?.AsObject();
        var lastLedger = GetLastLedgerSequence(tx);
        if (lastLedger == null)
        {
            throw new ValidationException(
                "Transaction must contain a LastLedgerSequence value for reliable submission.");
        }

        return await SubmitSignedAndWait(client, signedTx, lastLedger.Value, failHard, cancellationToken);
    }

    /// <summary>
    /// Waits for a transaction that was already submitted until it is in a validated ledger or will
    /// never be, without submitting it again.
    /// </summary>
    /// <remarks>
    /// For a transaction whose submission was interrupted - a crash, a timeout, a
    /// <see cref="TransactionOutcomeUnknownException"/>. The outcomes are those of
    /// <see cref="SubmitRequestAndWait"/>. A <see cref="TransactionExpiredException"/> needs a
    /// server that holds every ledger from <paramref name="minLedger"/> to
    /// <paramref name="lastLedgerSequence"/>.
    /// </remarks>
    /// <param name="client">A Client.</param>
    /// <param name="txHash">The transaction's hash.</param>
    /// <param name="lastLedgerSequence">The transaction's <c>LastLedgerSequence</c>.</param>
    /// <param name="minLedger">The ledger validated before the transaction was first submitted.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    public static Task<TransactionSummary> WaitForTransactionOutcome(
        this IXrplClient client,
        string txHash,
        uint lastLedgerSequence,
        uint minLedger,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(txHash))
            throw new ArgumentException("A transaction hash is required.", nameof(txHash));

        return WaitForFinalTransactionOutcome(
            Lookup(client),
            client.GetLedgerIndex,
            LedgerHoldsTransaction(client, txHash),
            txHash,
            lastLedgerSequence,
            minLedger,
            submissionResult: null,
            failHard: false,
            TimeSpan.FromMilliseconds(LEDGER_CLOSE_TIME),
            cancellationToken);
    }

    private static async Task<TransactionSummary> SubmitSignedAndWait(
        IXrplClient client,
        string signedTx,
        uint lastLedger,
        bool failHard,
        CancellationToken cancellationToken)
    {
        string txHash = HashLedger.HashSignedTx(signedTx);

        // Read before anything is sent, so a failure here still means nothing was submitted. The
        // transaction cannot be in this ledger or an earlier one, which bounds the search for it.
        uint minLedger = await client.GetLedgerIndex(cancellationToken);

        Submit response = await SubmitTracked(
            token => client.SubmitRequest(signedTx, failHard, token),
            txHash,
            lastLedger,
            minLedger,
            cancellationToken);

        return await WaitForFinalTransactionOutcome(
            Lookup(client),
            client.GetLedgerIndex,
            LedgerHoldsTransaction(client, txHash),
            txHash,
            lastLedger,
            minLedger,
            response.EngineResult,
            failHard,
            TimeSpan.FromMilliseconds(LEDGER_CLOSE_TIME),
            cancellationToken);
    }

    /// <summary>
    /// Sends the submit request. A failure after which the transaction may be on the network
    /// becomes <see cref="TransactionOutcomeUnknownException"/>, or
    /// <see cref="TransactionWaitCanceledException"/> when the caller cancelled; the rest
    /// propagates as it is.
    /// </summary>
    internal static async Task<Submit> SubmitTracked(
        Func<CancellationToken, Task<Submit>> submit,
        string txHash,
        uint lastLedgerSequence,
        uint minLedger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await submit(cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw Canceled(ex, txHash, lastLedgerSequence, minLedger, preliminaryResult: null, cancellationToken);
        }
        catch (Exception ex) when (!WasNotSubmitted(ex))
        {
            throw new TransactionOutcomeUnknownException(
                $"Submitting transaction {txHash} failed after it may have reached the network: {ex.Message}",
                txHash,
                lastLedgerSequence,
                minLedger,
                preliminaryResult: null,
                ex);
        }
    }

    private static TransactionWaitCanceledException Canceled(
        OperationCanceledException cancellation,
        string txHash,
        uint lastLedgerSequence,
        uint minLedger,
        string preliminaryResult,
        CancellationToken cancellationToken) =>
        new TransactionWaitCanceledException(
            $"Cancelled while transaction {txHash} may already be on the network.",
            txHash,
            lastLedgerSequence,
            minLedger,
            preliminaryResult,
            cancellation,
            cancellationToken);

    /// <summary>
    /// Whether a failed submit request certainly left nothing on the network.
    /// </summary>
    /// <remarks>
    /// Only an error answer from the node is certain: it refused the request. The connection
    /// rejects a request it already wrote to the socket with the same types it uses for one it
    /// never sent - <see cref="DisconnectedException"/> on a close, the not-connected family when
    /// a reconnect gives up - so none of those tells the two apart. Nor does an <c>internal</c>
    /// error: rippled answers <c>internalSubmit</c> when processing the transaction throws, and
    /// <c>internalJson</c> after it was processed.
    /// </remarks>
    internal static bool WasNotSubmitted(Exception exception) =>
        exception is RippledException rippled
        && !(rippled.Response?.Error?.StartsWith("internal", StringComparison.Ordinal) ?? false);

    private static Func<TxRequest, CancellationToken, Task<TransactionSummary>> Lookup(IXrplClient client) =>
        (request, cancellationToken) => client.TxV2(request, cancellationToken).Typed();

    /// <summary>
    /// Whether validated ledger <c>index</c> holds the transaction; <c>null</c> when the server
    /// does not have that ledger validated.
    /// </summary>
    private static Func<uint, CancellationToken, Task<bool?>> LedgerHoldsTransaction(IXrplClient client, string txHash) =>
        async (index, cancellationToken) =>
        {
            LOLedger response;
            try
            {
                response = await client.Ledger(
                    new LedgerRequest { LedgerIndex = new LedgerIndex(index), Transactions = true, Expand = false },
                    cancellationToken).Typed();
            }
            catch (RippledException)
            {
                // lgrNotFound and its kin: the server does not hold this ledger.
                return null;
            }

            if (!response.Validated || response.LedgerEntity is not LedgerEntity ledger || ledger.Transactions == null)
                return null;

            foreach (HashOrTransaction transaction in ledger.Transactions)
            {
                if (string.Equals(transaction?.TransactionHash, txHash, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        };
    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="signedTransaction">signed Transaction</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitRequest(this IXrplClient client, object signedTransaction, bool failHard, CancellationToken cancellationToken = default)
    {
        var signedTxEncoded = GetTxBlob(signedTransaction);

        var request = new SubmitRequest
        {
            Command = "submit",
            TxBlob = signedTxEncoded,
            FailHard = failHard,
        };
        var response = await client.GRequest<Submit, SubmitRequest>(request, cancellationToken).Typed();
        return response;
    }

    private static string GetTxBlob(object signedTransaction)
    {
        string signedTxEncoded;
        if (signedTransaction is string transaction)
        {
            signedTxEncoded = transaction;
        }
        else if (signedTransaction is SignatureResult { } sg)
        {
            signedTxEncoded = sg.TxBlob;
        }
        else
        {
            signedTxEncoded = XrplBinaryCodec.Encode(signedTransaction);
        }

        return signedTxEncoded;
    }

    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMulti(
        this IXrplClient client,
        ITransactionRequest tx,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var json = tx.ToJson();
        var txJson = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default)
                     ?? throw new ValidationException("Failed to deserialize tx json");
        var response = await SubmitMulti(client, txJson, wallets, autofill, failHard, cancellationToken);
        return response;
    }

    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMulti(
        this IXrplClient client,
        Dictionary<string, object> tx,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        if (wallets is null)
        {
            throw new ValidationException("Wallets must be provided when submitting an unsigned transaction");
        }

        var xrplWallets = wallets as XrplWallet[] ?? wallets.ToArray();
        if (autofill)
        {
            tx = await client.Autofill(tx, signersCount: xrplWallets.Length, cancellationToken: cancellationToken);
        }

        var signed = xrplWallets.Select(c => c.Sign(tx, multisign: true).TxBlob).ToArray();
        var combined = Signer.Multisign(signed);
        var txRes = XrplBinaryCodec.Decode(combined);

        var response = await client.SubmitRequest(combined, failHard: failHard, cancellationToken: cancellationToken);
        return response;
    }

    /// <summary>
    /// Encodes and submits a Batch signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="txJson">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMultiBatch(
        this IXrplClient client,
        Dictionary<string, object> txJson,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var walletList = wallets as IList<XrplWallet> ?? wallets.ToList();
        if (walletList.Count == 0)
        {
            throw new ValidationException("No wallets provided");
        } 
        var walletByAddr = walletList.ToDictionary(w => w.ClassicAddress, StringComparer.Ordinal);

        if (!txJson.TryGetValue("Account", out var mainAccObj))
        {
            throw new ValidationException("Main account not defined in tx json");
        }    
        var mainAcc = (string)mainAccObj;

        if (autofill)
        {
            txJson = await client.Autofill(txJson, signersCount: walletList.Count, cancellationToken: cancellationToken);
        }

        var root = JsonNode.Parse(JsonSerializer.Serialize(txJson, XrplJsonOptions.Default))?.AsObject();
        var rawArray = root["RawTransactions"]?.AsArray() ?? new JsonArray();

        // 1) signatures of the owners of the inner transactions
        var partialBlobs = new List<string>();
        foreach (var entry in rawArray.Where(n => n is JsonObject).Select(n => n!.AsObject()))
        {
            var acct = entry["RawTransaction"]?["Account"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(acct))
            {
                throw new ValidationException("Inner tx missing Account");
            }
            if (mainAcc == acct)
            {
                // No inner-batch signature for the creator's account: only the outer one
                continue;
            }

            // account_info, for the list of signers
            var ai = await client.AccountInfo(
                new AccountInfoRequest(acct)
                {
                    SignerLists = true
                }, cancellationToken).Typed();
            // Both are read below to decide how this account signs, and a response missing either
            // is malformed rather than a signer with no flags: the master-key check would otherwise
            // throw NullReferenceException here, and the RegularKey lookup further down would do
            // the same. Failing with the account named beats either.
            if (ai.AccountData is null)
            {
                throw new ValidationException($"account_info response for '{acct}' did not include account_data.");
            }

            if (ai.AccountFlags is null)
            {
                throw new ValidationException($"account_info response for '{acct}' did not include the account's flags.");
            }

            var hasSL = ai.SignerLists?.Length > 0 && ai.AccountFlags.DisableMasterKey;
            if (hasSL)
            {
                var sl = ai.SignerLists[0];
                var (picked, sum, quorum) = BatchSigningHelper.PickWalletsForQuorum(sl, walletByAddr);

                if (sum < quorum)
                {
                    throw new ValidationException($"Not enough signer wallets for multisig account {acct}.");
                }
                foreach (var wlt in picked)
                {
                    partialBlobs.Add(wlt.SignAsBatchPart(txJson, multisign: true, signingFor: acct).TxBlob);
                }
            }
            else
            {
                if (walletByAddr.TryGetValue(acct, out var owner) && !ai.AccountFlags.DisableMasterKey)
                    partialBlobs.Add(owner.SignAsBatchPart(txJson, multisign: false, signingFor: acct).TxBlob);
                else if (!string.IsNullOrEmpty(ai.AccountData.RegularKey) &&
                         walletByAddr.TryGetValue(ai.AccountData.RegularKey, out var rk))
                    partialBlobs.Add(rk.SignAsBatchPart(txJson, multisign: false, signingFor: acct).TxBlob);
                else
                    throw new ValidationException($"Wallet for account {acct} (or its RegularKey) not provided");
            }
        }

        // 2) splice the inner signatures together
        var combined = XrplWallet.CombineBatchSigners(partialBlobs.ToArray());
        var combinedJson = JsonNode.Parse(XrplBinaryCodec.Decode(combined.TxBlob).ToJsonString())?.AsObject();
        // 3) root signature: single-sig OR multi-sig, depending on whether the root has a SignerList
        var aiRoot = await client.AccountInfo(
            new AccountInfoRequest(mainAcc)
            {
                SignerLists = true
            }, cancellationToken).Typed();
        // Same shape as the per-account check above: the master-key flag decides how the root
        // signs, and a response without flags is malformed rather than an account with none.
        if (aiRoot.AccountFlags is null)
        {
            throw new ValidationException($"account_info response for '{mainAcc}' did not include the account's flags.");
        }

        var rootHasSL = aiRoot.SignerLists?.Length > 0 && aiRoot.AccountFlags.DisableMasterKey;
        if (!rootHasSL)
        {
            // the ordinary signature of the fee payer, who must be among wallets
            if (!walletByAddr.TryGetValue(mainAcc, out var main))
                throw new ValidationException($"Main account {mainAcc} not found in provided wallets");
            var final = main.Sign(JsonSerializer.Deserialize<Dictionary<string, object>>(combinedJson.ToJsonString(), XrplJsonOptions.Default));
            var submit = await client.SubmitRequest(final.TxBlob, failHard, cancellationToken);
            //var txRes = XrplBinaryCodec.Decode(submit.TxBlob);
            return submit;
        }
        else
        {
            // root multi-signature: take from wallets only those in SignerList(main)
            var sl = aiRoot.SignerLists[0];
            var (picked, sum, quorum) = BatchSigningHelper.PickWalletsForQuorum(sl, walletByAddr);

            if (sum < quorum) throw new ValidationException($"Not enough signer wallets for root multisig {mainAcc}.");

            //// root multi-signature: an empty SPK is required, and no TxnSignature
            //combinedJson.Remove("TxnSignature");
            //combinedJson["SigningPubKey"] = "";

            var msBlobs = picked.Select(w => w.Sign(
                JsonSerializer.Deserialize<Dictionary<string, object>>(combinedJson.ToJsonString(), XrplJsonOptions.Default),
                multisign: true).TxBlob).ToArray();
            var msCombined = Signer.Multisign(msBlobs);
            //var txRes = XrplBinaryCodec.Decode(msCombined);

            var submit = await client.SubmitRequest(msCombined, failHard, cancellationToken);
            return submit;
        }
    }

    /// <summary>
    /// Encodes and submits a Batch signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMultiBatch(
    this IXrplClient client,
    Batch tx,
    IEnumerable<XrplWallet> wallets,
    bool autofill = true,
    bool failHard = false,
    CancellationToken cancellationToken = default)
    {
        var json = tx.ToJson();
        var txJson = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default)
                    ?? throw new ValidationException("Failed to deserialize tx json");

        var response = await client.SubmitMultiBatch(txJson, wallets, autofill, failHard, cancellationToken);
        return response;
    }

    /// <summary>
    /// The core of reliable submission: polls until the transaction is in a validated ledger, or
    /// it is certain it never will be, or that cannot be told.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provisional result of the submission is final only for a <c>tem</c>, which no ledger
    /// accepts. Without <paramref name="failHard"/> rippled holds a locally submitted transaction
    /// whatever its <c>ter</c>, <c>tef</c> or <c>tel</c>, retries it in later ledgers until its
    /// <c>LastLedgerSequence</c>, and relays it when it is not in full mode; a <c>tec</c> was
    /// applied to the open ledger. All of those are waited for. Under <paramref name="failHard"/>
    /// a result other than <c>tesSUCCESS</c> or <c>terQUEUED</c> is neither applied, held nor
    /// relayed, so the transaction can reach a ledger only through an earlier submission of the
    /// same blob: it is looked up once and followed if found. <c>tefALREADY</c>,
    /// <c>tefPAST_SEQ</c> and <c>tefNO_TICKET</c> are the exception, since that earlier
    /// submission may be what they report, and are waited for as well.
    /// </para>
    /// <para>
    /// The lookup asks for the range <paramref name="minLedger"/> to
    /// <paramref name="lastLedgerSequence"/>. A <c>txnNotFound</c> with <c>searched_all</c> true
    /// proves that the transaction is in none of those ledgers and that all of them are validated.
    /// rippled counts only the ledgers that hold a transaction, though, so a range with an empty
    /// ledger - common on a quiet network - never gets that answer. Once the validated ledger is
    /// past <paramref name="lastLedgerSequence"/>, the ledgers of the range are then read one by
    /// one: a validated ledger whose transactions do not include the hash proves the same for that
    /// ledger, from whichever server answers.
    /// </para>
    /// <para>
    /// Without either proof the wait goes on, and gives up as unknown once the validated ledger is
    /// <see cref="LedgersPastExpiryBeforeUnknown"/> past the last one. A transaction applied
    /// before <paramref name="minLedger"/> by an earlier submission is found by its hash only if the
    /// server holds that ledger. A server whose validated ledger stops advancing keeps the wait
    /// going until <paramref name="cancellationToken"/> ends it.
    /// </para>
    /// </remarks>
    internal static async Task<TransactionSummary> WaitForFinalTransactionOutcome(
        Func<TxRequest, CancellationToken, Task<TransactionSummary>> lookup,
        Func<CancellationToken, Task<uint>> validatedLedgerIndex,
        Func<uint, CancellationToken, Task<bool?>> ledgerHoldsTransaction,
        string txHash,
        uint lastLedgerSequence,
        uint minLedger,
        string submissionResult,
        bool failHard,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        try
        {
            return await PollForOutcome(
                lookup, validatedLedgerIndex, ledgerHoldsTransaction, txHash, lastLedgerSequence, minLedger,
                submissionResult, failHard, pollInterval, cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested && ex is not TransactionWaitCanceledException)
        {
            // Everything the wait does follows the submission: the transaction may be on the network.
            throw Canceled(ex, txHash, lastLedgerSequence, minLedger, submissionResult, cancellationToken);
        }
    }

    private static async Task<TransactionSummary> PollForOutcome(
        Func<TxRequest, CancellationToken, Task<TransactionSummary>> lookup,
        Func<CancellationToken, Task<uint>> validatedLedgerIndex,
        Func<uint, CancellationToken, Task<bool?>> ledgerHoldsTransaction,
        string txHash,
        uint lastLedgerSequence,
        uint minLedger,
        string submissionResult,
        bool failHard,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        if (submissionResult != null && submissionResult.StartsWith("tem", StringComparison.Ordinal))
        {
            throw new TransactionFailedException(
                $"Final tx result is not success: {submissionResult}",
                engineResult: submissionResult,
                hash: txHash);
        }

        bool notHeld = IsNeitherAppliedNorHeld(submissionResult, failHard);
        bool rangeReadable = minLedger <= lastLedgerSequence && lastLedgerSequence - minLedger < MaxLedgersToRead;

        // Ledgers below this one are proven not to hold the transaction; a validated ledger never
        // changes, so none of them is read twice.
        uint firstUnread = minLedger;

        uint lastValidated = 0;
        int stalledPolls = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A transaction refused outright is looked up at once: waiting cannot bring it in.
            if (!notHeld)
                await Task.Delay(pollInterval, cancellationToken);

            TransactionSummary found = null;
            bool? searchedAll = null;
            try
            {
                found = await lookup(LookupRequest(txHash, minLedger, lastLedgerSequence), cancellationToken);
            }
            catch (RippledException ex) when (ex.Response?.Error == XrplErrorCodes.TxnNotFound)
            {
                searchedAll = ex.Response.SearchedAll;
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                throw Unknown($"Looking up transaction {txHash} failed: {ex.Message}", ex);
            }

            if (found != null)
            {
                if (found.Validated == true)
                    return Final(found, txHash);

                if (notHeld)
                {
                    // In a ledger or the node's cache, whatever the submission said: follow it.
                    notHeld = false;
                    continue;
                }
            }
            else if (notHeld)
            {
                // Neither applied nor held: under fail_hard rippled discards even a tec, fee and all.
                throw new TransactionFailedException(
                    $"Final tx result is not success: {submissionResult}",
                    engineResult: submissionResult,
                    hash: txHash,
                    reachedLedger: false);
            }
            else if (searchedAll == true)
            {
                throw NeverApplied($"it is in none of ledgers {minLedger} to {lastLedgerSequence}, all of them validated (searched_all)", searchedAllProof: true);
            }

            uint validated;
            try
            {
                validated = await validatedLedgerIndex(cancellationToken);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                throw Unknown($"Reading the validated ledger while waiting for transaction {txHash} failed: {ex.Message}", ex);
            }

            // A server that keeps answering while its validated ledger stands still - a lost
            // validator quorum - would otherwise be polled for ever.
            stalledPolls = validated == lastValidated ? stalledPolls + 1 : 0;
            lastValidated = validated;
            if (stalledPolls >= ValidatedStallPollsBeforeUnknown)
            {
                throw Unknown(
                    $"The server's validated ledger has stayed at {validated} for {stalledPolls} polls while waiting for transaction {txHash}.",
                    null);
            }

            if (validated <= lastLedgerSequence)
                continue;

            // A transaction found unvalidated counts too: a held one stays in the node's cache
            // after its LastLedgerSequence, reported unvalidated, though no ledger can take it.
            bool? holds = null;
            if (rangeReadable)
            {
                try
                {
                    while (firstUnread <= lastLedgerSequence)
                    {
                        holds = await ledgerHoldsTransaction(firstUnread, cancellationToken);
                        if (holds != false)
                            break;
                        firstUnread++;
                    }
                }
                catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
                {
                    throw Unknown($"Reading ledger {firstUnread} while waiting for transaction {txHash} failed: {ex.Message}", ex);
                }

                if (firstUnread > lastLedgerSequence)
                {
                    throw NeverApplied(
                        $"it is in none of ledgers {minLedger} to {lastLedgerSequence}, each read validated",
                        searchedAllProof: false);
                }
            }

            if (validated > lastLedgerSequence + LedgersPastExpiryBeforeUnknown)
            {
                throw Unknown(
                    holds == true
                        ? $"Validated ledger {firstUnread} holds transaction {txHash}, but looking it up does not return it."
                        : rangeReadable
                            ? $"Transaction {txHash} is not validated, and ledger {firstUnread}, between {minLedger} and its LastLedgerSequence {lastLedgerSequence}, is not available validated from the server."
                            : $"Transaction {txHash} is not validated, and ledgers {minLedger} to {lastLedgerSequence} are too many to read one by one.",
                    null);
            }
        }

        // The range proves the transaction never applied only when it can hold every ledger the
        // transaction could be in. A result saying the blob or its sequence was already used
        // points at an earlier submission, possibly in a ledger before the range.
        Exception NeverApplied(string reason, bool searchedAllProof) =>
            MayReportAnEarlierCopy(submissionResult)
                ? Unknown(
                    $"Transaction {txHash} is in none of ledgers {minLedger} to {lastLedgerSequence}, but {submissionResult} says this blob or its sequence " +
                    "was already used, possibly by an earlier submission applied before them.",
                    null)
                : new TransactionExpiredException(
                    $"Transaction {txHash} has expired: {reason}. Preliminary result: {submissionResult ?? "none"}",
                    txHash,
                    lastLedgerSequence,
                    minLedger,
                    submissionResult,
                    searchedAllProof);

        TransactionOutcomeUnknownException Unknown(string message, Exception inner) =>
            new TransactionOutcomeUnknownException(
                message + $" Preliminary result: {submissionResult ?? "none"}.",
                txHash,
                lastLedgerSequence,
                minLedger,
                submissionResult,
                inner);
    }

    /// <summary>
    /// How many ledgers past <c>LastLedgerSequence</c> the validated ledger may get while neither
    /// proof is in: the server fills recent gaps on its own, a lasting one is reported as unknown.
    /// </summary>
    internal const uint LedgersPastExpiryBeforeUnknown = 5;

    /// <summary>
    /// How many polls in a row the validated ledger may stay where it is before the wait gives up
    /// as unknown: about a minute, where a healthy network validates a ledger every few seconds.
    /// </summary>
    internal const int ValidatedStallPollsBeforeUnknown = 60;

    /// <summary>
    /// The most ledgers the range is read one by one for. Autofill leaves 20 between the ledger
    /// validated at submission and <c>LastLedgerSequence</c>; a far wider window is left to
    /// <c>searched_all</c>.
    /// </summary>
    internal const uint MaxLedgersToRead = 256;

    /// <summary>The widest <c>min_ledger</c>..<c>max_ledger</c> range rippled searches (<c>kMaxRange</c>).</summary>
    private const uint MaxLookupRange = 1000;

    private static TxRequest LookupRequest(string txHash, uint minLedger, uint lastLedgerSequence)
    {
        TxRequest request = new TxRequest(txHash) { ApiVersion = 2 };
        if (minLedger <= lastLedgerSequence && lastLedgerSequence - minLedger <= MaxLookupRange)
        {
            request.MinLedger = minLedger;
            request.MaxLedger = lastLedgerSequence;
        }

        return request;
    }

    private static TransactionSummary Final(TransactionSummary validated, string txHash)
    {
        string txResult = validated.Meta?.TransactionResult;
        if (txResult != null && txResult != "tesSUCCESS")
        {
            // Applied to a ledger: the fee was taken and there is a transaction to look up, so the
            // summary travels with the failure.
            throw new TransactionFailedException(
                $"Final tx result is not success: {txResult}",
                engineResult: txResult,
                hash: txHash,
                result: validated);
        }

        return validated;
    }

    /// <summary>
    /// Whether, under <c>fail_hard</c>, the node neither applied, held nor relayed the transaction:
    /// any result but <c>tesSUCCESS</c> and <c>terQUEUED</c>, except the ones an earlier submission
    /// of the same blob can cause.
    /// </summary>
    private static bool IsNeitherAppliedNorHeld(string submissionResult, bool failHard) =>
        failHard
        && submissionResult != null
        && submissionResult != "tesSUCCESS"
        && submissionResult != "terQUEUED"
        && !MayReportAnEarlierCopy(submissionResult);

    /// <summary>
    /// Whether the result says this blob, or the sequence or ticket it uses, was already consumed -
    /// which an earlier submission of the same blob can have done.
    /// </summary>
    private static bool MayReportAnEarlierCopy(string submissionResult) =>
        submissionResult is "tefALREADY" or "tefPAST_SEQ" or "tefNO_TICKET";

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Initializes a transaction for a submit request
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>The signed transaction blob and the transaction it was built from.</returns>
    public static async Task<(string txBlob, Dictionary<string, object> tx)> GetSignedTx(
        this IXrplClient client,
        Dictionary<string, object> transaction,
        bool autofill = false,
        XrplWallet? wallet = null,
        CancellationToken cancellationToken = default,
        bool sponsorPreCheck = true
    )
    {
        if (wallet == null)
        {
            throw new ValidationException("Wallet must be provided when submitting an unsigned transaction");
        }

        var tx = transaction;

        bool isSponsored = tx.TryGetValue("Sponsor", out var sponsorField) && sponsorField is string;
        string? sponsorAddress = isSponsored ? (string)sponsorField : null;
        // The main signature is either a single TxnSignature or multisig Signers
        bool hasMainSignature =
            (tx.TryGetValue("TxnSignature", out var mainSig) && mainSig is string { Length: > 0 }) ||
            (tx.TryGetValue("Signers", out var mainSigners) && mainSigners is not null);
        // ANY signature material freezes the body: a co-signature was computed
        // over these exact fields, so autofill would silently invalidate it
        bool hasAnySignature = hasMainSignature ||
            (tx.TryGetValue("SponsorSignature", out var sponsorSigMaterial) && sponsorSigMaterial is not null) ||
            (tx.TryGetValue("CounterpartySignature", out var counterpartySigMaterial) && counterpartySigMaterial is not null);

        if (autofill && !hasAnySignature)
        {
            tx = await client.Autofill(tx, cancellationToken: cancellationToken);
        }

        if (isSponsored && string.Equals(sponsorAddress, wallet.ClassicAddress, StringComparison.Ordinal))
        {
            // The sponsor finalizes: the sponsee's signature must already be present —
            // the sponsor cannot produce it
            if (!hasMainSignature)
            {
                throw new ValidationException("Sponsored transaction is not signed by all participants: the submitter's TxnSignature is missing and the sponsor cannot produce it.");
            }

            string mainBlob = XrplBinaryCodec.Encode(tx);
            SignatureResult sponsorPart = wallet.Sign(tx, multisign: false); // routes to the sponsor path
            SignatureResult final = SignatureComposer.ComposeSignatures(new[] { mainBlob, sponsorPart.TxBlob });
            return (final.TxBlob, tx);
        }

        if (isSponsored && sponsorPreCheck)
        {
            bool hasSponsorSignature = tx.TryGetValue("SponsorSignature", out var sponsorSigValue) && sponsorSigValue is not null;
            if (!hasSponsorSignature &&
                await IsSponsorSignatureRequired(client, tx, sponsorAddress!, cancellationToken))
            {
                throw new ValidationException("Sponsored transaction is not signed by all participants: the sponsorship requires the sponsor's co-signature (SponsorSignature) for this coverage.");
            }
        }

        return (wallet.Sign(tx, multisign: false).TxBlob, tx);
    }

    /// <summary>
    /// Submits a sponsored transaction with both keys available locally (the
    /// V1 flow in one call): autofills, prepares, co-signs with the sponsee
    /// and the sponsor, submits and waits for the final outcome.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction carrying Sponsor/SponsorFlags.</param>
    /// <param name="sponseeWallet">The submitting account's wallet.</param>
    /// <param name="sponsorWallet">The sponsor's wallet (must match tx.Sponsor).</param>
    /// <param name="autofill">If true, autofill the transaction.</param>
    /// <param name="failHard">If true, do not retry or relay on local failure.</param>
    public static async Task<TransactionSummary> SubmitAndWaitSponsored(
        this IXrplClient client,
        Dictionary<string, object> transaction,
        XrplWallet sponseeWallet,
        XrplWallet sponsorWallet,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        if (sponseeWallet is null || sponsorWallet is null)
        {
            throw new ValidationException("Both the sponsee and the sponsor wallets must be provided.");
        }

        var tx = transaction;
        if (autofill)
        {
            tx = await client.Autofill(tx, cancellationToken: cancellationToken);
        }

        JsonObject prepared = JsonNode.Parse(JsonSerializer.Serialize(tx, XrplJsonOptions.Default))?.AsObject()
            ?? throw new ValidationException("Failed to serialize transaction to JSON");
        prepared["SigningPubKey"] = sponseeWallet.PublicKey;
        prepared.Remove("SponsorSignature");
        prepared.Remove("TxnSignature");

        var signed = SponsorSigningHelper.SignSponsored(prepared, sponseeWallet, sponsorWallet);
        return await client.SubmitRequestAndWait(signed.TxBlob, failHard, cancellationToken);
    }

    /// <summary>
    /// Submits a sponsored transaction with both keys available locally (the
    /// V1 flow in one call).
    /// </summary>
    public static Task<TransactionSummary> SubmitAndWaitSponsored(
        this IXrplClient client,
        ITransactionRequest transaction,
        XrplWallet sponseeWallet,
        XrplWallet sponsorWallet,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default) =>
        SubmitAndWaitSponsored(client, transaction.ToDictionary(), sponseeWallet, sponsorWallet, autofill, failHard, cancellationToken);

    /// <summary>
    /// Checks the Sponsorship ledger object's require-sign flags against the
    /// transaction's SponsorFlags coverage. Returns false when the relationship
    /// does not exist (the node will reject the transaction with a clear code).
    /// </summary>
    internal static async Task<bool> IsSponsorSignatureRequired(
        IXrplClient client,
        Dictionary<string, object> tx,
        string sponsorAddress,
        CancellationToken cancellationToken = default)
    {
        // The sponsorship is the one with the transaction's initiator (rippled STTx::getInitiator): the delegate when
        // the transaction has one.
        string initiator = tx.TryGetValue("Delegate", out var delegateField) && delegateField is string { Length: > 0 } @delegate
            ? @delegate
            : tx.TryGetValue("Account", out var accountField) ? accountField as string : null;
        if (initiator is null)
            return false;
        uint coverage = tx.TryGetValue("SponsorFlags", out var flagsField) &&
                        Models.Transactions.Common.TryGetUInt32(flagsField, out uint parsed)
            ? parsed
            : 0;
        if (coverage == 0)
            return false;

        // A sponsor can have more sponsorships than one page holds, and absence is only an answer after the last page.
        Models.Ledger.LOSponsorship sponsorship = null;
        object marker = null;
        do
        {
            var request = new Models.Methods.AccountObjectsRequest(sponsorAddress)
            {
                Type = Models.LedgerEntryType.Sponsorship,
                Marker = marker,
            };
            var response = await client.AccountObjects(request, cancellationToken).Typed().ConfigureAwait(false);
            sponsorship = response?.AccountObjectList?
                .OfType<Models.Ledger.LOSponsorship>()
                .FirstOrDefault(s => string.Equals(s.Sponsee, initiator, StringComparison.Ordinal));
            marker = response?.Marker;
        }
        while (sponsorship is null && marker is not null);

        if (sponsorship is null)
            return false;

        // A missing Flags value is equivalent to "no flags set" for a bitmask check, so false is the correct
        // (not a fabricated) default here - unlike numeric fields (Sequence, balances) where 0 would be a lie.
        bool requireForFee = sponsorship.Flags?.HasFlag(Models.Ledger.SponsorshipFlags.lsfSponsorshipRequireSignForFee) ?? false;
        bool requireForReserve = sponsorship.Flags?.HasFlag(Models.Ledger.SponsorshipFlags.lsfSponsorshipRequireSignForReserve) ?? false;

        return ((coverage & (uint)SponsorCoverage.spfSponsorFee) != 0 && requireForFee)
            || ((coverage & (uint)SponsorCoverage.spfSponsorReserve) != 0 && requireForReserve);
    }

    public static bool IsSigned(object transaction)
    {
        if (transaction is Dictionary<string, object> { } tx)
        {
            return (tx.TryGetValue(key: "SigningPubKey", value: out var SigningPubKey) && SigningPubKey is not null) ||
                   (tx.TryGetValue(key: "TxnSignature", value: out var TxnSignature) && TxnSignature is not null);
        }
        else
        {
            JsonObject json = ToJsonObject(transaction);
            if (json == null) return false;
            return (json.TryGetPropertyValue("SigningPubKey", out var SigningPubKey) &&
                    !string.IsNullOrWhiteSpace(SigningPubKey?.ToString())) ||
                   (json.TryGetPropertyValue("TxnSignature", out var TxnSignature) &&
                    !string.IsNullOrWhiteSpace(TxnSignature?.ToString()));
        }
    }

    /// <summary>
    /// checks if there is a LastLedgerSequence as a part of the transaction
    /// </summary>
    /// <param name="transaction">tx</param>
    /// <returns></returns>
    public static uint? GetLastLedgerSequence(object transaction) => LedgerSequenceHelper.GetLastLedgerSequence(transaction);

    /// <summary>
    /// checks if the transaction is an AccountDelete transaction
    /// </summary>
    /// <param name="transaction">tx</param>
    /// <returns></returns>
    public static bool IsAccountDelete(object transaction)
    {
        if (transaction is Dictionary<string, object> { } tx)
        {
            return tx.TryGetValue(key: "TransactionType", value: out var TransactionType) &&
                   $"{TransactionType}" == "AccountDelete";
        }
        else if (transaction is TransactionRequest txc)
        {
            return txc.TransactionType == TransactionType.AccountDelete;
        }
        else
        {
            JsonObject json = ToJsonObject(transaction);
            if (json == null) return false;

            return json.TryGetPropertyValue("TransactionType", out var TransactionType) &&
                   TransactionType?.ToString() == "AccountDelete";
        }
    }

    // XrplBinaryCodec.Encode returns the serialized blob as hex, which is not JSON, so the fields are
    // read from the object's own JSON form instead.
    private static JsonObject ToJsonObject(object transaction)
    {
        if (transaction is JsonObject jsonObject)
            return jsonObject;

        return transaction is null
            ? null
            : JsonSerializer.SerializeToNode(transaction, transaction.GetType(), XrplJsonOptions.Default) as JsonObject;
    }
}