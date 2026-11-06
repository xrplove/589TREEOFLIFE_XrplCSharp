using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Common;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class AmountMapperTests
{
    [TestMethod]
    public void TestUMapsXrpDrops()
    {
        PaymentRequirement req = new() { Asset = "XRP", Amount = "1500000" };
        Currency c = X402AmountMapper.ToCurrency(req);
        Assert.AreEqual("XRP", c.CurrencyCode);
        Assert.AreEqual("1500000", c.Value);
        Assert.IsNull(c.Issuer);
    }

    [TestMethod]
    public void TestUMapsIouWithIssuer()
    {
        PaymentRequirement req = new()
        {
            Asset = "524C555344000000000000000000000000000000", Amount = "2.50",
            Extra = new() { ["issuer"] = System.Text.Json.JsonDocument.Parse("\"rIssuer\"").RootElement }
        };
        Currency c = X402AmountMapper.ToCurrency(req);
        Assert.AreEqual("524C555344000000000000000000000000000000", c.CurrencyCode);
        Assert.AreEqual("rIssuer", c.Issuer);
        Assert.AreEqual("2.50", c.Value);
    }

    [TestMethod]
    public void TestUIouWithoutIssuerThrows()
    {
        PaymentRequirement req = new() { Asset = "USD", Amount = "1" };
        Assert.ThrowsExactly<X402PaymentException>(() => X402AmountMapper.ToCurrency(req));
    }

    [TestMethod]
    public void TestUMapsLowercaseXrpAsAnIssuedCurrency()
    {
        PaymentRequirement req = new()
        {
            Asset = "xrp", Amount = "2.50",
            Extra = new() { ["issuer"] = System.Text.Json.JsonDocument.Parse("\"rIssuer\"").RootElement }
        };
        Currency c = X402AmountMapper.ToCurrency(req);
        Assert.IsFalse(X402AmountMapper.IsXrp(req));
        Assert.AreEqual("xrp", c.CurrencyCode);
        Assert.AreEqual("rIssuer", c.Issuer);
    }

    [TestMethod]
    public void TestULowercaseXrpWithoutIssuerThrows()
    {
        PaymentRequirement req = new() { Asset = "xrp", Amount = "1000000" };
        Assert.ThrowsExactly<X402PaymentException>(() => X402AmountMapper.ToCurrency(req));
    }
}
