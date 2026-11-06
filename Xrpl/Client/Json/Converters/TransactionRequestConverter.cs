using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;
using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters;
/// <summary> Transaction json Converter </summary>
public class TransactionRequestConverter : JsonConverter<ITransactionRequest>
{
    /// <summary>
    /// Writes an <see cref="ITransactionRequest"/> to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, ITransactionRequest value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Remove this converter to avoid infinite recursion
        JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<TransactionRequestConverter>(options);

        JsonSerializer.Serialize(writer, value, value.GetType(), innerOptions);
    }

    /// <summary>
    /// create <see cref="ITransactionRequest"/> by TransactionType discriminator
    /// </summary>
    public static ITransactionRequest Create(string transactionType)
    {
        return transactionType switch
        {
            "AccountSet" => new AccountSet(),
            "AccountDelete" => new AccountDelete(),

            "CheckCancel" => new CheckCancel(),
            "CheckCash" => new CheckCash(),
            "CheckCreate" => new CheckCreate(),

            "DepositPreauth" => new DepositPreauth(),

            "EscrowCancel" => new EscrowCancel(),
            "EscrowCreate" => new EscrowCreate(),
            "EscrowFinish" => new EscrowFinish(),

            "NFTokenAcceptOffer" => new NFTokenAcceptOffer(),
            "NFTokenCancelOffer" => new NFTokenCancelOffer(),
            "NFTokenBurn" => new NFTokenBurn(),
            "NFTokenCreateOffer" => new NFTokenCreateOffer(),
            "NFTokenMint" => new NFTokenMint(),
            "NFTokenModify" => new NFTokenModify(),

            "OfferCancel" => new OfferCancel(),
            "OfferCreate" => new OfferCreate(),

            "Payment" => new Payment(),
            "PaymentChannelClaim" => new PaymentChannelClaim(),
            "PaymentChannelCreate" => new PaymentChannelCreate(),
            "PaymentChannelFund" => new PaymentChannelFund(),

            "SetRegularKey" => new SetRegularKey(),
            "SignerListSet" => new SignerListSet(),
            "TicketCreate" => new TicketCreate(),
            "TrustSet" => new TrustSet(),
            "EnableAmendment" => new EnableAmendment(),
            "SetFee" => new SetFee(),
            "UNLModify" => new UNLModify(),

            "AMMBid" => new AMMBid(),
            "AMMCreate" => new AMMCreate(),
            "AMMDelete" => new AMMDelete(),
            "AMMDeposit" => new AMMDeposit(),
            "AMMVote" => new AMMVote(),
            "AMMWithdraw" => new AMMWithdraw(),

            "Clawback" => new ClawBack(),
            "AMMClawback" => new AMMClawBack(),

            "Batch" => new Batch(),

            "MPTokenAuthorize" => new MPTokenAuthorize(),
            "MPTokenIssuanceCreate" => new MPTokenIssuanceCreate(),
            "MPTokenIssuanceDestroy" => new MPTokenIssuanceDestroy(),
            "MPTokenIssuanceSet" => new MPTokenIssuanceSet(),

            "OracleSet" => new OracleSet(),
            "OracleDelete" => new OracleDelete(),
            "DIDSet" => new DIDSet(),
            "DIDDelete" => new DIDDelete(),
            "PermissionedDomainSet" => new PermissionedDomainSet(),
            "PermissionedDomainDelete" => new PermissionedDomainDelete(),
            "CredentialCreate" => new CredentialCreate(),
            "CredentialAccept" => new CredentialAccept(),
            "CredentialDelete" => new CredentialDelete(),

            "XChainCreateBridge" => new XChainCreateBridge(),
            "XChainModifyBridge" => new XChainModifyBridge(),
            "XChainCreateClaimID" => new XChainCreateClaimID(),
            "XChainCommit" => new XChainCommit(),
            "XChainClaim" => new XChainClaim(),
            "XChainAccountCreateCommit" => new XChainAccountCreateCommit(),
            "XChainAddClaimAttestation" => new XChainAddClaimAttestation(),
            "XChainAddAccountCreateAttestation" => new XChainAddAccountCreateAttestation(),

            "VaultCreate" => new VaultCreate(),
            "VaultSet" => new VaultSet(),
            "VaultDelete" => new VaultDelete(),
            "VaultDeposit" => new VaultDeposit(),
            "VaultWithdraw" => new VaultWithdraw(),
            "VaultClawback" => new VaultClawback(),

            "LoanBrokerSet" => new LoanBrokerSet(),
            "LoanBrokerDelete" => new LoanBrokerDelete(),
            "LoanBrokerCoverDeposit" => new LoanBrokerCoverDeposit(),
            "LoanBrokerCoverWithdraw" => new LoanBrokerCoverWithdraw(),
            "LoanBrokerCoverClawback" => new LoanBrokerCoverClawback(),
            "LoanSet" => new LoanSet(),
            "LoanDelete" => new LoanDelete(),
            "LoanManage" => new LoanManage(),
            "LoanPay" => new LoanPay(),

            "DelegateSet" => new DelegateSet(),
            "LedgerStateFix" => new LedgerStateFix(),

            "SponsorshipSet" => new SponsorshipSet(),
            "SponsorshipTransfer" => new SponsorshipTransfer(),

            "ConfidentialMPTConvert" => new ConfidentialMPTConvert(),
            "ConfidentialMPTMergeInbox" => new ConfidentialMPTMergeInbox(),
            "ConfidentialMPTConvertBack" => new ConfidentialMPTConvertBack(),
            "ConfidentialMPTSend" => new ConfidentialMPTSend(),
            "ConfidentialMPTClawback" => new ConfidentialMPTClawback(),
            "ConfidentialMPTMirrorUpdate" => new ConfidentialMPTMirrorUpdate(),
            "ConfidentialMPTHolderKeyUpdate" => new ConfidentialMPTHolderKeyUpdate(),

            "TransactionProposalCreate" => new TransactionProposalCreate(),
            "TransactionProposalCancel" => new TransactionProposalCancel(),

            //_ => throw new Exception("Can't create transaction type" + transactionType)
            _ => new TransactionUnknown(),
        };
    }

    /// <summary>
    /// Sentinel for a transaction the SDK does not recognise, including one that carries no
    /// TransactionType at all. The constructor sets the property because in that second case
    /// there is nothing in the JSON for <see cref="TransactionTypeConverter"/> to read, and
    /// the enum's default is AccountSet - a silently wrong type rather than an unknown one.
    /// </summary>
    private class TransactionUnknown : TransactionRequest, ITransactionRequest
    {
        public TransactionUnknown() => TransactionType = TransactionType.Unknown;
    }

    /// <summary> read  <see cref="ITransactionRequest"/>   from json object </summary>
    public override ITransactionRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string transactionType = JsonMemberReader.TryFind(reader, "TransactionType"u8, out Utf8JsonReader ttValue)
            ? ttValue.GetString()
            : null;

        ITransactionRequest transactionRequest = Create(transactionType);

        // Remove this converter to avoid infinite recursion
        JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<TransactionRequestConverter>(options);

        // The JsonException is deliberately not caught. It used to be, returning the bare
        // instance created above - a transaction with nothing but its TransactionType set. That
        // object is indistinguishable from a real one to everything downstream, including Sign:
        // malformed input became a signable, empty transaction instead of an error.
        return (ITransactionRequest)JsonSerializer.Deserialize(ref reader, transactionRequest.GetType(), innerOptions);
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeof(ITransactionRequest).IsAssignableFrom(typeToConvert);
}
