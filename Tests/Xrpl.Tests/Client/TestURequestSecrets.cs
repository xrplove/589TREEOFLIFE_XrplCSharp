using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Methods;

using TimeoutException = Xrpl.Client.Exceptions.TimeoutException;

namespace Xrpl.Tests.ClientLib;

/// <summary>
/// A request can carry a secret (channel_authorize takes one), and an exception message ends up
/// in logs and crash reports, so a timeout names the command and never the request body.
/// </summary>
[TestClass]
public class TestURequestSecrets
{
    private const string Secret = "snoPBrXtMeMyMHUVTgbuqAfg1SUTb";

    [TestMethod]
    public void Timeout_TypedRequest_DoesNotEchoTheSecret()
    {
        RequestManager manager = new RequestManager();
        RequestManager.XrplGRequest pending = manager.CreateGRequest<ChannelAuthorizeResponse, ChannelAuthorizeRequest>(
            new ChannelAuthorizeRequest { ChannelId = new string('A', 64), Secret = Secret, Amount = 1 },
            TimeSpan.FromMilliseconds(50));

        TimeoutException exception = Assert.ThrowsExactly<TimeoutException>(() => pending.Promise.GetAwaiter().GetResult());

        StringAssert.Contains(exception.Message, "channel_authorize");
        Assert.IsFalse(exception.Message.Contains(Secret), exception.Message);
    }

    [TestMethod]
    public void Timeout_DictionaryRequest_DoesNotEchoTheSecret()
    {
        RequestManager manager = new RequestManager();
        RequestManager.XrplRequest pending = manager.CreateRequest(
            new Dictionary<string, object> { ["command"] = "channel_authorize", ["secret"] = Secret, ["amount"] = "1" },
            TimeSpan.FromMilliseconds(50));

        TimeoutException exception = Assert.ThrowsExactly<TimeoutException>(() => pending.Promise.GetAwaiter().GetResult());

        StringAssert.Contains(exception.Message, "channel_authorize");
        Assert.IsFalse(exception.Message.Contains(Secret), exception.Message);
    }

    [TestMethod]
    public void InvalidSeedCharacter_DoesNotEchoTheCharacter()
    {
        // '0' is not in the base58 alphabet.
        string seed = "snoPBrXtMeMyMHUVTgbuqAfg1SU0b";

        Exception exception = Assert.ThrowsExactly<Xrpl.AddressCodec.EncodingFormatException>(() => Xrpl.AddressCodec.XrplCodec.DecodeSeed(seed));

        Assert.IsFalse(exception.Message.Contains("snoPB"), exception.Message);
        StringAssert.Contains(exception.Message, "index 27");
    }
}
