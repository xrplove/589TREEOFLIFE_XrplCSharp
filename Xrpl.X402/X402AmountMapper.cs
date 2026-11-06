using System.Text.Json;
using Xrpl.Models.Common;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

/// <summary>
/// Converts a <see cref="PaymentRequirement"/> amount into an XRPL <see cref="Currency"/> value,
/// handling both native XRP (drops) and IOU/token assets.
/// </summary>
public static class X402AmountMapper
{
    /// <summary>
    /// Returns <c>true</c> when the requirement's asset is native XRP. The code is compared exactly:
    /// the ledger reserves <c>XRP</c> alone, and <c>xrp</c> is a legal issued currency code.
    /// </summary>
    /// <param name="req">The payment requirement to inspect.</param>
    /// <returns><c>true</c> for XRP; <c>false</c> for IOU/token assets.</returns>
    public static bool IsXrp(PaymentRequirement req) => AssetIdentity.IsXrp(req.Asset);

    /// <summary>
    /// Converts the requirement's amount and asset to an XRPL <see cref="Currency"/> instance.
    /// For XRP, the amount is expressed in drops. For IOU assets, <c>extra.issuer</c> must be present.
    /// </summary>
    /// <param name="req">The payment requirement containing asset, amount, and extra fields.</param>
    /// <returns>An XRPL <see cref="Currency"/> ready to set as the <c>Amount</c> of a <c>Payment</c> transaction.</returns>
    /// <exception cref="X402PaymentException">Thrown when an IOU requirement is missing <c>extra.issuer</c>.</exception>
    public static Currency ToCurrency(PaymentRequirement req)
    {
        if (IsXrp(req))
            return new Currency { CurrencyCode = "XRP", Value = req.Amount };

        string issuer = req.Extra.TryGetValue("issuer", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()!
            : throw new X402PaymentException("invalid_requirement", "IOU asset requires extra.issuer");

        return new Currency { CurrencyCode = req.Asset, Issuer = issuer, Value = req.Amount };
    }
}
