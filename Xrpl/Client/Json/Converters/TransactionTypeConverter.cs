using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Converts <see cref="TransactionType"/> enum to/from JSON strings.
    /// Unknown enum values are deserialized as <see cref="TransactionType.Unknown"/>
    /// instead of throwing, preserving the previous behavior where
    /// <c>serializer.Populate()</c> tolerated unrecognized enum values.
    /// </summary>
    public class TransactionTypeConverter : JsonConverter<TransactionType>
    {
        /// <summary>
        /// The member for a type name, <see cref="TransactionType.Unknown"/> for a name this SDK
        /// does not know or for <see langword="null"/>.
        /// </summary>
        internal static TransactionType Parse(string name)
            => Enum.TryParse(name, ignoreCase: true, out TransactionType result) ? result : TransactionType.Unknown;

        public override TransactionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return Parse(reader.GetString());
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                if (Enum.IsDefined(typeof(TransactionType), intValue))
                    return (TransactionType)intValue;

                return TransactionType.Unknown;
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to TransactionType.");
        }

        public override void Write(Utf8JsonWriter writer, TransactionType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// Reads the <c>TransactionType</c> field into its name, by the rules of
    /// <see cref="TransactionTypeConverter"/> for everything but a string: a number becomes the
    /// member's name, and a JSON null or any other token raises <see cref="JsonException"/>.
    /// </summary>
    public class TransactionTypeNameConverter : JsonConverter<string>
    {
        private static readonly TransactionTypeConverter TypeConverter = new TransactionTypeConverter();

        /// <inheritdoc />
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String
                ? reader.GetString()
                : TypeConverter.Read(ref reader, typeof(TransactionType), options).ToString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
