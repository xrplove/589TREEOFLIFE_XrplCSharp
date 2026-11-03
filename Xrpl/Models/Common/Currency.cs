using System.Text.Json.Serialization;

using System;
using System.Globalization;
using Xrpl.Amounts;
using Xrpl.Client.Exceptions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Xrpl.Client.Extensions;
using Xrpl.Utils;
using Xrpl.Models.Methods;
using Xrpl.Models.Utils;
using Xrpl.Utils.Hashes;

//https://xrpl.org/currency-formats.html#currency-formats
//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/common/index.ts

namespace Xrpl.Models.Common;

/// <summary>
/// The XRP Ledger has two kinds of digital asset: XRP and tokens.<br/>
/// Both types have high precision, although their formats are different
/// </summary>
public class Currency
{
    private string _mpTokenIssuanceId;

    /// <summary>
    /// base constructor.
    /// </summary>
    public Currency() { CurrencyCode = "XRP"; }

    /// <summary>
    /// The ID of the MPT to authorize.
    /// </summary>
    [JsonPropertyName("mpt_issuance_id")]
    public string MPTokenIssuanceID
    {
        get => _mpTokenIssuanceId;

        set
        {
            _mpTokenIssuanceId = value;
            CurrencyCode = null;
        }
    }

    /// <summary>
    /// The standard format for currency codes is a three-character string such as USD.<br/>
    /// This is intended for use with ISO 4217 Currency Codes <br/>
    /// As a 160-bit hexadecimal string, such as "0158415500000000C1F76FF6ECB0BAC600000000".<br/>
    /// The following characters are permitted:<br/>
    /// all uppercase and lowercase letters, digits, as well as the symbols ? ! @ # $ % ^ * ( ) { } [ ] | and symbols ampersand, less, greater<br/>
    /// Currency codes are case-sensitive.
    /// </summary>
    [JsonPropertyName("currency")]
    public string CurrencyCode { get; set; }

    /// <summary>
    /// Quoted decimal representation of the amount of the token.<br/>
    /// This can include scientific notation, such as 1.23e11 meaning 123,000,000,000.<br/>
    /// Both e and E may be used.<br/>
    /// This can be negative when displaying balances, but negative values are disallowed in other contexts such as specifying how much to send.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; }

    /// <summary>
    /// Generally, the account that issues this token.<br/>
    /// In special cases, this can refer to the account that holds the token instead.
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    /// <summary>
    /// Readable currency name 
    /// </summary>
    [JsonIgnore]
    public string CurrencyValidName => CurrencyCode.CurrencyReadableName();

    /// <summary>
    /// What the ledger's amount string allows: a sign, a decimal point and an exponent, and
    /// nothing else. Surrounding whitespace is not accepted, because the node never sends it.
    /// </summary>
    private const NumberStyles AmountStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>
    /// decimal currency amount (drops for XRP)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="decimal"/> does not cover the range XRPL allows an issued currency: the ledger
    /// reaches roughly <c>1e96</c> and down to <c>1e-81</c>, this type stops at about
    /// <c>7.9e28</c>. Amounts above that throw <see cref="AmountOutOfRangeException"/> rather than
    /// being answered with a number that is not the one the node sent.
    /// </para>
    /// <para>
    /// Amounts below <c>1e-28</c> return zero instead of throwing, and the asymmetry is deliberate.
    /// A balance of <c>1e-81</c> rounded to zero is zero at any scale a caller can act on, so
    /// failing over it would cost more than it protects; an amount of <c>1e96</c> reported as
    /// <c>7.9e28</c> is wrong by 67 orders of magnitude and is worth stopping for.
    /// </para>
    /// <para>
    /// Writing rounds to the ledger's sixteen significant digits; the binary codec, given a string
    /// with more than sixteen, refuses it outright. That looks inconsistent and is not: the two see
    /// different inputs. A string with seventeen digits cannot arrive from the network - rippled
    /// normalises the mantissa into <c>[1e15, 1e16)</c> before serialising - so the codec only ever
    /// meets one a caller wrote by hand, where refusing is right. This setter meets a computed
    /// <see cref="decimal"/>, which routinely carries 28 digits: <see cref="Xrpl.Sugar.AmmMath"/>
    /// returns such values. Rounding them is the service, not a loss.
    /// </para>
    /// </remarks>
    /// <exception cref="AmountOutOfRangeException">The amount exceeds what <see cref="decimal"/> can hold.</exception>
    /// <exception cref="FormatException">The amount is not a number at all.</exception>
    [JsonIgnore]
    public decimal ValueAsNumber
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                return 0;
            }

            if (decimal.TryParse(Value, AmountStyles, CultureInfo.InvariantCulture, out decimal amount))
            {
                return amount;
            }

            // Tell the two failures apart rather than reporting both as a bad format. double
            // spans the whole ledger range, so parsing there succeeds exactly when the string is
            // a real number that decimal simply cannot hold.
            //
            // IsFinite matters: double.TryParse accepts "NaN", "Infinity" and "-Infinity" whatever
            // NumberStyles it is given, because those symbols are matched separately from the
            // numeric ones. Without the check, a string that is not a quantity at all would be
            // reported as a quantity too large - which is the sort of confident wrong answer this
            // property is being changed to stop giving.
            if (double.TryParse(Value, AmountStyles, CultureInfo.InvariantCulture, out double asDouble)
                && double.IsFinite(asDouble))
            {
                throw new AmountOutOfRangeException(Value);
            }

            throw new FormatException(
                $"The amount '{Value}' is not a number in the form the XRP Ledger uses.");
        }

        set
        {
            if (CurrencyCode == "XRP")
            {
                Value = value.ToString("G0", CultureInfo.InvariantCulture);
                return;
            }

            // MPT amounts are whole numbers up to 2^63-1; G16 would turn them into exponent notation.
            if (!string.IsNullOrWhiteSpace(MPTokenIssuanceID))
            {
                if (value != Math.Truncate(value))
                {
                    throw new ArgumentException("MPT amounts must be whole numbers.", nameof(value));
                }

                Value = value.ToString("F0", CultureInfo.InvariantCulture);
                return;
            }

            // G16 keeps the sixteen significant digits the ledger allows, rounding to nearest -
            // which is what rippled does too, so this is not a place to be clever. The one input
            // it cannot serve is the top of decimal's own range: there, rounding to nearest rounds
            // *up*, past what decimal holds, and the SDK would write a string it could not read
            // back. Only then is the sixteenth digit truncated instead.
            string formatted = value.ToString("G16", CultureInfo.InvariantCulture);

            if (!decimal.TryParse(formatted, AmountStyles, CultureInfo.InvariantCulture, out _))
            {
                formatted = TruncateToLedgerPrecision(value).ToString("G16", CultureInfo.InvariantCulture);
            }

            Value = formatted;
        }
    }

    /// <summary>
    /// The same number with its digits beyond the ledger's sixteen dropped rather than rounded.
    /// </summary>
    /// <remarks>
    /// Reached only when rounding would carry the value past <see cref="decimal.MaxValue"/>.
    /// <see cref="LedgerAmountGrid.RoundDown"/> first rounds the scaled value to six places, which
    /// can in general land above the input, so it is safe here only because of where this is called:
    /// the fractional part of <c>decimal.MaxValue / 1e13</c> is .759..., outside the window that
    /// rounding can carry across, so the result stays at or below the input.
    /// </remarks>
    private static decimal TruncateToLedgerPrecision(decimal value) =>
        LedgerAmountGrid.RoundDown(value, isXrp: false);

    /// <summary>
    /// XRP token amount (non drops value)
    /// </summary>
    [JsonIgnore]
    public decimal? ValueAsXrp
    {
        get
        {
            if (CurrencyCode != "XRP" || string.IsNullOrWhiteSpace(Value))
            {
                return null;
            }

            return ValueAsNumber / 1000000;
        }
        set
        {
            if (value.HasValue)
            {
                CurrencyCode = "XRP";
                decimal val = value.Value * 1000000;
                if (val != Math.Truncate(val))
                {
                    throw new ArgumentException("XRP amounts cannot have more than 6 decimal places (drops are whole numbers).", nameof(value));
                }

                Value = val.ToString(format: "G0", CultureInfo.InvariantCulture);
            }
            else
            {
                Value = "0";
            }
        }
    }

    /// <summary>
    /// The amount as an <see cref="Xrpl.Amounts.XrplAmount"/>: exact over the ledger's whole range,
    /// where <see cref="ValueAsNumber"/> is limited to what <see cref="decimal"/> holds.
    /// </summary>
    /// <exception cref="FormatException">The value is not an amount.</exception>
    /// <exception cref="OverflowException">The value is beyond what the asset can hold.</exception>
    public Xrpl.Amounts.XrplAmount ToXrplAmount() => Xrpl.Amounts.XrplAmount.FromCurrency(this);

    #region Overrides of Object

    /// <summary>
    /// A readable form of the amount.
    /// </summary>
    /// <remarks>
    /// Falls back to the raw <see cref="Value"/> for an amount outside what <see cref="decimal"/>
    /// can hold, rather than letting <see cref="ValueAsNumber"/> throw through it. By convention
    /// <see cref="object.ToString"/> does not throw, and the places it is called from - logging,
    /// string interpolation, a debugger's watch window - are exactly where someone would be while
    /// working out why an amount is unusual. Failing there hides the value instead of showing it.
    /// </remarks>
    public override string ToString()
    {
        try
        {
            return CurrencyValidName == "XRP"
                ? $"XRP: {ValueAsXrp:0.######}"
                : $"{CurrencyValidName}: {ValueAsNumber:0.###############}";
        }
        catch (Exception exception) when (exception is AmountOutOfRangeException or FormatException)
        {
            return $"{CurrencyValidName}: {Value}";
        }
    }

    /// <summary>
    /// Two amounts are equal when they name the same asset: the same currency code and issuer, or
    /// the same MPT issuance. <see cref="Value"/> is not compared.
    /// </summary>
    /// <remarks>
    /// The MPT issuance ID is compared ignoring case because it is hex, and nodes and callers do not
    /// agree on the case they write it in.
    /// </remarks>
    public override bool Equals(object o) =>
        o is Currency model
        && model.Issuer == Issuer
        && model.CurrencyCode == CurrencyCode
        && string.Equals(model.MPTokenIssuanceID, MPTokenIssuanceID, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Issuer,
        CurrencyCode,
        MPTokenIssuanceID is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(MPTokenIssuanceID));

    public static bool operator ==(Currency? left, Currency? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }


    public static bool operator !=(Currency? left, Currency? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Implicit conversion from string → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(string value)
        => new Currency { Value = value };
    /// <summary>
    /// Implicit conversion from decimal → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(decimal value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    /// <summary>
    /// Implicit conversion from double → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(double value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    /// <summary>
    /// Implicit conversion from int → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(int value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    /// <summary>
    /// Implicit conversion from uint → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(uint value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    #endregion
}

public static class CurrencyExtensions
{
    /// <summary>
    /// The amount as a <see cref="decimal"/> (drops for XRP, as <see cref="Currency.ValueAsNumber"/> reads it),
    /// saturating at <see cref="decimal.MinValue"/> or <see cref="decimal.MaxValue"/> for an amount the ledger
    /// allows but <see cref="decimal"/> cannot hold.
    /// </summary>
    /// <remarks>
    /// For a value that is compared or displayed, where "at least this large" is the whole question: a
    /// trust line limit against a balance, the sign of an issuer's supply. It is not for an amount that goes
    /// back to the ledger; build that from <see cref="Currency.Value"/>, which is the text as the node sent
    /// it. Everything else is read as <see cref="Currency.ValueAsNumber"/> reads it, including drops for XRP
    /// and zero for an amount below 1e-28.
    /// </remarks>
    /// <param name="amount">The amount, or null.</param>
    /// <returns>The value; zero for a null amount or a blank <see cref="Currency.Value"/>.</returns>
    /// <exception cref="FormatException"><see cref="Currency.Value"/> is not a number at all.</exception>
    public static decimal ToDecimalClamped(this Currency amount)
    {
        if (amount is null)
        {
            return 0m;
        }

        try
        {
            return amount.ValueAsNumber;
        }
        catch (AmountOutOfRangeException)
        {
            // Too large rather than malformed, so the sign picks the end of the range.
            return amount.Value.AsSpan().TrimStart().StartsWith("-", StringComparison.Ordinal)
                ? decimal.MinValue
                : decimal.MaxValue;
        }
    }

    /// <summary>
    /// Returns the human-readable value of this currency amount.
    /// For XRP, returns the value in XRP (drops / 1,000,000).
    /// For tokens, returns the raw numeric value.
    /// </summary>
    /// <param name="currency">The currency to get the value from.</param>
    /// <param name="round">Optional number of decimal places to round to.</param>
    /// <returns>The decimal value, or null if the currency is null.</returns>
    public static decimal? GetValue(this Currency currency, int? round = null)
        => currency is null
            ? null
            : currency.CurrencyCode is "XRP"
                ? round is { } r1 && currency.ValueAsXrp is { } v1 ? Math.Round(v1, r1) : currency.ValueAsXrp
                : round is { } r2 ? Math.Round(currency.ValueAsNumber, r2) : currency.ValueAsNumber;

    /// <summary>
    /// Determines whether this currency represents XRP.
    /// A currency is XRP if its code is exactly "XRP" (case-sensitive) and it has no issuer.
    /// </summary>
    /// <remarks>
    /// The comparison is ordinal because the exact code "XRP" is reserved for native XRP, so an issued
    /// currency cannot use it (the JSON code "XRP" is parsed as native, and the hex form of ASCII "XRP"
    /// is refused with <c>temBAD_CURRENCY</c>): "xrp" and "Xrp" are legal issued currency codes, distinct
    /// from native XRP.
    /// An issued currency always has an issuer, so a legal IOU was never reported as XRP; what changed in
    /// 11.24.0.0 is that a currency with the code "xrp" or "Xrp" and no issuer (malformed input) used to be
    /// reported as XRP because the code was upper-cased first, and now is not.
    /// </remarks>
    /// <param name="currency">The currency to check.</param>
    /// <returns><c>true</c> if this currency is XRP; otherwise, <c>false</c>.</returns>
    public static bool IsXrp(this Currency currency)
        => currency is not null && AssetIdentity.IsXrp(currency.CurrencyCode) && currency.Issuer == null;

    /// <summary>
    /// The asset of this amount, without its value: currency code and issuer, or the MPT issuance ID.
    /// </summary>
    public static Common.IssuedCurrency ToIssued(this Currency currency) =>
        new Common.IssuedCurrency()
        {
            Currency = currency.CurrencyCode,
            Issuer = currency.Issuer,
            MptIssuanceId = currency.MPTokenIssuanceID,
        };
    /// <summary>
    /// check that currency is NFT XLS14D
    /// </summary>
    /// <param name="cur"></param>
    /// <returns></returns>
    public static bool IsNFT14D(this Currency cur) => cur is { } c && c.CurrencyCode?.StartsWith("02", StringComparison.Ordinal) == true;
    //cur is { ValueAsNumber: 0.000000000000000000000000000000000000000000000000000000000000000000000000000000001m };

    /// <summary> get readable token code </summary>
    /// <param name="currencyCode">token code</param>
    /// <returns>readable token code</returns>
    public static string CurrencyReadableName(this string currencyCode)
    {
        if (!IsValidCurrencyCode(currencyCode))
        {
            return string.Empty;
        }

        return NormalizeCurrencyCode(currencyCode);
    }

    private static bool IsValidCurrencyCode(string currencyCode) =>
        !string.IsNullOrEmpty(currencyCode) && currencyCode.Length > 0;

    public static bool IsLpToken(this Currency currency)
    {
        return currency.CurrencyCode.IsLpToken();
    }

    /// <summary>
    /// Whether this amount is a multi-purpose token - that is, whether it carries an issuance id.
    /// </summary>
    /// <remarks>
    /// An issuance id is what distinguishes a multi-purpose token from every other kind of amount,
    /// so this is exclusive with <see cref="IsXrp"/> and with an issued currency: an amount is at
    /// most one of the three.
    /// </remarks>
    public static bool IsMPTToken(this Currency currency)
    {
        return currency is not null && !string.IsNullOrWhiteSpace(currency.MPTokenIssuanceID);
    }
    public static bool IsLpToken(this TrustLine currency)
    {
        return currency.Currency.IsLpToken();
    }
    public static bool IsLpToken(this string currencyCode)
    {
        return !string.IsNullOrWhiteSpace(currencyCode) && currencyCode.IsHexCurrencyCode() && currencyCode.StartsWith("03");
    }

    public static string NormalizeCurrencyCode(this string currencyCode, int maxLength = 20)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return currencyCode;

        // Standard three-character code
        if (currencyCode.Length == 3)
        {
            return currencyCode.Trim();
        }

        // Check for a 40-character hexadecimal code
        if (currencyCode.IsHexCurrencyCode())
        {
            string hex = currencyCode;

            // Legacy demurrage code, which begins with 01
            if (hex.StartsWith("01"))
            {
                return ConvertDemurrageToUTF8(currencyCode);
            }

            // XLS-16d NFT metadata, which begins with 02
            if (hex.StartsWith("02"))
            {
                // The text is bytes 8 to 20, cut as bytes before decoding: UTF-8 text with
                // multi-byte characters decodes to fewer characters than bytes, and anyone can
                // open a trust line with such a code. maxLength counts the decoded characters.
                byte[] bytes = HexToBytes(hex);
                string text = Encoding.UTF8.GetString(bytes, 8, bytes.Length - 8);
                string xlf15d = text.Substring(0, Math.Min(Math.Max(0, maxLength), text.Length)).Trim();
                if (Regex.IsMatch(xlf15d, "[a-zA-Z0-9]{3,}") && xlf15d.ToLower() != "xrp")
                {
                    return xlf15d;
                }
            }

            if (hex.StartsWith("03"))
            {
                return $"LP {currencyCode[2..6]}..";
            }

            // An ordinary hexadecimal code
            var decodedHex = hex.FromHexString().Replace("\0", null).Trim('\0');
            if (string.IsNullOrWhiteSpace(decodedHex))
            {
                return currencyCode;
            }
            return decodedHex;
        }

        return currencyCode;
    }

    public static bool NormalCurrency(this string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return false;

        // Standard three-character code
        if (currencyCode.Length == 3 && currencyCode.Trim().ToLower() != "xrp")
        {
            return true;
        }

        // Check for a 40-character hexadecimal code
        if (currencyCode.IsHexCurrencyCode())
        {
            string hex = currencyCode;

            // Legacy demurrage code, which begins with 01
            if (hex.StartsWith("01"))
            {
                return false;
            }

            // XLS-16d NFT metadata, which begins with 02
            if (hex.StartsWith("02"))
            {
                return false;
            }

            if (hex.StartsWith("03"))
            {
                return false;
            }

            // An ordinary hexadecimal code
            return true;
        }

        return false;
    }
    static string ConvertDemurrageToUTF8(string demurrageCode)
    {
        byte[] bytes = HexToBytes(demurrageCode);
        string code = $"{(char)bytes[1]}{(char)bytes[2]}{(char)bytes[3]}";

        // Interest rate computation
        int interestStart = (bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7];
        double interestPeriod = BitConverter.ToDouble(bytes.Skip(8).Take(8).Reverse().ToArray(), 0);
        const int yearSeconds = 31536000; // A fixed number of seconds in a year
        double interestAfterYear = Math.Pow(Math.E, (interestStart + yearSeconds - interestStart) / interestPeriod);
        double interest = (interestAfterYear * 100) - 100;

        return $"{code} ({interest:F1}% pa)";
    }
    static byte[] HexToBytes(string hex)
    {
        return Enumerable.Range(0, hex.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
            .ToArray();
    }

}