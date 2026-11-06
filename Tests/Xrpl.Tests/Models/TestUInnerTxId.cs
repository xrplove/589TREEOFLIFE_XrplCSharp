using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Utils;

namespace XrplTests.Models;

/// <summary>
/// The id of an inner transaction against the hash the ledger gave it. The bytes behind the id are what an outer Batch
/// signature commits to, so a change that makes the computation cheaper has to leave them exactly as they were.
/// </summary>
[TestClass]
public class TestUInnerTxId
{
    /// <summary>Applied inside a Batch in mainnet ledger 107927291.</summary>
    [TestMethod]
    public void TestUIdOfAMainnetInnerTransactionIsTheLedgersHash()
    {
        JsonObject inner = JsonNode.Parse(
            @"{""Account"":""rJP1gFwFfnvaMhyh9xVGLBoikGDfPBSTb"",""Fee"":""0"",""Flags"":1073807360,""Sequence"":0,""SigningPubKey"":"""",
               ""TakerGets"":{""currency"":""43414D454C000000000000000000000000000000"",""issuer"":""rHLKNDsVVGZS7DVYCmkQpCWcr7deNBejsx"",""value"":""3001293797.71""},
               ""TakerPays"":""5105601"",""TicketSequence"":107698641,""TransactionType"":""OfferCreate""}")!.AsObject();

        Assert.AreEqual("0F7DA1BD062F2B9F45E44A3FC7FEC9B0764682DA27910C59FD489CC82605E149", inner.ComputeInnerTxId().ToUpperInvariant());
    }

    /// <summary>
    /// A <see cref="JsonObject"/> built in code keeps the CLR types of its values, and the codec reads it without a trip
    /// through text: the same transaction written either way has one id.
    /// </summary>
    [TestMethod]
    public void TestUValuesBuiltInCodeGiveTheIdOfTheSameTextParsed()
    {
        JsonObject built = new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
            ["Destination"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["DestinationTag"] = 7u,
            ["Amount"] = new JsonObject
            {
                ["currency"] = "USD",
                ["issuer"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                ["value"] = "1.5",
            },
            ["Fee"] = "0",
            ["Sequence"] = 1,
            ["Flags"] = 1073741824L,
            ["SigningPubKey"] = "",
        };

        JsonObject parsed = JsonNode.Parse(built.ToJsonString())!.AsObject();

        Assert.AreEqual(parsed.ComputeInnerTxId(), built.ComputeInnerTxId());
    }

    /// <summary>The object the id is computed from stays the caller's: reading it must not change it.</summary>
    [TestMethod]
    public void TestUComputingTheIdLeavesTheObjectAsItWas()
    {
        JsonObject inner = new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
            ["Destination"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["Amount"] = "1000",
            ["Fee"] = "0",
            ["Sequence"] = 1,
            ["SigningPubKey"] = "",
        };
        string before = inner.ToJsonString();

        inner.ComputeInnerTxId();

        Assert.AreEqual(before, inner.ToJsonString());
    }
}
