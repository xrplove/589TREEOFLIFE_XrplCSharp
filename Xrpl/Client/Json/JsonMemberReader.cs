using System;
using System.Text.Json;

namespace Xrpl.Client.Json
{
    /// <summary>
    /// Looks at one member of the JSON object a polymorphic converter is about to read, without taking a copy of it.
    /// </summary>
    /// <remarks>
    /// A converter that picks the concrete type from a member has to see that member before it reads the object. Doing that
    /// through a <see cref="JsonDocument"/> parses the value a second time and, with <c>GetRawText</c>, turns it into a
    /// string and back to UTF-8 on top. Nested converters repeat all of it, and a ledger of a thousand transactions is
    /// a few megabytes of JSON. A <see cref="Utf8JsonReader"/> is a struct: the copy taken here walks the object while
    /// the caller's reader stays on its start, ready for <c>JsonSerializer.Deserialize(ref reader, ...)</c>.
    /// </remarks>
    internal static class JsonMemberReader
    {
        /// <summary>
        /// Finds a member of the object <paramref name="reader"/> stands on. As with <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>,
        /// the last of several members of that name is the one found.
        /// </summary>
        /// <param name="reader">A copy of the converter's reader, standing on the value to look into.</param>
        /// <param name="name">The name of the member in UTF-8.</param>
        /// <param name="value">A reader standing on the value of the member, when there is one.</param>
        /// <returns><see langword="true"/> when the value is an object with such a member.</returns>
        public static bool TryFind(Utf8JsonReader reader, ReadOnlySpan<byte> name, out Utf8JsonReader value)
        {
            value = default;

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            bool found = false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool wanted = reader.ValueTextEquals(name);

                reader.Read();

                if (wanted)
                {
                    value = reader;
                    found = true;
                }

                // TrySkip and not Skip: a stream hands a converter its whole value on a reader that is not on the final
                // block, and Skip refuses every such reader.
                if (!reader.TrySkip())
                {
                    throw new JsonException("The object to look into is not whole.");
                }
            }

            return found;
        }
    }
}
