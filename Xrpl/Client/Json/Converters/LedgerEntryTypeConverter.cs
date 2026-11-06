using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Converts <see cref="LedgerEntryType"/> enum to/from JSON strings.
    /// Unknown enum values are deserialized as <see cref="LedgerEntryType.Unknown"/>
    /// instead of throwing, preserving forward compatibility with new ledger object types.
    /// </summary>
    public class LedgerEntryTypeConverter : JsonConverter<LedgerEntryType>
    {
        /// <summary>
        /// The member for a type name, <see cref="LedgerEntryType.Unknown"/> for a name this SDK
        /// does not know or for <see langword="null"/>.
        /// </summary>
        internal static LedgerEntryType Parse(string name)
            => Enum.TryParse(name, ignoreCase: true, out LedgerEntryType result) ? result : LedgerEntryType.Unknown;

        public override LedgerEntryType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return Parse(reader.GetString());
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                if (Enum.IsDefined(typeof(LedgerEntryType), intValue))
                    return (LedgerEntryType)intValue;

                return LedgerEntryType.Unknown;
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to LedgerEntryType.");
        }

        public override void Write(Utf8JsonWriter writer, LedgerEntryType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// Reads the <c>LedgerEntryType</c> field into its name, by the rules of
    /// <see cref="LedgerEntryTypeConverter"/> for everything but a string: a number becomes the
    /// member's name.
    /// </summary>
    public class LedgerEntryTypeNameConverter : JsonConverter<string>
    {
        private static readonly LedgerEntryTypeConverter TypeConverter = new LedgerEntryTypeConverter();

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String
                ? reader.GetString()
                : TypeConverter.Read(ref reader, typeof(LedgerEntryType), options).ToString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
