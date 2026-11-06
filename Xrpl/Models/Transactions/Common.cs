using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Extensions;
using Xrpl.Utils;
using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Utils;

using static Xrpl.Models.Transactions.BatchSigner;

using JsonSerializer = System.Text.Json.JsonSerializer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/common.ts

namespace Xrpl.Models.Transactions
{
    public class Common
    {
        const int ISSUED_CURRENCY_SIZE = 3;
        private const int SIGNER_SIZE = 3;
        private const int MEMO_SIZE = 3;

        public static bool IsRecord(object value)
        {
            return value != null && value is Dictionary<string, object>;
        }
        /// <summary>
        /// Verify the form and type of an IssuedCurrencyAmount at runtime.
        /// </summary>
        /// <param name="input">The input to check the form and type of.</param>
        /// <returns>Whether the IssuedCurrencyAmount is malformed.</returns>
        public static bool IsIssuedCurrency(object input)
        {
            if (input is not Dictionary<string, object> dict)
                return false;
            return dict.Count == ISSUED_CURRENCY_SIZE &&
                dict.TryGetValue("value", out object val) && val is string &&
                dict.TryGetValue("issuer", out object iss) && iss is string &&
                dict.TryGetValue("currency", out object cur) && cur is string;
        }
        /// <summary>
        /// Verify the form and type of an Amount at runtime.
        /// </summary>
        /// <param name="amount">The object to check the form and type of.</param>
        /// <returns>Whether the Amount is malformed.</returns>
        public static bool IsAmount(object amount)
        {
            return amount is string || IsIssuedCurrency(amount) || IsMPTAmount(amount);
        }


        /// <summary>
        /// Verify the form and type of a Signers array element at runtime.
        /// </summary>
        /// <param name="signer">A Signers element, in its STArray form <c>{"Signer": {...}}</c>.</param>
        /// <returns>Whether the element is a well-formed Signer with Account, TxnSignature and SigningPubKey.</returns>
        public static bool IsSigner(object signer)
        {
            if (signer is not Dictionary<string, object> wrapper
                || !wrapper.TryGetValue("Signer", out object inner)
                || inner is not Dictionary<string, object> { Count: SIGNER_SIZE } value)
                return false;

            return (value.TryGetValue("Account", out object account) && account is string { }) &&
                   (value.TryGetValue("TxnSignature", out object TxnSignature) && TxnSignature is string { }) &&
                   (value.TryGetValue("SigningPubKey", out object SigningPubKey) && SigningPubKey is string { });
        }
        /// <summary>
        /// Verify the form and type of a Memos array element at runtime.
        /// </summary>
        /// <param name="memo">A Memos element, in its STArray form <c>{"Memo": {...}}</c>.</param>
        /// <returns>Whether the element is a well-formed Memo: one to three of MemoType, MemoData and
        /// MemoFormat, each optional and each a hex string when present.</returns>
        public static bool IsMemo(object memo)
        {
            if (memo is not Dictionary<string, object> wrapper
                || !wrapper.TryGetValue("Memo", out object inner)
                || inner is not Dictionary<string, object> value)
                return false;

            int size = value.Count;

            return size is >= 1 and <= MEMO_SIZE
                   && IsOptionalHex(value, "MemoData")
                   && IsOptionalHex(value, "MemoFormat")
                   && IsOptionalHex(value, "MemoType")
                   && value.OnlyHasFields(new[] { "MemoFormat", "MemoData", "MemoType" });
        }

        private static bool IsOptionalHex(Dictionary<string, object> value, string field)
            => !value.TryGetValue(field, out object fieldValue) || fieldValue is null || fieldValue is string s && s.IsHex();

        /// <summary>
        /// Verify the form and type of an MPT amount (<c>{"value", "mpt_issuance_id"}</c>) at runtime.
        /// </summary>
        /// <param name="input">The object to check the form and type of.</param>
        /// <returns>Whether the input is a well-formed MPT amount.</returns>
        public static bool IsMPTAmount(object input)
        {
            return input is Dictionary<string, object> { Count: 2 } dict
                   && dict.TryGetValue("value", out object val) && val is string
                   && dict.TryGetValue("mpt_issuance_id", out object id) && id is string;
        }


        /// <summary>
        /// Parse the value of an amount, expressed either in XRP or as an Issued Currency, into a number.
        /// </summary>
        /// <param name="amount"> An Amount to parse for its value.</param>
        /// <returns></returns>
        /// <exception cref="ValidationException">The parsed amount value, or null if the amount count not be parsed.</exception>
        public static double ParseAmountValue(object amount)
        {
            if (!Common.IsAmount(amount))
            {
                return double.NaN;
            }
            if (amount is string strAmount)
            {
                return double.Parse(
                    strAmount, NumberStyles.AllowLeadingSign
                                 | (NumberStyles.AllowLeadingSign & NumberStyles.AllowExponent)
                                 | (NumberStyles.AllowLeadingSign & NumberStyles.AllowExponent & NumberStyles.AllowDecimalPoint)
                                 | (NumberStyles.AllowExponent & NumberStyles.AllowDecimalPoint)
                                 | NumberStyles.AllowExponent
                                 | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture);
            }

            Dictionary<string, object> dict = (Dictionary<string, object>)amount;
            return double.Parse(
                (string)dict["value"], NumberStyles.AllowLeadingSign
                             | (NumberStyles.AllowLeadingSign & NumberStyles.AllowExponent)
                             | (NumberStyles.AllowLeadingSign & NumberStyles.AllowExponent & NumberStyles.AllowDecimalPoint)
                             | (NumberStyles.AllowExponent & NumberStyles.AllowDecimalPoint)
                             | NumberStyles.AllowExponent
                             | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture);
        }
        /// <summary>
        /// Verify the form and type of an Issue at runtime.
        /// </summary>
        /// <param name="input">The object to check the form and type of.</param>
        /// <returns>Whether the Issue is malformed.</returns>
        public static bool IsIssue(object input)
        {
            if (input is not Dictionary<string, object> issue)
                return false;

            int length = issue.Count;
            issue.TryGetValue("currency", out object currency);
            issue.TryGetValue("issuer", out object issuer);
            return (length == 1 && currency is string c && c == "XRP")
                   || (length == 2 && currency is string && issuer is string)
                   || (length == 1 && IsMPTIssuanceId(issue.TryGetValue("mpt_issuance_id", out object id) ? id : null));
        }

        /// <summary>
        /// rippled's <c>mptIssueFromJson</c> parses the id as a 192-bit hex value, so anything other
        /// than 48 hex characters is refused by the node.
        /// </summary>
        private static bool IsMPTIssuanceId(object value)
            => value is string { Length: 48 } s && s.IsHex();
        /// <summary>
        /// True when the value is an integral number representable as UInt32.
        /// DictionaryObjectConverter materializes JSON numbers as int/long/ulong,
        /// so a uint-only type test rejects valid dictionary values.
        /// </summary>
        public static bool IsUInt32(object value) => value switch
        {
            uint => true,
            int i => i >= 0,
            long l => l >= 0 && l <= uint.MaxValue,
            ulong ul => ul <= uint.MaxValue,
            _ => false,
        };

        /// <summary>
        /// Extracts a UInt32 from any integral representation produced by the JSON layer.
        /// </summary>
        public static bool TryGetUInt32(object value, out uint result)
        {
            switch (value)
            {
                case uint u: result = u; return true;
                case int i when i >= 0: result = (uint)i; return true;
                case long l when l >= 0 && l <= uint.MaxValue: result = (uint)l; return true;
                case ulong ul when ul <= uint.MaxValue: result = (uint)ul; return true;
                case byte b: result = b; return true;
                case ushort us: result = us; return true;
                case sbyte sb when sb >= 0: result = (uint)sb; return true;
                case short s when s >= 0: result = (uint)s; return true;
                default: result = 0; return false;
            }
        }

        /// <summary>
        /// Extracts a signed Int32 from any integral representation produced by the JSON layer.
        /// Used by delta fields such as SponsorshipSet.RemainingOwnerCountDelta, which are
        /// serialized as the XRPL Int32 type and may be negative.
        /// </summary>
        public static bool TryGetInt32(object value, out int result)
        {
            switch (value)
            {
                case int i: result = i; return true;
                case uint u when u <= int.MaxValue: result = (int)u; return true;
                case long l when l >= int.MinValue && l <= int.MaxValue: result = (int)l; return true;
                case ulong ul when ul <= int.MaxValue: result = (int)ul; return true;
                default: result = 0; return false;
            }
        }

        /// <summary>
        /// Validates a flags value that must be non-zero and contain only bits
        /// defined by <typeparamref name="TEnum"/> (rippled temINVALID_FLAG pattern).
        /// </summary>
        /// <exception cref="ValidationException">When the value is zero or has bits outside the enum mask.</exception>
        public static void ValidateNonZeroFlagsMask<TEnum>(uint value, string error) where TEnum : struct, Enum
        {
            uint validMask = Enum.GetValues<TEnum>().Aggregate(0u, (acc, flag) => acc | Convert.ToUInt32(flag));
            if (value == 0 || (value & ~validMask) != 0)
            {
                throw new ValidationException(error);
            }
        }

        /// <summary>
        /// Validates a DomainID value: a 64-character hex UInt256. On
        /// MPTokenIssuanceCreate a zero domain is temMALFORMED; on
        /// MPTokenIssuanceSet zero is legal (it clears the domain), so the
        /// zero check is opt-in via <paramref name="allowZero"/>.
        /// </summary>
        /// <exception cref="ValidationException">When the value is malformed.</exception>
        public static void ValidateDomainId(string value, string txLabel, bool allowZero = false)
        {
            if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            {
                throw new ValidationException($"{txLabel}: DomainID must be a 64-character hexadecimal string");
            }
            if (!allowZero && value.All(c => c == '0'))
            {
                throw new ValidationException($"{txLabel}: DomainID must not be zero");
            }
        }

        /// <summary>
        /// Verify the common fields of a transaction.<br/>
        /// The validate functionality will be optional, and will check transaction form at runtime.
        /// This should be called any time a transaction will be verified.
        /// </summary>
        /// <param name="tx">An interface w/ common transaction fields.</param>
        /// <exception cref="ValidationException"> When the common param is malformed.</exception>
        public static void ValidateBaseTransaction(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("Account", out var Account) || Account is null)
            {
                throw new ValidationException("BaseTransaction: missing field Account");
            }

            if (Account is not string { })
            {
                throw new ValidationException("BaseTransaction: Account not string");
            }

            if (!tx.TryGetValue("TransactionType", out var TransactionType) || TransactionType is null)
            {
                throw new ValidationException("BaseTransaction: missing field TransactionType");
            }

            if (TransactionType is not string)
            {
                throw new ValidationException("BaseTransaction: TransactionType not string");
            }


            if (Enum.GetValues<TransactionType>().All(type => type.ToString() != $"{TransactionType}"))
            {
                throw new ValidationException("BaseTransaction: Unknown TransactionType");
            }

            if (tx.TryGetValue("Fee", out var Fee) && Fee is not string { })
            {
                throw new ValidationException("BaseTransaction: invalid Fee");
            }

            if (tx.TryGetValue("Sequence", out var Sequence) && !IsUInt32(Sequence))
            {
                throw new ValidationException("BaseTransaction: invalid Sequence");
            }
            if (tx.TryGetValue("AccountTxnID", out var AccountTxnID) && AccountTxnID is not string { })
            {
                throw new ValidationException("BaseTransaction: invalid AccountTxnID");
            }
            if (tx.TryGetValue("LastLedgerSequence", out var LastLedgerSequence) && !IsUInt32(LastLedgerSequence))
            {
                throw new ValidationException("BaseTransaction: invalid LastLedgerSequence");
            }

            // eslint-disable-next-line @typescript-eslint/consistent-type-assertions -- Only used by JS
            tx.TryGetValue("Memos", out var Memos);
            if (Memos is not null)
            {
                if (Memos is not IEnumerable<object> { } memos)
                    throw new ValidationException("BaseTransaction: invalid Memos");

                if (memos.Any(memo => !Common.IsMemo(memo)))
                {
                    throw new ValidationException("BaseTransaction: invalid Memos");
                }
            }

            // eslint-disable-next-line @typescript-eslint/consistent-type-assertions -- Only used by JS
            tx.TryGetValue("Signers", out var Signers);

            if (Signers is not null)
            {
                if (Signers is not List<object> signers)
                    throw new ValidationException("BaseTransaction: invalid Signers");

                if (signers.ToArray().Length == 0)
                    throw new ValidationException("BaseTransaction: invalid Signers");

                if (signers.Any(signer => !Common.IsSigner(signer)))
                {
                    throw new ValidationException("BaseTransaction: invalid Signers");
                }

            }

            if (tx.TryGetValue("SourceTag", out var SourceTag) && !IsUInt32(SourceTag))
            {
                throw new ValidationException("BaseTransaction: invalid SourceTag");
            }
            if (tx.TryGetValue("SigningPubKey", out var SigningPubKey) && SigningPubKey is not string { })
            {
                throw new ValidationException("BaseTransaction: invalid SigningPubKey");
            }
            if (tx.TryGetValue("TicketSequence", out var TicketSequence) && !IsUInt32(TicketSequence))
            {
                throw new ValidationException("BaseTransaction: invalid TicketSequence");
            }
            if (tx.TryGetValue("TxnSignature", out var TxnSignature) && TxnSignature is not string { })
            {
                throw new ValidationException("BaseTransaction: invalid TxnSignature");
            }
            if (tx.TryGetValue("Sponsor", out var Sponsor) && Sponsor is not string { })
            {
                throw new ValidationException("BaseTransaction: invalid Sponsor");
            }
            if (tx.TryGetValue("SponsorFlags", out var SponsorFlags) && !IsUInt32(SponsorFlags))
            {
                throw new ValidationException("BaseTransaction: invalid SponsorFlags");
            }
            if (tx.TryGetValue("Delegate", out var Delegate) && Delegate is not string { })
            {
                throw new ValidationException("BaseTransaction: invalid Delegate");
            }
            if (tx.TryGetValue("OperationLimit", out var OperationLimit) && !IsUInt32(OperationLimit))
            {
                throw new ValidationException("BaseTransaction: invalid OperationLimit");
            }
            return;
        }
    }

    /// <inheritdoc />
    [JsonConverter(typeof(TransactionRequestConverter))]
    public abstract class TransactionRequest : ITransactionRequest //todo rename to BaseTransaction
    {
        //protected TransactionCommon()
        //{
        //    Fee = new Currency { Value = "10" };
        //}

        public uint? NetworkID { get; set; }

        /// <inheritdoc />
        public string Account { get; set; }

        /// <inheritdoc />
        public string AccountTxnID { get; set; }
        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Fee { get; set; }
        /// <inheritdoc />
        public uint? Flags { get; set; }
        /// <inheritdoc />
        public uint? LastLedgerSequence { get; set; }
        /// <inheritdoc />
        public List<MemoWrapper> Memos { get; set; }

        /// <inheritdoc />
        public uint? Sequence { get; set; }
        /// <inheritdoc />
        [JsonPropertyName("SigningPubKey")]
        public string SigningPublicKey { get; set; }
        /// <inheritdoc />
        public List<SignerWrapper> Signers { get; set; }
        private TransactionType _transactionType;
        private string _unknownTransactionType;

        /// <inheritdoc />
        [JsonIgnore]
        public TransactionType TransactionType
        {
            get => _transactionType;
            set
            {
                _transactionType = value;
                _unknownTransactionType = null;
            }
        }

        /// <inheritdoc />
        [JsonPropertyName("TransactionType")]
        [JsonConverter(typeof(TransactionTypeNameConverter))]
        public string TransactionTypeName
        {
            get => _unknownTransactionType ?? _transactionType.ToString();
            set
            {
                _transactionType = TransactionTypeConverter.Parse(value);
                _unknownTransactionType = _transactionType == TransactionType.Unknown ? value : null;
            }
        }

        /// <inheritdoc />
        [JsonPropertyName("TxnSignature")]
        public string TransactionSignature { get; set; }

        /// <inheritdoc />
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, this.GetType(), XrplJsonOptions.Default);
        }

        public Dictionary<string, object> ToDictionary()
        {
            var json = ToJson();
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default) ?? throw new ValidationException("Failed to deserialize tx json");
        }

        /// <inheritdoc />
        public uint? SourceTag { get; set; }
        /// <inheritdoc />
        public uint? TicketSequence { get; set; }

        /// <inheritdoc />
        public string Sponsor { get; set; }

        /// <inheritdoc />
        public SponsorCoverage? SponsorFlags { get; set; }

        /// <inheritdoc />
        public SignatureObject SponsorSignature { get; set; }

        /// <inheritdoc />
        public string Delegate { get; set; }

        /// <inheritdoc />
        public uint? OperationLimit { get; set; }

        //todo not found fields -  SourceTag?: number, TicketSequence?: number
    }

    /// <summary>
    /// Additional arbitrary information used to identify transaction.
    /// </summary>
    public class MemoWrapper
    {
        /// <summary>
        /// The Memos field includes arbitrary messaging data with the transaction.
        /// </summary>
        [JsonPropertyName("Memo")]
        public Memo Memo { get; set; }
    }

    /// <summary>
    /// The Memos field includes arbitrary messaging data with the transaction.
    /// </summary>
    public class Memo
    {
        /// <summary>
        /// Arbitrary hex value, conventionally containing the content of the memo.
        /// </summary>
        public string MemoData { get; set; }

        /// <summary>
        /// Content of the memo.
        /// </summary>
        [JsonIgnore]
        public string MemoDataAsText => MemoData.FromHexString();
        /// <summary>
        /// Hex value representing characters allowed in URLs.<br/>
        /// Conventionally containing information on how the memo is encoded, for example as a MIME type.
        /// </summary>
        public string MemoFormat { get; set; }
        /// <summary>
        /// Conventionally containing information on how the memo is encoded, for example as a MIME type.
        /// </summary>
        [JsonIgnore]
        public string MemoFormatAsText => MemoFormat.FromHexString();

        /// <summary>
        /// Hex value representing characters allowed in URLs.<br/>
        /// Conventionally, a unique relation (according to RFC 5988 ) that defines the format of this memo.
        /// </summary>
        public string MemoType { get; set; }

        /// <summary>
        /// Conventionally, a unique relation (according to RFC 5988 ) that defines the format of this memo.
        /// </summary>
        [JsonIgnore]
        public string MemoTypeAsText => MemoType.FromHexString();
    }

    /// <summary>
    /// Transaction metadata is a section of data that gets added to a transaction after it is processed.<br/>
    /// Any transaction that gets included in a ledger has metadata, regardless of whether it is successful.<br/>
    /// The transaction metadata describes the outcome of the transaction in detail.<br/>
    /// <remarks>
    /// Warning: The changes described in transaction metadata are only final if the transaction is in a validated ledger version.
    /// </remarks>
    /// </summary>
    public class Meta : ITransactionMetadata
    {
        /// <summary>
        /// List of ledger objects that were created, deleted, or modified by this transaction, and specific changes to each.
        /// </summary>
        public List<AffectedNode> AffectedNodes { get; set; }

        public string MetaBlob { get; set; } //todo unknown field

        /// <summary>
        /// The transaction's position within the ledger that included it.<br/>
        /// This is zero-indexed.<br/>
        /// (For example, the value 2 means it was the 3rd transaction in that ledger.)
        /// </summary>
        public int TransactionIndex { get; set; }

        /// <summary>
        /// A result code indicating whether the transaction succeeded or how it failed.
        /// </summary>
        public string TransactionResult { get; set; }

        /// <summary>
        /// Shows the OfferIDof a new NFTokenOffer in a response from a NFTokenCreateOffer transaction.
        /// </summary>
        [JsonPropertyName("offer_id")]
        public string OfferID { get; set; }
        /// <summary>
        /// Shows the NFTokenID for the NFToken that changed on the ledger as a result of the transaction.
        /// Only present if the transaction is NFTokenMint or NFTokenAcceptOffer
        /// </summary>
        [JsonPropertyName("nftoken_id")]
        public string NFTokenId { get; set; }

        /// <summary>
        /// Shows all the NFTokenIDs for the NFTokens that changed on the ledger as a result of the transaction.
        /// Only present if the transaction is NFTokenCancelOffer.
        /// </summary>
        [JsonPropertyName("nftoken_ids")]
        public string[] NFTokenIds { get; set; }

        /// <inheritdoc />
        public string? ParentBatchID { get; set; }

        /// <summary>
        /// Shows the MPTokenIssuanceID for the MPTokenIssuance that was created by this transaction.
        /// Only present if the transaction is MPTokenIssuanceCreate.
        /// </summary>
        [JsonPropertyName("mpt_issuance_id")]
        public string MptIssuanceId { get; set; }

        /// <summary>
        /// (Omitted for non-Payment transactions) The Currency Amount actually received by the Destination account.<br/>
        /// Use this field to determine how much was delivered, regardless of whether the transaction is a partial payment.<br/>
        /// See this description for details.
        /// </summary>
        [JsonConverter(typeof(CurrencyConverter))]
        [JsonPropertyName("delivered_amount")]
        public Currency ActuallyDeliveredAmount { get; set; }
        /// <summary>
        /// (May be omitted) For a partial payment, this field records the amount of currency actually delivered to the destination.<br/>
        /// To avoid errors when reading transactions, instead use the delivered_amount field, which is provided for all Payment transactions, partial or not.
        /// </summary>
        [JsonConverter(typeof(CurrencyConverter))]
        [JsonPropertyName("DeliveredAmount")]
        public Currency PartialDeliveredAmount { get; set; }
    }

    /// <summary>
    /// The AffectedNodes array contains a complete list of the objects in the ledger that this transaction modified in some way. 
    /// </summary>
    public class AffectedNode : IAffectedNode
    {
        /// <summary>
        /// indicates that the transaction created a new object in the ledger.
        /// </summary>
        public CreatedNode? CreatedNode { get; set; }
        /// <summary>
        /// indicates that the transaction removed an object from the ledger.
        /// </summary>
        public DeletedNode? DeletedNode { get; set; }
        /// <summary>
        /// indicates that the transaction modified an existing object in the ledger.
        /// <remarks>
        /// If the modified ledger object has PreviousTxnID and PreviousTxnLgrSeq fields,
        /// the transaction always updates them with the transaction's own identifying hash and the index of the ledger version that included the transaction,
        /// but these fields' new value is not listed in the FinalFields of the ModifiedNode object,
        /// and their previous values are listed at the top level of the ModifiedNode object rather than in the nested PreviousFields object.
        /// </remarks>
        /// </summary>
        public ModifiedNode? ModifiedNode { get; set; }
    }

    /// <summary>
    /// transaction object
    /// </summary>
    public class NodeInfo : ICreatedNode, IModifiedNode, IDeletedNode
    {
        /// <summary>
        /// The type of ledger object
        /// </summary>
        [JsonConverter(typeof(LedgerEntryTypeConverter))]
        public LedgerEntryType LedgerEntryType { get; set; }
        /// <summary>
        /// The ID of this ledger object in the ledger's state tree.<br/>
        /// Note: This is not the same as a ledger index, even though the field name is very similar.
        /// </summary>
        public string LedgerIndex { get; set; }
        /// <summary>
        /// ModifiedNode <br/>
        /// (May be omitted) The identifying hash of the previous transaction to modify this ledger object.<br/>
        /// Omitted for ledger object types that do not have a PreviousTxnID field.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// ModifiedNode <br/>
        /// (May be omitted) The Ledger Index of the ledger version containing the previous transaction to modify this ledger object.<br/>
        /// Omitted for ledger object types that do not have a PreviousTxnLgrSeq field.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint? PreviousTxnLgrSeq { get; set; }
        /// <summary>
        /// The content fields of the ledger object, read differently depending on the node type.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>DeletedNode</c>: the fields as they were immediately before the object was deleted.
        /// Which fields are present depends on what type of ledger object it was.
        /// </para>
        /// <para>
        /// <c>ModifiedNode</c>: the fields after applying any changes from this transaction. Which
        /// fields are present depends on the object type, and this omits <c>PreviousTxnID</c> and
        /// <c>PreviousTxnLgrSeq</c> even though most types of ledger object carry them.
        /// </para>
        /// </remarks>
        public BaseLedgerEntry? FinalFields { get; set; }

        /// <summary>
        /// CreatedNode <br/>
        /// The content fields of the newly-created ledger object.<br/>
        /// Which fields are present depends on what type of ledger object was created.
        /// </summary>
        public BaseLedgerEntry? NewFields { get; set; }

        /// <summary>
        /// JSON
        /// ModifiedNode <br/>
        /// The previous values for all fields of the object that were changed as a result of this transaction.<br/>
        /// If the transaction only added fields to the object, this field is an empty object.
        /// </summary>
        public BaseLedgerEntry? PreviousFields { get; set; }
    }
    //https://xrpl.org/transaction-common-fields.html
    /// <summary>
    /// Every transaction has the same set of common fields.
    /// </summary>
    public interface ITransactionCommon
    {
        public uint? NetworkID { get; set; }
        /// <summary>
        /// This is a required field
        /// The unique address of the account that initiated the transaction.
        /// </summary>
        string Account { get; set; }

        /// <summary>
        /// Hash value identifying another transaction.<br/>
        /// If provided, this transaction is only valid if the sending account's previously-sent transaction matches the provided hash.
        /// </summary>
        string AccountTxnID { get; set; }

        /// <summary>
        /// Integer amount of XRP, in drops, to be destroyed as a cost for distributing this transaction to the network.<br/>
        /// Some transaction types have different minimum requirements.
        /// </summary>
        Currency Fee { get; set; }

        /// <summary>
        /// Set of bit-flags for this transaction.<br/>
        /// number | GlobalFlags
        /// </summary>
        uint? Flags { get; set; }

        /// <summary>
        /// Although optional, the LastLedgerSequence is strongly recommended on every transaction to ensure it's validated or rejected promptly.<br/>
        /// Highest ledger index this transaction can appear in.<br/>
        /// Specifying this field places a strict upper limit on how long the transaction can wait to be validated or rejected.
        /// </summary>
        uint? LastLedgerSequence { get; set; }

        /// <summary>
        /// Additional arbitrary information used to identify this transaction.
        /// </summary>
        List<MemoWrapper> Memos { get; set; }
        [JsonIgnore]
        public string MemoValue => Memos is not { } memos ? null : memos.Aggregate(string.Empty, (current, memo) => current + $"{memo.Memo.MemoData.FromHexString()}");

        /// <summary>
        /// The sequence number of the account sending the transaction.<br/>
        /// A transaction is only valid if the Sequence number is exactly 1 greater than the previous transaction from the same account.<br/>
        /// The special case 0 means the transaction is using a Ticket instead.
        /// </summary>
        uint? Sequence { get; set; }

        /// <summary>
        /// Array of objects that represent a multi-signature which authorizes this transaction.
        /// </summary>
        List<SignerWrapper> Signers { get; set; }

        /// <summary>
        /// Hex representation of the public key that corresponds to the private key used to sign this transaction.<br/>
        /// If an empty string, indicates a multi-signature is present in the Signers field instead.
        /// </summary>
        string SigningPublicKey { get; set; }

        /// <summary>
        /// The signature that verifies this transaction as originating from the account it says it is from.
        /// </summary>
        string TransactionSignature { get; set; }

        /// <summary>
        /// The type of transaction.<br/>
        /// Valid types include: `Payment`, `OfferCreate`, `SignerListSet`,
        /// `EscrowCreate`, `EscrowFinish`, `EscrowCancel`, `PaymentChannelCreate`,
        /// `PaymentChannelFund`, `PaymentChannelClaim`, and `DepositPreauth`.
        /// </summary>
        TransactionType TransactionType { get; set; }

        /// <summary>
        /// The name of the transaction's type: the JSON form of <see cref="TransactionType"/>.
        /// For a type this SDK does not know, where <see cref="TransactionType"/> is
        /// <see cref="Xrpl.Models.TransactionType.Unknown"/>, this is the name the node sent, and
        /// the transaction writes that name back to JSON.
        /// </summary>
        [JsonIgnore]
        string TransactionTypeName { get; }

        /// <summary>
        /// (Optional) Arbitrary integer used to identify the reason for this payment, or a sender on whose behalf this transaction is made.<br/>
        /// Conventionally, a refund should specify the initial payment's SourceTag as the refund payment's DestinationTag.
        /// </summary>
        public uint? SourceTag { get; set; }
        /// <summary>
        /// (Optional) The sequence number of the ticket to use in place of a Sequence number.<br/>
        /// If this is provided, Sequence must be 0. Cannot be used with AccountTxnID.
        /// </summary>
        public uint? TicketSequence { get; set; }

        /// <summary>
        /// (Optional) The account submitting this transaction on behalf of <see cref="Account"/>,
        /// under permissions granted by DelegateSet (sfDelegate, a rippled common field).
        /// </summary>
        public string Delegate { get; set; }

        /// <summary>
        /// (Optional) sfOperationLimit, a rippled common field accepted on every transaction type.
        /// No XRPL transactor reads it; on Xahau a Burn-2-Mint burn carries the destination
        /// network id here.
        /// </summary>
        public uint? OperationLimit { get; set; }

        /// <summary>
        /// XLS-68: the account sponsoring this transaction's fee and/or reserve.
        /// </summary>
        public string Sponsor { get; set; }

        /// <summary>
        /// XLS-68: what the sponsor covers (serialized as the numeric sfSponsorFlags value).
        /// </summary>
        public SponsorCoverage? SponsorFlags { get; set; }

        /// <summary>
        /// XLS-68: the sponsor's co-signature (sfSponsorSignature) - <c>SigningPubKey</c> and
        /// <c>TxnSignature</c>, or <c>Signers</c> for a multisig sponsor. It is not part of what
        /// any party signs. Inside a Batch it is an unsigned marker (an empty object) on an inner
        /// transaction whose sponsor co-signs the batch through <c>BatchSigners</c>.
        /// </summary>
        /// <remarks>Produced by <c>SponsorSigningHelper</c> and <c>XrplWallet.SignAsSponsor</c>.</remarks>
        public SignatureObject SponsorSignature { get; set; }

        /// <summary>
        /// convert transaction to string json value
        /// </summary>
        /// <returns></returns>
        string ToJson();
        Dictionary<string, object> ToDictionary();
    }
    /// <summary>
    /// A transaction on its way to a node: built by a caller, signed, submitted.
    /// </summary>
    /// <remarks>
    /// The converter is declared here as well as on the implementing class because
    /// System.Text.Json picks one by the <b>declared</b> type: a variable typed as this interface
    /// does not inherit the class's attribute, and without it a transaction held in such a variable
    /// was written as the interface - every field of its actual type missing, and no exception to
    /// say so. It happened to work through <see cref="Xrpl.Client.Json.XrplJsonOptions.Default"/>,
    /// whose converter list makes up for a missing attribute, and only there.
    /// </remarks>
    [JsonConverter(typeof(TransactionRequestConverter))]
    public interface ITransactionRequest : ITransactionCommon
    {
    }

    /// <summary>
    /// Every transaction has the same set of common fields.
    /// </summary>
    /// <remarks>
    /// The converter is declared here for the same reason as on <see cref="ITransactionRequest"/>:
    /// System.Text.Json picks one by the declared type, so without it a transaction held in a
    /// variable of this type was written as the interface rather than as what it is.
    /// </remarks>
    [JsonConverter(typeof(TransactionResponseConverter))]
    public interface ITransactionResponse : IBaseTransactionResponse, ITransactionCommon
    {
        /// <summary>
        /// Transaction metadata is a section of data that gets added to a transaction after it is processed.<br/>
        /// Any transaction that gets included in a ledger has metadata, regardless of whether it is successful.<br/>
        /// The transaction metadata describes the outcome of the transaction in detail.<br/>
        /// <remarks>
        /// Warning: The changes described in transaction metadata are only final if the transaction is in a validated ledger version.
        /// </remarks>
        /// </summary>
        Meta Meta { get; set; }
    }

    /// <inheritdoc cref="ITransactionResponse" />
    [JsonConverter(typeof(TransactionResponseConverter))]
    public class TransactionResponse : BaseTransactionResponse, ITransactionResponse
    {
        public uint? NetworkID { get; set; }

        /// <inheritdoc/>
        public string Account { get; set; }

        /// <inheritdoc/>
        public string AccountTxnID { get; set; }

        /// <inheritdoc/>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Fee { get; set; }

        /// <inheritdoc/>
        public uint? Flags { get; set; }

        /// <inheritdoc/>
        public uint? LastLedgerSequence { get; set; }

        /// <inheritdoc/>
        public List<MemoWrapper> Memos { get; set; }
        [JsonIgnore]
        public string MemoValue => Memos is not { } memos ? null : memos.Aggregate(string.Empty, (current, memo) => current + $"{memo.Memo.MemoData.FromHexString()}");
        /// <inheritdoc/>
        public uint? Sequence { get; set; }
        /// <inheritdoc/>
        [JsonPropertyName("SigningPubKey")]
        public string SigningPublicKey { get; set; }

        /// <inheritdoc/>
        public List<SignerWrapper> Signers { get; set; }

        private TransactionType _transactionType;
        private string _unknownTransactionType;

        /// <inheritdoc/>
        [JsonIgnore]
        public TransactionType TransactionType
        {
            get => _transactionType;
            set
            {
                _transactionType = value;
                _unknownTransactionType = null;
            }
        }

        /// <inheritdoc/>
        [JsonPropertyName("TransactionType")]
        [JsonConverter(typeof(TransactionTypeNameConverter))]
        public string TransactionTypeName
        {
            get => _unknownTransactionType ?? _transactionType.ToString();
            set
            {
                _transactionType = TransactionTypeConverter.Parse(value);
                _unknownTransactionType = _transactionType == TransactionType.Unknown ? value : null;
            }
        }

        /// <inheritdoc/>
        [JsonPropertyName("TxnSignature")]
        public string TransactionSignature { get; set; }

        /// <inheritdoc/>
        [JsonPropertyName("meta")]
        public Meta Meta { get; set; }

        /// <inheritdoc />
        public string Sponsor { get; set; }

        /// <inheritdoc />
        public SponsorCoverage? SponsorFlags { get; set; }

        /// <inheritdoc />
        public SignatureObject SponsorSignature { get; set; }

        /// <inheritdoc />
        public string Delegate { get; set; }

        /// <inheritdoc />
        public uint? OperationLimit { get; set; }

        /// <inheritdoc/>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, this.GetType(), XrplJsonOptions.Default);
        }

        public Dictionary<string, object> ToDictionary()
        {
            var json = ToJson();
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default) ?? throw new ValidationException("Failed to deserialize tx json");
        }

        /// <inheritdoc />
        public uint? SourceTag { get; set; }
        /// <inheritdoc />
        public uint? TicketSequence { get; set; }
    }

    /// <summary>
    /// Represents a node that was created in a transaction.
    /// </summary>
    [JsonConverter(typeof(CreatedNodeConverter))]
    public class CreatedNode : NodeBase, ICreatedNode
    {
        /// <summary>
        /// Gets or sets the new fields created.
        /// </summary>
        [JsonPropertyName("NewFields")]
        public BaseLedgerEntry? NewFields { get; set; }

        public bool TryGetNew<T>(out T? value) where T : BaseLedgerEntry
        {
            value = NewFields as T;
            return value is not null;
        }
    }

    /// <summary>
    /// Represents a node that was modified in a transaction.
    /// </summary>
    [JsonConverter(typeof(ModifiedNodeConverter))]
    public class ModifiedNode : NodeBase, IModifiedNode
    {
        /// <summary>
        /// Gets or sets the final fields after modification.
        /// </summary>
        [JsonPropertyName("FinalFields")]
        public BaseLedgerEntry? FinalFields { get; set; }

        /// <summary>
        /// Gets or sets the previous fields before modification.
        /// </summary>
        [JsonPropertyName("PreviousFields")]
        public BaseLedgerEntry? PreviousFields { get; set; }

        /// <summary>
        /// Gets or sets the previous transaction ID.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// Gets or sets the previous transaction ledger sequence.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint? PreviousTxnLgrSeq { get; set; }

        public bool TryGetFinal<T>(out T? value) where T : BaseLedgerEntry
        {
            value = FinalFields as T;
            return value is not null;
        }

        public bool TryGetPrevious<T>(out T? value) where T : BaseLedgerEntry
        {
            value = PreviousFields as T;
            return value is not null;
        }
    }

    /// <summary>
    /// Represents a node that was deleted in a transaction.
    /// </summary>
    [JsonConverter(typeof(DeletedNodeConverter))]
    public class DeletedNode : NodeBase, IDeletedNode
    {
        /// <summary>
        /// The content fields of the ledger entry immediately before it was deleted.
        /// Which fields are present depends on what type of ledger entry was created.
        /// </summary>
        [JsonPropertyName("FinalFields")]
        public BaseLedgerEntry? FinalFields { get; set; }

        /// <summary>
        /// (May be omitted) Selected fields of the ledger entry before it was deleted.
        /// Which fields are present depends on what type of ledger entry was created.
        /// </summary>
        [JsonPropertyName("PreviousFields")]
        public BaseLedgerEntry? PreviousFields { get; set; }

        public bool TryGetFinal<T>(out T? value) where T : BaseLedgerEntry
        {
            value = FinalFields as T;
            return value is not null;
        }

        public bool TryGetPrevious<T>(out T? value) where T : BaseLedgerEntry
        {
            value = PreviousFields as T;
            return value is not null;
        }
    }
    /// <summary>
    /// Base class for ledger entries
    /// </summary>
    public abstract class NodeBase
    {
        private LedgerEntryType _ledgerEntryType;
        private string _unknownLedgerEntryType;

        /// <summary>
        /// Type of entry in the ledger
        /// </summary>
        [JsonIgnore]
        public LedgerEntryType LedgerEntryType
        {
            get => _ledgerEntryType;
            set
            {
                _ledgerEntryType = value;
                _unknownLedgerEntryType = null;
            }
        }

        /// <summary>
        /// The name of the entry's type: the JSON form of <see cref="LedgerEntryType"/>. For a type
        /// this SDK does not know, where <see cref="LedgerEntryType"/> is
        /// <see cref="Xrpl.Models.LedgerEntryType.Unknown"/>, this is the name the node sent.
        /// </summary>
        [JsonPropertyName("LedgerEntryType")]
        [JsonConverter(typeof(LedgerEntryTypeNameConverter))]
        public string LedgerEntryTypeName
        {
            get => _unknownLedgerEntryType ?? _ledgerEntryType.ToString();
            set
            {
                _ledgerEntryType = LedgerEntryTypeConverter.Parse(value);
                _unknownLedgerEntryType = _ledgerEntryType == LedgerEntryType.Unknown ? value : null;
            }
        }

        /// <summary>
        /// Identifier of this object in the ledger's state tree
        /// </summary>
        [JsonPropertyName("LedgerIndex")]
        public string LedgerIndex { get; set; } = string.Empty;
    }

    /// <summary>
    /// object has destination field
    /// </summary>
    public interface IDestination
    {
        string Destination { get; set; }
    }
}
