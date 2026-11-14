using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.Models;

/// <summary>channel_authorize takes exactly one of secret, seed, seed_hex or passphrase.</summary>
[TestClass]
public class TestUChannelAuthorize
{
    [TestMethod]
    public void SeedHex_IsWrittenAsSeedHex_AndAloneAmongTheKeyFields()
    {
        ChannelAuthorizeRequest request = new ChannelAuthorizeRequest
        {
            ChannelId = "5DB01B7FFED6B67E6B0414DED11E051D2EE2B7619CE0EAA6286D67A3A4D5BDB3",
            SeedHex = "1F1F1F1F1F1F1F1F1F1F1F1F1F1F1F1F",
            KeyType = "secp256k1",
            Amount = 1000000,
        };

        JsonObject json = JsonNode.Parse(JsonSerializer.Serialize(request, XrplJsonOptions.Default))!.AsObject();

        Assert.AreEqual("channel_authorize", json["command"]!.GetValue<string>());
        Assert.AreEqual("1F1F1F1F1F1F1F1F1F1F1F1F1F1F1F1F", json["seed_hex"]!.GetValue<string>());
        Assert.AreEqual("secp256k1", json["key_type"]!.GetValue<string>());
        Assert.AreEqual("1000000", json["amount"]!.GetValue<string>());
        Assert.IsFalse(json.ContainsKey("secret"));
        Assert.IsFalse(json.ContainsKey("seed"));
        Assert.IsFalse(json.ContainsKey("passphrase"));
    }
}
