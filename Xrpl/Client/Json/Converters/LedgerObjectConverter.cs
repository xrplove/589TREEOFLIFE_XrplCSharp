using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters;

public abstract class NodeConverterBase<TNode> : JsonConverter<TNode> where TNode : NodeBase, new()
{
    protected static TNode CreateBaseNode(JsonElement root, JsonSerializerOptions options)
    {
        return new TNode
        {
            LedgerEntryTypeName = root.TryGetProperty("LedgerEntryType", out JsonElement type) ? type.GetString() : null,
            LedgerIndex = root.TryGetProperty("LedgerIndex", out JsonElement li) ? li.GetString() ?? string.Empty : string.Empty,
        };
    }
}

public class ModifiedNodeConverter : NodeConverterBase<ModifiedNode>
{
    public override ModifiedNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        ModifiedNode node = CreateBaseNode(root, options);

        node.PreviousTxnID = root.TryGetProperty("PreviousTxnID", out JsonElement ptid) ? ptid.GetString() : null;
        node.PreviousTxnLgrSeq = root.TryGetProperty("PreviousTxnLgrSeq", out JsonElement ptls) ? ptls.GetUInt32() : null;

        node.FinalFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            root.TryGetProperty("FinalFields", out JsonElement ff) ? ff : (JsonElement?)null,
            options);

        node.PreviousFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            root.TryGetProperty("PreviousFields", out JsonElement pf) ? pf : (JsonElement?)null,
            options);

        return node;
    }

    public override void Write(Utf8JsonWriter writer, ModifiedNode value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }

        writer.WriteStartObject();
        writer.WriteString("LedgerEntryType", value.LedgerEntryTypeName);
        if (!string.IsNullOrWhiteSpace(value.LedgerIndex))
            writer.WriteString("LedgerIndex", value.LedgerIndex);

        WriteLedgerEntry(writer, "FinalFields", value.FinalFields, options);
        WriteLedgerEntry(writer, "PreviousFields", value.PreviousFields, options);

        if (!string.IsNullOrWhiteSpace(value.PreviousTxnID))
            writer.WriteString("PreviousTxnID", value.PreviousTxnID);
        if (value.PreviousTxnLgrSeq.HasValue)
            writer.WriteNumber("PreviousTxnLgrSeq", value.PreviousTxnLgrSeq.Value);

        writer.WriteEndObject();
    }

    private static void WriteLedgerEntry(Utf8JsonWriter writer, string propertyName, BaseLedgerEntry entry, JsonSerializerOptions options)
    {
        if (entry == null) return;
        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(writer, entry, entry.GetType(), options);
    }
}

public class CreatedNodeConverter : NodeConverterBase<CreatedNode>
{
    public override CreatedNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        CreatedNode node = CreateBaseNode(root, options);

        node.NewFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            root.TryGetProperty("NewFields", out JsonElement nf) ? nf : (JsonElement?)null,
            options);

        return node;
    }

    public override void Write(Utf8JsonWriter writer, CreatedNode value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }

        writer.WriteStartObject();
        writer.WriteString("LedgerEntryType", value.LedgerEntryTypeName);
        if (!string.IsNullOrWhiteSpace(value.LedgerIndex))
            writer.WriteString("LedgerIndex", value.LedgerIndex);

        if (value.NewFields != null)
        {
            writer.WritePropertyName("NewFields");
            JsonSerializer.Serialize(writer, value.NewFields, value.NewFields.GetType(), options);
        }

        writer.WriteEndObject();
    }
}

public class DeletedNodeConverter : NodeConverterBase<DeletedNode>
{
    public override DeletedNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        DeletedNode node = CreateBaseNode(root, options);

        node.FinalFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            root.TryGetProperty("FinalFields", out JsonElement ff) ? ff : (JsonElement?)null,
            options);

        node.PreviousFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            root.TryGetProperty("PreviousFields", out JsonElement pf) ? pf : (JsonElement?)null,
            options);

        return node;
    }

    public override void Write(Utf8JsonWriter writer, DeletedNode value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }

        writer.WriteStartObject();
        writer.WriteString("LedgerEntryType", value.LedgerEntryTypeName);
        if (!string.IsNullOrWhiteSpace(value.LedgerIndex))
            writer.WriteString("LedgerIndex", value.LedgerIndex);

        if (value.FinalFields != null)
        {
            writer.WritePropertyName("FinalFields");
            JsonSerializer.Serialize(writer, value.FinalFields, value.FinalFields.GetType(), options);
        }
        if (value.PreviousFields != null)
        {
            writer.WritePropertyName("PreviousFields");
            JsonSerializer.Serialize(writer, value.PreviousFields, value.PreviousFields.GetType(), options);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// <see cref="BaseLedgerEntry"/> json converter
/// </summary>
public class LOConverter : JsonConverter<BaseLedgerEntry>
{
    private static Type GetTypeForLedgerEntry(LedgerEntryType type) => type switch
    {
        LedgerEntryType.AccountRoot => typeof(LOAccountRoot),
        LedgerEntryType.Amendments => typeof(LOAmendments),
        LedgerEntryType.DirectoryNode => typeof(LODirectoryNode),
        LedgerEntryType.Escrow => typeof(LOEscrow),
        LedgerEntryType.FeeSettings => typeof(LOFeeSettings),
        LedgerEntryType.LedgerHashes => typeof(LOLedgerHashes),
        LedgerEntryType.Offer => typeof(LOOffer),
        LedgerEntryType.PayChannel => typeof(LOPayChannel),
        LedgerEntryType.RippleState => typeof(LORippleState),
        LedgerEntryType.SignerList => typeof(LOSignerList),
        LedgerEntryType.NFTokenOffer => typeof(LONFTokenOffer),
        LedgerEntryType.NegativeUNL => typeof(LONegativeUNL),
        LedgerEntryType.NFTokenPage => typeof(LONFTokenPage),
        LedgerEntryType.Ticket => typeof(LOTicket),
        LedgerEntryType.AMM => typeof(LOAmm),
        LedgerEntryType.Check => typeof(LOCheck),
        LedgerEntryType.MPToken => typeof(LOMPToken),
        LedgerEntryType.MPTokenIssuance => typeof(LOMPTokenIssuance),
        LedgerEntryType.Oracle => typeof(LOOracle),
        LedgerEntryType.DID => typeof(LODID),
        LedgerEntryType.PermissionedDomain => typeof(LOPermissionedDomain),
        LedgerEntryType.Credential => typeof(LOCredential),
        LedgerEntryType.DepositPreauth => typeof(LODepositPreauth),
        LedgerEntryType.Bridge => typeof(LOBridge),
        LedgerEntryType.XChainOwnedClaimID => typeof(LOXChainOwnedClaimID),
        LedgerEntryType.XChainOwnedCreateAccountClaimID => typeof(LOXChainOwnedCreateAccountClaimID),
        LedgerEntryType.Delegate => typeof(LODelegate),
        LedgerEntryType.Vault => typeof(LOVault),
        LedgerEntryType.LoanBroker => typeof(LOLoanBroker),
        LedgerEntryType.Loan => typeof(LOLoan),
        LedgerEntryType.Sponsorship => typeof(LOSponsorship),
        LedgerEntryType.TransactionProposal => typeof(LOTransactionProposal),
        _ => typeof(BaseLedgerEntry),
    };

    /// <summary>
    /// Convert ledger entry json element to typed ledger object
    /// </summary>
    /// <param name="type">field type</param>
    /// <param name="element">current json element (nullable)</param>
    /// <param name="options">serializer options</param>
    /// <returns></returns>
    public static BaseLedgerEntry GetBaseRippleLO(
        LedgerEntryType type,
        JsonElement? element,
        JsonSerializerOptions options)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string rawJson = element.Value.GetRawText();

        // Remove LOConverter to avoid infinite recursion
        JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(options);

        Type targetType = GetTypeForLedgerEntry(type);

        BaseLedgerEntry result = (BaseLedgerEntry)JsonSerializer.Deserialize(rawJson, targetType, innerOptions);

        if (result != null)
        {
            // `type` (from the owning node) only selects which C# class to deserialize into, via
            // GetTypeForLedgerEntry above. It must not be stamped onto the result: rippled's wire
            // format never repeats "LedgerEntryType" inside FinalFields/PreviousFields/NewFields
            // (only the owning ModifiedNode/CreatedNode/DeletedNode carries it), so the field would
            // otherwise be a fabricated non-null value whenever it is genuinely absent from `element`.
            // The normal Deserialize call above already populates it correctly when the raw JSON does
            // contain the member.

            if (string.IsNullOrWhiteSpace(result.LedgerIndex) && element.Value.TryGetProperty("LedgerIndex", out JsonElement liEl))
            {
                result.LedgerIndex = liEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(result.Index) && element.Value.TryGetProperty("index", out JsonElement idxEl))
            {
                result.Index = idxEl.GetString();
            }
        }

        return result;
    }

    /// <summary>
    /// Writes a <see cref="BaseLedgerEntry"/> to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, BaseLedgerEntry value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Serialize the concrete runtime type to avoid infinite recursion
        JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(options);

        JsonSerializer.Serialize(writer, value, value.GetType(), innerOptions);
    }

    /// <summary>
    /// create <see cref="BaseLedgerEntry"/> by LedgerEntryType or objectType
    /// </summary>
    private static Type DetermineType(Type objectType, JsonElement root)
    {
        // Try by concrete .NET type name first
        Type byName = objectType.Name switch
        {
            "LOAccountRoot" => typeof(LOAccountRoot),
            "LOAmendments" => typeof(LOAmendments),
            "LODirectoryNode" => typeof(LODirectoryNode),
            "LOEscrow" => typeof(LOEscrow),
            "LOFeeSettings" => typeof(LOFeeSettings),
            "LOLedgerHashes" => typeof(LOLedgerHashes),
            "LOOffer" => typeof(LOOffer),
            "LOPayChannel" => typeof(LOPayChannel),
            "LORippleState" => typeof(LORippleState),
            "LOSignerList" => typeof(LOSignerList),
            "LONFTokenOffer" => typeof(LONFTokenOffer),
            "LONFTokenPage" => typeof(LONFTokenPage),
            "LOTicket" => typeof(LOTicket),
            "LONegativeUNL" => typeof(LONegativeUNL),
            "LOAmm" => typeof(LOAmm),
            "LOCheck" => typeof(LOCheck),
            "LOMPToken" => typeof(LOMPToken),
            "LOMPTokenIssuance" => typeof(LOMPTokenIssuance),
            "LOOracle" => typeof(LOOracle),
            "LODID" => typeof(LODID),
            "LOPermissionedDomain" => typeof(LOPermissionedDomain),
            "LOCredential" => typeof(LOCredential),
            "LODepositPreauth" => typeof(LODepositPreauth),
            "LOBridge" => typeof(LOBridge),
            "LOXChainOwnedClaimID" => typeof(LOXChainOwnedClaimID),
            "LOXChainOwnedCreateAccountClaimID" => typeof(LOXChainOwnedCreateAccountClaimID),
            "LODelegate" => typeof(LODelegate),
            "LOVault" => typeof(LOVault),
            "LOLoanBroker" => typeof(LOLoanBroker),
            "LOLoan" => typeof(LOLoan),
            "LOSponsorship" => typeof(LOSponsorship),
            "LOTransactionProposal" => typeof(LOTransactionProposal),
            _ => null
        };

        if (byName != null) return byName;

        // Fall back to LedgerEntryType discriminator (case-insensitive)
        string ledgerEntryType = root.TryGetProperty("LedgerEntryType", out JsonElement letEl)
            ? letEl.GetString()
            : null;

        return GetTypeForLedgerEntry(LedgerEntryTypeConverter.Parse(ledgerEntryType));
    }

    /// <summary> read <see cref="BaseLedgerEntry"/>  from json object </summary>
    public override BaseLedgerEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        Type targetType = DetermineType(typeToConvert, root);
        string rawJson = root.GetRawText();

        // Remove LOConverter to avoid infinite recursion
        JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(options);

        return (BaseLedgerEntry)JsonSerializer.Deserialize(rawJson, targetType, innerOptions);
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeof(BaseLedgerEntry).IsAssignableFrom(typeToConvert);
}
