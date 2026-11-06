using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json;

/// <summary>
/// The polymorphic converters pick their type from a member of the object they are about to read, and read the object
/// afterwards with the same reader. These pin what used to come for free from a <see cref="JsonDocument"/>.
/// </summary>
[TestClass]
public class TestUJsonMemberReader
{
    private static Utf8JsonReader Start(string json)
    {
        Utf8JsonReader reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        return reader;
    }

    [TestMethod]
    public void TryFind_MemberThere_StandsOnItsValue()
    {
        Utf8JsonReader reader = Start(@"{""a"":{""b"":1},""TransactionType"":""Payment"",""z"":[1,2]}");

        Assert.IsTrue(JsonMemberReader.TryFind(reader, "TransactionType"u8, out Utf8JsonReader value));
        Assert.AreEqual("Payment", value.GetString());
    }

    [TestMethod]
    public void TryFind_MemberOnlyInANestedObject_IsNotFound()
    {
        Utf8JsonReader reader = Start(@"{""a"":{""TransactionType"":""Payment""}}");

        Assert.IsFalse(JsonMemberReader.TryFind(reader, "TransactionType"u8, out _));
    }

    [TestMethod]
    public void TryFind_MemberRepeated_FindsTheLast()
    {
        Utf8JsonReader reader = Start(@"{""t"":""first"",""t"":""last""}");

        Assert.IsTrue(JsonMemberReader.TryFind(reader, "t"u8, out Utf8JsonReader value));
        Assert.AreEqual("last", value.GetString());
    }

    [TestMethod]
    public void TryFind_ValueIsNoObject_FindsNothing()
    {
        Utf8JsonReader reader = Start("[1,2]");

        Assert.IsFalse(JsonMemberReader.TryFind(reader, "t"u8, out _));
    }

    [TestMethod]
    public void TryFind_LeavesTheCallersReaderOnTheStartOfTheObject()
    {
        Utf8JsonReader reader = Start(@"{""t"":1}");

        JsonMemberReader.TryFind(reader, "t"u8, out _);

        Assert.AreEqual(JsonTokenType.StartObject, reader.TokenType);
    }

    [TestMethod]
    public void Request_TypeRepeated_TheLastPicksTheModel()
    {
        ITransactionRequest transaction = JsonSerializer.Deserialize<ITransactionRequest>(
            @"{""TransactionType"":""Payment"",""TransactionType"":""OfferCancel"",""Account"":""rA""}",
            XrplJsonOptions.Default);

        Assert.IsInstanceOfType(transaction, typeof(OfferCancel));
    }

    [TestMethod]
    public void Response_TheFollowingMemberIsReadFromWhereTheConverterStopped()
    {
        Pair pair = JsonSerializer.Deserialize<Pair>(
            @"{""First"":{""TransactionType"":""Payment"",""Account"":""rA""},""Second"":{""TransactionType"":""OfferCancel"",""Account"":""rB""}}",
            XrplJsonOptions.Default);

        Assert.IsInstanceOfType(pair.First, typeof(PaymentResponse));
        Assert.AreEqual("rA", pair.First.Account);
        Assert.IsInstanceOfType(pair.Second, typeof(OfferCancelResponse));
        Assert.AreEqual("rB", pair.Second.Account);
    }

    [TestMethod]
    public void Response_FailureInTheModel_StillNamesTheTransaction()
    {
        string hash = new string('C', 64);

        JsonException failure = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ITransactionResponse>(
            $@"{{""TransactionType"":""Payment"",""hash"":""{hash}"",""Sequence"":""not a number""}}",
            XrplJsonOptions.Default));

        StringAssert.Contains(failure.Message, hash);
    }

    [TestMethod]
    public void Ledger_ReadStraightFromTheReader_KeepsItsTransactions()
    {
        LOLedger answer = JsonSerializer.Deserialize<LOLedger>(
            @"{""ledger"":{""ledger_hash"":""AB"",""transactions"":[{""hash"":""CD"",""tx_json"":{""TransactionType"":""OfferCancel"",""Account"":""rA""}}]},""validated"":true}",
            XrplJsonOptions.Default);

        LedgerEntity ledger = (LedgerEntity)answer.LedgerEntity;

        Assert.AreEqual("AB", ledger.LedgerHash);
        Assert.IsTrue(answer.Validated);
        Assert.IsInstanceOfType(ledger.Transactions[0].Transaction.Transaction, typeof(OfferCancel));
    }

    [TestMethod]
    public async Task Stream_SmallBuffer_ConvertersReadTheirValueAsFromMemory()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(XrplJsonOptions.Default) { DefaultBufferSize = 16 };
        byte[] json = Encoding.UTF8.GetBytes(
            @"{""ledger"":{""ledger_hash"":""AB"",""transactions"":[{""hash"":""CD"",""tx_json"":{""Account"":""rA"",""Flags"":0,""TransactionType"":""OfferCancel""}}]}}");

        using MemoryStream stream = new MemoryStream(json);
        LOLedger answer = await JsonSerializer.DeserializeAsync<LOLedger>(stream, options);

        LedgerEntity ledger = (LedgerEntity)answer.LedgerEntity;
        Assert.AreEqual("AB", ledger.LedgerHash);
        Assert.IsInstanceOfType(ledger.Transactions[0].Transaction.Transaction, typeof(OfferCancel));
    }

    [TestMethod]
    public async Task Stream_SmallBuffer_ResponseKeepsItsModel()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(XrplJsonOptions.Default) { DefaultBufferSize = 16 };
        byte[] json = Encoding.UTF8.GetBytes(@"{""Account"":""rA"",""Flags"":0,""Sequence"":7,""TransactionType"":""OfferCancel""}");

        using MemoryStream stream = new MemoryStream(json);
        ITransactionResponse transaction = await JsonSerializer.DeserializeAsync<ITransactionResponse>(stream, options);

        Assert.IsInstanceOfType(transaction, typeof(OfferCancelResponse));
        Assert.AreEqual("rA", transaction.Account);
    }

    private sealed class Pair
    {
        public ITransactionResponse First { get; set; }

        public ITransactionResponse Second { get; set; }
    }
}
