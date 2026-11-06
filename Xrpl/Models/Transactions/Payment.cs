using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;
using Xrpl.Models.Utils;


// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/payment.ts

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Enum representing values for Payment Transaction Flags.
    /// </summary>
    [Flags]
    public enum PaymentFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,

        /// <summary>
        /// Do not use the default path;<br/>
        /// only use paths included in the Paths field.<br/>
        /// This is intended to force the transaction to take arbitrage opportunities.<br/>
        /// Most clients do not need this.
        /// </summary>
        tfNoDirectRipple = 65536,
        /// <summary>
        /// If the specified Amount cannot be sent without spending more than SendMax, reduce the received amount instead of failing outright.<br/>
        /// See Partial Payments for more details.
        /// </summary>
        tfPartialPayment = 131072,
        /// <summary>
        /// Only take paths where all the conversions have an input:output ratio that is equal or better than the ratio of Amount:SendMax.<br/>
        /// See Limit Quality for details.
        /// </summary>
        tfLimitQuality = 262144,

        /// <summary>
        /// The payment's <c>Account</c> sponsors the reserve of the account this payment creates
        /// (XLS-68); the new account's <c>Sponsor</c> names it, not the transaction's <c>Sponsor</c>.
        /// </summary>
        /// <remarks>
        /// rippled declares it in the Payment block of TxFlags.h as 0x00080000. The rest of
        /// XLS-68 was already modelled here - the Sponsor fields, SponsorshipSet, SponsorshipFlags
        /// - and only this flag was missing, which is what a conformance check over TxFlags.h
        /// would have caught. No such check exists: ledger flags are verified against
        /// LedgerFormats.h, transaction flags against nothing at all.
        /// </remarks>
        tfSponsorCreatedAccount = 524288,
    }

    /// <inheritdoc cref="IPayment" />
    public class Payment : TransactionRequest, IPayment, IDestination
    {
        public Payment()
        {
            TransactionType = TransactionType.Payment;
        }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        /// <remarks>
        /// API v2 renames the ledger's Amount field to DeliverMax on the wire and omits Amount entirely.
        /// System.Text.Json skips non-public members unless they carry [JsonInclude], so this attribute
        /// is what keeps the alias wired up — without it Amount silently stays null on every v2 payload.
        /// <para>
        /// The property is deliberately set-only, so DeliverMax is never written back out, unlike its
        /// counterpart on <see cref="PaymentResponse"/>. <b>Do not "fix" this asymmetry by copying
        /// PaymentResponse's read/write alias pair here</b> — it would be a signing-safety regression, not
        /// a cleanup. The reason: <c>DeliverMax</c> has no entry of its own in
        /// <c>Base/Xrpl.BinaryCodec/Enums/definitions.json</c> — checked directly: <c>Amount</c>,
        /// <c>DeliverMin</c> and <c>SendMax</c> are all present there, <c>DeliverMax</c> is not, because it
        /// is a JSON API v2 presentation-layer rename with no binary field code of its own. A
        /// <see cref="PaymentResponse"/> is read-only display data, so preserving the wire name it arrived
        /// under is safe and correct for a reconciliation UI. This class instead feeds
        /// <c>ToJson()</c> → <c>EncodeForSigning</c> (see <c>XrplWallet.Sign</c>, which calls
        /// <c>ITransactionRequest.ToJson()</c> before handing the dictionary to the binary codec): the
        /// codec looks fields up by name in <c>definitions.json</c>, so an object that re-emitted
        /// "DeliverMax" hands the codec a name it cannot resolve. What happens then depends on the
        /// entry point, and both outcomes are bad — verified by execution:
        /// <c>EncodeForSigning</c> raises <c>InvalidJsonException: unknown field DeliverMax</c>, so
        /// signing fails outright, while a direct <c>XrplBinaryCodec.Encode</c> drops the member
        /// silently and yields a blob with no amount in it at all. On the <c>Sign</c> path the loud
        /// failure comes first, since the signature is computed before the final blob is built — but
        /// "it throws instead" is not a reason to relax the rule, and nothing stops a caller from
        /// reaching <c>Encode</c> directly. So Amount is always written here, regardless of which
        /// name it came in under.
        /// </para>
        /// </remarks>
        [JsonInclude]
        [JsonPropertyName("DeliverMax")]
        [JsonConverter(typeof(CurrencyConverter))]
        private Currency? DeliverMax
        {
            set => Amount = value;
        }

        /// <inheritdoc />
        public string Destination { get; set; }

        /// <inheritdoc />
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        public new PaymentFlags? Flags
        {
            get => base.Flags.HasValue ? (PaymentFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
        /// <inheritdoc />
        public string InvoiceID { get; set; }

        /// <inheritdoc />
        public List<List<PathStep>> Paths { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DeliverMin { get; set; }

        /// <inheritdoc />
        public string DomainID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CredentialIDs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> CredentialIDs { get; set; }
    }

    /// <summary>
    /// A Payment transaction represents a transfer of value from one account to  another.
    /// </summary>
    /// <code>
    /// ```typescript
    /// const partialPayment: Payment =
    /// {
    ///         TransactionType: 'Payment',
    ///         Account: 'rM9WCfJU6udpFkvKThRaFHDMsp7L8rpgN',
    ///         Amount:{
    ///         currency: 'FOO',
    ///         value: '4000',
    ///         issuer: 'rPzwM2JfCSDjhbesdTCqFjWWdK7eFtTwZz',
    ///     },
    ///         Destination: 'rPzwM2JfCSDjhbesdTCqFjWWdK7eFtTwZz',
    ///         Flags:{
    ///         tfPartialPayment: true
    ///     }
    /// }
    /// ```
    /// </code>
    public interface IPayment : ITransactionCommon, IDestination
    {
        /// <summary>
        /// API v1
        /// The amount of currency to deliver.<br/>
        /// For non-XRP amounts, the nested field names MUST be lower-case.<br/>
        /// If the tfPartialPayment flag is set, deliver up to this amount instead.
        /// Alias to DeliverMax <br/>
        /// API v2: The maximum amount of currency to deliver.<br/>
        /// Partial payments can deliver less than this amount and still succeed;<br/>
        /// other payments fail unless they deliver the exact amount.
        /// </summary>
        Currency? Amount { get; set; }

        /// <summary>
        /// Minimum amount of destination currency this transaction should deliver.<br/>
        /// Only valid if this is a partial payment.<br/>
        /// For non-XRP amounts, the nested field names are lower-case.
        /// </summary>
        Currency? DeliverMin { get; set; }
        /// <summary>
        /// The unique address of the account receiving the payment.
        /// </summary>
        string Destination { get; set; }
        /// <summary>
        /// Arbitrary tag that identifies the reason for the payment to the destination, or a hosted recipient to pay.
        /// </summary>
        uint? DestinationTag { get; set; }
        /// <summary>
        /// Payment Transaction Flags
        /// </summary>
        new PaymentFlags? Flags { get; set; }
        /// <summary>
        /// Arbitrary 256-bit hash representing a specific reason or identifier for this payment.
        /// </summary>
        string InvoiceID { get; set; }
        /// <summary>
        /// Array of payment paths to be used for this transaction.<br/>
        /// Must be omitted for XRP-to-XRP transactions.
        /// </summary>
        List<List<PathStep>> Paths { get; set; }
        /// <summary>
        /// Highest amount of source currency this transaction is allowed to cost, including transfer fees, exchange rates, and slippage.<br/>
        /// Does not include the XRP destroyed as a cost for submitting the transaction.<br/>
        /// For non-XRP amounts, the nested field names MUST be lower-case.<br/>
        /// Must be supplied for cross-currency/cross-issue payments.<br/>
        /// Must be omitted for XRP-to-XRP Payments.
        /// </summary>
        Currency SendMax { get; set; }
        /// <summary>
        /// The domain the sender intends to use for cross-currency payments through the permissioned DEX.
        /// Both sender and destination must be part of this domain if it interacts with the DEX.
        /// </summary>
        string DomainID { get; set; }

        /// <summary>
        /// (Optional) Set of Credentials (object IDs, hex 64-char each) to authorize a deposit
        /// when the destination account requires Deposit Authorization with credential-based preauth (XLS-70).
        /// Maximum 8 entries.
        /// </summary>
        List<string> CredentialIDs { get; set; }
    }

    /// <inheritdoc cref="IPayment" />
    public class PaymentResponse : TransactionResponse, IPayment, IDestination
    {
        /// <summary>
        /// True once a value has been assigned through the <see cref="AmountAlias"/> setter below —
        /// i.e. the node sent (or an earlier deserialize pass already saw) the field under its API v1
        /// name "Amount". Independent of <see cref="_receivedAsDeliverMax"/>: rippled's <c>tx</c>
        /// method with <c>api_version: 1</c> sends BOTH "Amount" and "DeliverMax" for the same
        /// transaction (confirmed live against mainnet — see
        /// <c>Fixtures/Responses/tx_v1_raw.json</c>), and <see cref="System.Text.Json"/> calls both
        /// property setters in the order the members appear in the source JSON. A single bool here
        /// could only remember whichever setter ran last, so the earlier one's presence would be
        /// lost — exactly the defect this pair of flags exists to avoid.
        /// </summary>
        private bool _receivedAsAmount;

        /// <summary>
        /// True once a value has been assigned through the <see cref="DeliverMax"/> setter below —
        /// i.e. the node sent the field under its API v2 name "DeliverMax". See
        /// <see cref="_receivedAsAmount"/> for why this is a second, independent flag rather than a
        /// single "which one" bool.
        /// </summary>
        private bool _receivedAsDeliverMax;

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// The single value callers read, regardless of whether the node sent it as "Amount" (API v1),
        /// "DeliverMax" (API v2), or both (API v1, confirmed live - see
        /// <see cref="_receivedAsAmount"/>). Excluded from JSON directly: <see cref="AmountAlias"/> and
        /// <see cref="DeliverMax"/> below own the wire representation, so every field name a value came
        /// in under is one it goes back out under — callers never have to guess which one(s) fired.
        /// </para>
        /// <para>
        /// One value behind two names, which is exact for every response rippled actually produces:
        /// v2 sends DeliverMax alone, v1 sends both carrying the same amount. It is not exact if the
        /// two ever disagree - the setters run in document order, the later one wins, and both names
        /// then re-serialize with that single value. Measured on a hand-built payload where the node
        /// "sent" Amount 1000000 and DeliverMax 999, the round trip emits 999 under both names, so
        /// Amount reads as a value that was never sent. rippled cannot produce that response - the v1
        /// path duplicates one field rather than computing two - so this is a property of malformed
        /// or hostile input, not of the protocol. <c>Raw</c> keeps the truth either way; a consumer
        /// that must detect tampering should compare against it rather than trusting this projection.
        /// </para>
        /// </remarks>
        [JsonIgnore]
        public Currency Amount { get; set; }

        /// <summary>
        /// JSON view of <see cref="Amount"/> under its API v1 wire name "Amount". Serialized whenever
        /// the value arrived under this name, or under neither name (an object assembled by
        /// application code defaults to "Amount") - i.e. whenever it was NOT received exclusively as
        /// DeliverMax. That "received both" case is what keeps this alias and <see cref="DeliverMax"/>
        /// both emitting instead of one silently winning over the other.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        private Currency AmountAlias
        {
            get => _receivedAsAmount || !_receivedAsDeliverMax ? Amount : null;
            set
            {
                Amount = value;
                _receivedAsAmount = true;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// API v2 renames the ledger's Amount field to DeliverMax on the wire and omits Amount entirely.
        /// System.Text.Json skips non-public members unless they carry [JsonInclude], so this attribute
        /// is what keeps the alias wired up — without it Amount silently stays null on every v2 payload
        /// (account_tx, tx with api_version 2, subscription streams).
        /// Serialized only when the node actually sent "DeliverMax" - a value that arrived as DeliverMax
        /// is written back out as DeliverMax, not silently renamed to Amount, and an object the node
        /// never sent this field for does not gain it on round-trip.
        /// </remarks>
        [JsonInclude]
        [JsonPropertyName("DeliverMax")]
        [JsonConverter(typeof(CurrencyConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        private Currency DeliverMax
        {
            get => _receivedAsDeliverMax ? Amount : null;
            set
            {
                Amount = value;
                _receivedAsDeliverMax = true;
            }
        }

        /// <inheritdoc />
        public string Destination { get; set; }

        /// <inheritdoc />
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        public new PaymentFlags? Flags
        {
            get => base.Flags.HasValue ? (PaymentFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        public string InvoiceID { get; set; }

        /// <inheritdoc />
        public List<List<PathStep>> Paths { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DeliverMin { get; set; }

        /// <inheritdoc />
        public string DomainID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CredentialIDs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> CredentialIDs { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a Payment at runtime.
        /// </summary>
        /// <param name="tx"> A Payment Transaction.</param>
        /// <exception cref="ValidationException">When the Payment is malformed.</exception>
        public static void ValidatePayment(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Amount", out var Amount) || Amount is null)
                throw new ValidationException("PaymentTransaction: missing field Amount");

            if (!Common.IsAmount(Amount))
                throw new ValidationException("PaymentTransaction: invalid Amount");


            if (!tx.TryGetValue("Destination", out var Destination) || Destination is null)
                throw new ValidationException("PaymentTransaction: missing field Destination");
            if (!Common.IsAmount(Destination))
                throw new ValidationException("PaymentTransaction: invalid Destination");

            if (tx.TryGetValue("DestinationTag", out var DestinationTag) && !Common.IsUInt32(DestinationTag))
                throw new ValidationException("PaymentTransaction: DestinationTag must be a number");

            if (tx.TryGetValue("InvoiceID", out var InvoiceID) && InvoiceID is not string { })
                throw new ValidationException("PaymentTransaction: InvoiceID must be a string");
            if (tx.TryGetValue("Paths", out var Paths) && Paths is not null && !IsPathSet(Paths))
                throw new ValidationException("PaymentTransaction: invalid Paths");
            if (tx.TryGetValue("SendMax", out var SendMax) && !Common.IsAmount(SendMax))
                throw new ValidationException("PaymentTransaction: invalid SendMax");

            if (tx.TryGetValue("DomainID", out var domainId) && domainId is not null)
            {
                if (domainId is not string domainIdStr)
                    throw new ValidationException("PaymentTransaction: DomainID must be a string");
                if (domainIdStr.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(domainIdStr, "^[0-9A-Fa-f]{64}$"))
                    throw new ValidationException("PaymentTransaction: DomainID must be a 64-character hexadecimal string (256-bit hash)");
            }

            if (tx.TryGetValue("CredentialIDs", out var credentialIds) && credentialIds is not null)
            {
                CredentialsValidator.ValidateCredentialsList(credentialIds, "PaymentTransaction", "CredentialIDs", isStringID: true);
            }

            CheckPartialPayment(tx);
        }

        public static void CheckPartialPayment(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("DeliverMin", out var DeliverMin)) 
                return;

            if (tx.TryGetValue("Flags", out var flags))
            {
                if (flags is null)
                    throw new ValidationException("PaymentTransaction: tfPartialPayment flag required with DeliverMin");
            }

            bool isTfPartialPayment = flags switch
            {
                PaymentFlags pf => (pf & PaymentFlags.tfPartialPayment) != 0,
                Dictionary<string, object> flagDict => CheckFlag<PaymentFlags>(flagDict, "tfPartialPayment"),
                // Dictionary values carry JSON numbers as int/long/ulong, not uint.
                _ => Common.TryGetUInt32(flags, out uint uFlag) && ModelUtils.IsFlagEnabled(uFlag, (uint)PaymentFlags.tfPartialPayment),
            };
            if (!isTfPartialPayment)
                throw new ValidationException("PaymentTransaction: tfPartialPayment flag required with DeliverMin");
            if (!Common.IsAmount(DeliverMin))
                throw new ValidationException("PaymentTransaction: invalid DeliverMin");

            return;
        }
        static bool CheckFlag<T>(Dictionary<string, object> flag, string type) where T : Enum
        {
            if (flag.TryGetValue(type, out object f) && f is true)
            {
                return true;
            }

            return false;

        }
        public static bool IsPathStep(Dictionary<string, object> pathStep)
        {
            if (pathStep.TryGetValue("account", out var acc) && acc is not string { })
                return false;
            if (pathStep.TryGetValue("currency", out var currency) && currency is not string { })
                return false;
            if (pathStep.TryGetValue("issuer", out var issuer) && issuer is not string { })
                return false;
            if (pathStep.TryGetValue("mpt_issuance_id", out var mptIssuanceId) && mptIssuanceId is not string { })
                return false;

            // rippled toStrand(): `hasAccount && (hasIssuer || hasCurrency)` and
            // `hasMPT && (hasCurrency || hasAccount)` are both temBAD_PATH
            if (currency is not null && mptIssuanceId is not null)
                return false;
            if (acc is not null && (currency is not null || issuer is not null || mptIssuanceId is not null))
                return false;
            if (acc is not null)
                return true;
            if (currency is not null || issuer is not null || mptIssuanceId is not null)
                return true;
            return false;
        }
        public static bool IsPaths(List<Dictionary<string, object>> paths)
        {
            foreach (var path in paths)
            {
                if (!IsPathStep(path))
                    return false;
            }

            return true;

        }
        /// <summary>
        /// Checks a path set of any collection shape: DictionaryObjectConverter yields
        /// <c>List&lt;object&gt;</c> of <c>List&lt;object&gt;</c> of <c>Dictionary&lt;string, object&gt;</c>,
        /// while code-built transactions use strongly typed lists.
        /// </summary>
        public static bool IsPathSet(object paths)
        {
            if (paths is string || paths is not System.Collections.IEnumerable pathList)
                return false;

            bool any = false;
            foreach (object path in pathList)
            {
                if (path is string || path is not System.Collections.IEnumerable steps)
                    return false;

                bool anyStep = false;
                foreach (object step in steps)
                {
                    if (step is not System.Collections.Generic.IDictionary<string, object> stepDict)
                        return false;
                    if (!IsPathStep(new Dictionary<string, object>(stepDict)))
                        return false;
                    anyStep = true;
                }

                if (!anyStep)
                    return false;
                any = true;
            }

            return any;
        }

        public static bool IsPaths(List<List<Dictionary<string, object>>> paths)
        {
            if (paths is null || paths.Count == 0)
                return false;
            foreach (var c in paths)
            {
                if (c is null || c.Count == 0) return false;
                if (!IsPaths(c)) return false;
            }
            return true;
        }
    }

}
