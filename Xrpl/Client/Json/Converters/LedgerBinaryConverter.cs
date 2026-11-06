using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Models.Ledger;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// <see cref="LedgerEntity"/> or  <see cref="LedgerBinaryEntity"/>  converter
    /// </summary>
    public class LedgerBinaryConverter : JsonConverter<IBaseLedgerEntity>
    {
        /// <summary>
        /// Writes a <see cref="LedgerEntity"/> or <see cref="LedgerBinaryEntity"/> to JSON.
        /// Null fields are ignored based on the serializer settings.
        /// </summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The <see cref="LedgerEntity"/> or <see cref="LedgerBinaryEntity"/> to serialize.</param>
        /// <param name="options">The JSON serializer options.</param>
        public override void Write(Utf8JsonWriter writer, IBaseLedgerEntity value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // Serialize the concrete type to avoid infinite recursion
            JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<LedgerBinaryConverter>(options);

            if (value is LedgerBinaryEntity binaryEntity)
                JsonSerializer.Serialize(writer, binaryEntity, innerOptions);
            else if (value is LedgerEntity ledgerEntity)
                JsonSerializer.Serialize(writer, ledgerEntity, innerOptions);
            else
                JsonSerializer.Serialize(writer, value, value.GetType(), innerOptions);
        }

        /// <summary>
        /// create <see cref="LedgerEntity"/> or  <see cref="LedgerBinaryEntity"/> 
        /// </summary>
        private static Type DetermineType(Utf8JsonReader reader)
        {
            return JsonMemberReader.TryFind(reader, "ledger_data"u8, out _)
                ? typeof(LedgerBinaryEntity)
                : typeof(LedgerEntity);
        }

        /// <summary> read <see cref="LedgerEntity"/> or  <see cref="LedgerBinaryEntity"/>  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="typeToConvert">target type</param>
        /// <param name="options">json serializer options</param>
        /// <returns><see cref="LedgerEntity"/> or  <see cref="LedgerBinaryEntity"/> </returns>
        public override IBaseLedgerEntity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Remove this converter to avoid infinite recursion
            JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<LedgerBinaryConverter>(options);

            Type targetType = DetermineType(reader);
            return (IBaseLedgerEntity)JsonSerializer.Deserialize(ref reader, targetType, innerOptions);
        }

        public override bool CanConvert(Type typeToConvert) =>
            typeof(IBaseLedgerEntity).IsAssignableFrom(typeToConvert);
    }
}
