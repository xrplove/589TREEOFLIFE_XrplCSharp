using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

// A type this SDK does not know reads as the Unknown enum member. The name the node sent stays
// on the object, through both the read and the write, for transactions, ledger entries and
// metadata nodes alike.
[TestClass]
public class TestUUnknownTypeName
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;
    private static readonly JsonSerializerOptions PlainOptions = new JsonSerializerOptions();

    private const string UnknownTransaction = @"{
        ""TransactionType"": ""FutureAmendmentSet"",
        ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
        ""Fee"": ""10"",
        ""Sequence"": 5
    }";

    private const string UnknownLedgerEntry = @"{
        ""LedgerEntryType"": ""FutureObject"",
        ""Flags"": 0,
        ""index"": ""ABCDEF""
    }";

    [TestMethod]
    public void Response_UnknownType_KeepsTheNameTheNodeSent()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(UnknownTransaction, Options);

        Assert.AreEqual(TransactionType.Unknown, result.TransactionType);
        Assert.AreEqual("FutureAmendmentSet", result.TransactionTypeName);
    }

    [TestMethod]
    public void Response_UnknownType_WritesTheNameBack()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(UnknownTransaction, Options);

        JsonNode written = JsonNode.Parse(result.ToJson());

        Assert.AreEqual("FutureAmendmentSet", written["TransactionType"].GetValue<string>());
    }

    [TestMethod]
    public void Response_KnownType_NameIsTheEnumMember()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(
            @"{""TransactionType"":""OfferCancel"",""Account"":""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh""}", Options);

        Assert.AreEqual(TransactionType.OfferCancel, result.TransactionType);
        Assert.AreEqual("OfferCancel", result.TransactionTypeName);
    }

    [TestMethod]
    public void Response_KnownTypeInAnotherCase_NameIsTheEnumMember()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(
            @"{""TransactionType"":""offercancel"",""Account"":""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh""}", Options);

        Assert.AreEqual(TransactionType.OfferCancel, result.TransactionType);
        Assert.AreEqual("OfferCancel", result.TransactionTypeName);
    }

    [TestMethod]
    public void Response_MissingType_NameIsTheUnknownMember()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(
            @"{""Account"":""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh""}", Options);

        Assert.AreEqual(TransactionType.Unknown, result.TransactionType);
        Assert.AreEqual(nameof(TransactionType.Unknown), result.TransactionTypeName);
    }

    [TestMethod]
    public void Response_TypeAssignedAfterReading_ReplacesTheName()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(UnknownTransaction, Options);

        result.TransactionType = TransactionType.Payment;

        Assert.AreEqual("Payment", result.TransactionTypeName);
    }

    [TestMethod]
    public void Response_NameAssigned_SetsTheEnumMember()
    {
        TransactionResponse result = new TransactionResponse { TransactionTypeName = "Payment" };

        Assert.AreEqual(TransactionType.Payment, result.TransactionType);
    }

    [TestMethod]
    public void Request_UnknownType_KeepsTheNameTheNodeSent()
    {
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(UnknownTransaction, Options);

        Assert.AreEqual(TransactionType.Unknown, result.TransactionType);
        Assert.AreEqual("FutureAmendmentSet", result.TransactionTypeName);
    }

    [TestMethod]
    public void Request_UnknownType_WritesTheNameBack()
    {
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(UnknownTransaction, Options);

        JsonNode written = JsonNode.Parse(result.ToJson());

        Assert.AreEqual("FutureAmendmentSet", written["TransactionType"].GetValue<string>());
    }

    [TestMethod]
    public void Request_BuiltInCode_NameIsTheEnumMember()
    {
        OfferCancel request = new OfferCancel();

        Assert.AreEqual("OfferCancel", request.TransactionTypeName);
        Assert.AreEqual("OfferCancel", JsonNode.Parse(request.ToJson())["TransactionType"].GetValue<string>());
    }

    [TestMethod]
    public void Request_MissingType_NameIsTheUnknownMember()
    {
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(
            @"{""Account"":""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh""}", Options);

        Assert.AreEqual(TransactionType.Unknown, result.TransactionType);
        Assert.AreEqual(nameof(TransactionType.Unknown), result.TransactionTypeName);
    }

    [TestMethod]
    public void TransactionTypeName_HeldAsTheCommonInterface_IsNotWrittenAsAFieldOfItsOwn()
    {
        ITransactionCommon held = new OfferCancel();

        JsonNode written = JsonNode.Parse(JsonSerializer.Serialize(held, Options));

        Assert.IsNull(written["TransactionTypeName"]);
    }

    [TestMethod]
    public void TransactionTypeName_HasADefault_SoAnImplementationOutsideTheSdkStillCompiles()
    {
        // StaticBit.XrplHelper's tests hold a bare ITransactionCommon; an abstract member broke them.
        PropertyInfo property = typeof(ITransactionCommon).GetProperty(nameof(ITransactionCommon.TransactionTypeName));

        Assert.IsFalse(property.GetMethod.IsAbstract);
    }

    [TestMethod]
    public void LedgerEntry_UnknownType_KeepsTheNameTheNodeSent()
    {
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(UnknownLedgerEntry, Options);

        Assert.AreEqual(LedgerEntryType.Unknown, result.LedgerEntryType);
        Assert.AreEqual("FutureObject", result.LedgerEntryTypeName);
    }

    [TestMethod]
    public void LedgerEntry_UnknownType_WritesTheNameBack()
    {
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(UnknownLedgerEntry, Options);

        JsonNode written = JsonNode.Parse(JsonSerializer.Serialize(result, Options));

        Assert.AreEqual("FutureObject", written["LedgerEntryType"].GetValue<string>());
    }

    [TestMethod]
    public void LedgerEntry_KnownType_NameIsTheEnumMember()
    {
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(
            @"{""LedgerEntryType"":""Ticket"",""index"":""ABCDEF""}", Options);

        Assert.IsInstanceOfType(result, typeof(LOTicket));
        Assert.AreEqual(LedgerEntryType.Ticket, result.LedgerEntryType);
        Assert.AreEqual("Ticket", result.LedgerEntryTypeName);
    }

    [TestMethod]
    public void LedgerEntry_NoTypeInTheJson_HasNoName()
    {
        // FinalFields and PreviousFields do not repeat the type: only the owning node carries it.
        BaseLedgerEntry result = ReadFields(LedgerEntryType.Unknown, @"{""Flags"":0}");

        Assert.IsNull(result.LedgerEntryType);
        Assert.IsNull(result.LedgerEntryTypeName);
    }

    [TestMethod]
    public void LedgerEntry_TypeAssignedAfterReading_ReplacesTheName()
    {
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(UnknownLedgerEntry, Options);

        result.LedgerEntryType = LedgerEntryType.Ticket;

        Assert.AreEqual("Ticket", result.LedgerEntryTypeName);
    }

    [TestMethod]
    public void ModifiedNode_UnknownType_KeepsAndWritesTheName()
    {
        ModifiedNode node = JsonSerializer.Deserialize<ModifiedNode>(
            @"{""LedgerEntryType"":""FutureObject"",""LedgerIndex"":""ABCDEF"",""FinalFields"":{""Flags"":0}}", Options);

        Assert.AreEqual(LedgerEntryType.Unknown, node.LedgerEntryType);
        Assert.AreEqual("FutureObject", node.LedgerEntryTypeName);
        Assert.AreEqual("FutureObject", WrittenType(node));
    }

    [TestMethod]
    public void CreatedNode_UnknownType_KeepsAndWritesTheName()
    {
        CreatedNode node = JsonSerializer.Deserialize<CreatedNode>(
            @"{""LedgerEntryType"":""FutureObject"",""LedgerIndex"":""ABCDEF"",""NewFields"":{""Flags"":0}}", Options);

        Assert.AreEqual(LedgerEntryType.Unknown, node.LedgerEntryType);
        Assert.AreEqual("FutureObject", node.LedgerEntryTypeName);
        Assert.AreEqual("FutureObject", WrittenType(node));
    }

    [TestMethod]
    public void DeletedNode_UnknownType_KeepsAndWritesTheName()
    {
        DeletedNode node = JsonSerializer.Deserialize<DeletedNode>(
            @"{""LedgerEntryType"":""FutureObject"",""LedgerIndex"":""ABCDEF"",""FinalFields"":{""Flags"":0}}", Options);

        Assert.AreEqual(LedgerEntryType.Unknown, node.LedgerEntryType);
        Assert.AreEqual("FutureObject", node.LedgerEntryTypeName);
        Assert.AreEqual("FutureObject", WrittenType(node));
    }

    [TestMethod]
    public void Node_KnownType_NameIsTheEnumMember()
    {
        ModifiedNode node = JsonSerializer.Deserialize<ModifiedNode>(
            @"{""LedgerEntryType"":""Ticket"",""LedgerIndex"":""ABCDEF""}", Options);

        Assert.AreEqual(LedgerEntryType.Ticket, node.LedgerEntryType);
        Assert.AreEqual("Ticket", node.LedgerEntryTypeName);
    }

    [TestMethod]
    public void Node_MissingType_NameIsTheUnknownMember()
    {
        ModifiedNode node = JsonSerializer.Deserialize<ModifiedNode>(@"{""LedgerIndex"":""ABCDEF""}", Options);

        Assert.AreEqual(LedgerEntryType.Unknown, node.LedgerEntryType);
        Assert.AreEqual(nameof(LedgerEntryType.Unknown), node.LedgerEntryTypeName);
    }

    [TestMethod]
    public void Transaction_NullType_IsRefused()
    {
        const string json = @"{""TransactionType"":null,""Account"":""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh""}";

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ITransactionResponse>(json, Options));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ITransactionRequest>(json, Options));
    }

    [TestMethod]
    public void Transaction_NumberReadWithoutThePolymorphicConverter_IsTheEnumMember()
    {
        string json = $@"{{""TransactionType"":{(int)TransactionType.OfferCancel}}}";

        OfferCancelResponse result = JsonSerializer.Deserialize<OfferCancelResponse>(json, PlainOptions);

        Assert.AreEqual(TransactionType.OfferCancel, result.TransactionType);
        Assert.AreEqual("OfferCancel", result.TransactionTypeName);
    }

    [TestMethod]
    public void Transaction_UnknownNumberReadWithoutThePolymorphicConverter_IsUnknown()
    {
        OfferCancelResponse result = JsonSerializer.Deserialize<OfferCancelResponse>(@"{""TransactionType"":99999}", PlainOptions);

        Assert.AreEqual(TransactionType.Unknown, result.TransactionType);
    }

    [TestMethod]
    public void LedgerEntry_NullType_HasNoType()
    {
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(@"{""LedgerEntryType"":null,""index"":""ABCDEF""}", Options);

        Assert.IsNull(result.LedgerEntryType);
        Assert.IsNull(result.LedgerEntryTypeName);
    }

    [TestMethod]
    public void LedgerEntry_NumberReadWithoutThePolymorphicConverter_IsTheEnumMember()
    {
        string json = $@"{{""LedgerEntryType"":{(int)LedgerEntryType.Ticket}}}";

        LOTicket result = JsonSerializer.Deserialize<LOTicket>(json, PlainOptions);

        Assert.AreEqual(LedgerEntryType.Ticket, result.LedgerEntryType);
        Assert.AreEqual("Ticket", result.LedgerEntryTypeName);
    }

    private static BaseLedgerEntry ReadFields(LedgerEntryType type, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        return LOConverter.GetBaseRippleLO(type, document.RootElement, Options);
    }

    private static string WrittenType<TNode>(TNode node)
        => JsonNode.Parse(JsonSerializer.Serialize(node, Options))["LedgerEntryType"].GetValue<string>();
}
