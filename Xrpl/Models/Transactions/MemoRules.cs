using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The limits a node puts on <c>Memos</c> before it will accept a transaction at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are local checks in rippled's <c>passesLocalChecks</c> → <c>isMemoOkay</c>
    /// (<c>src/libxrpl/protocol/STTx.cpp</c>). A transaction that fails them is not relayed, never
    /// reaches a ledger and costs no fee - but the consumer only finds out after building,
    /// autofilling and signing it, and the refusal does not say which field was at fault. Checking
    /// the same rules before signing turns that into an exception that names the problem.
    /// </para>
    /// <para>
    /// Two of the five rules the node applies are already enforced by the binary codec, so they are
    /// deliberately not repeated here: a member other than <c>MemoType</c>, <c>MemoData</c> or
    /// <c>MemoFormat</c> inside a <c>Memo</c> is refused when the blob is built, and so is a value
    /// that is not hex. Re-checking them would mean two places to keep in step with one truth.
    /// </para>
    /// </remarks>
    public static class MemoRules
    {
        /// <summary>
        /// The largest the serialized <c>Memos</c> array may be, in bytes.
        /// </summary>
        /// <remarks>
        /// The limit is on the array as a whole, so several memos do not raise it. In practice this
        /// leaves 1019 bytes of <c>MemoData</c> in a single memo carrying nothing else.
        /// </remarks>
        public const int MaxSerializedLength = 1024;

        /// <summary>
        /// Characters a decoded <c>MemoType</c> or <c>MemoFormat</c> may consist of - the ones RFC
        /// 3986 allows in a URL. <c>MemoData</c> is exempt: it carries arbitrary bytes.
        /// </summary>
        private const string UrlSafeCharacters =
            "0123456789" +
            "-._~:/?#[]@!$&'()*+,;=%" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz";

        private static readonly bool[] AllowedInMemoType = BuildAllowedTable();

        /// <summary>
        /// Checks a transaction's <c>Memos</c> against the rules a node applies locally.
        /// </summary>
        /// <param name="memos">The value of the transaction's <c>Memos</c> field, or <c>null</c>.</param>
        /// <exception cref="ValidationException">
        /// When the array is too large, an element is not a <c>Memo</c> object, or a
        /// <c>MemoType</c>/<c>MemoFormat</c> decodes to something a URL may not contain.
        /// </exception>
        public static void Validate(object memos)
        {
            if (memos is null)
            {
                return;
            }

            JsonNode node = memos as JsonNode ?? JsonSerializer.SerializeToNode(memos, XrplJsonOptions.Default);

            // Anything that is not an array of well-formed Memo objects is refused by the codec a
            // moment later, and refused better: it names the member at fault. These rules only add
            // what nothing else checks, so a shape they cannot read is left alone rather than
            // reported here in poorer words.
            if (node is not JsonArray array || array.Count == 0)
            {
                return;
            }

            bool everyElementIsAMemo = true;
            foreach (JsonNode element in array)
            {
                JsonObject memo = TryUnwrapMemo(element);
                if (memo is null)
                {
                    everyElementIsAMemo = false;
                    continue;
                }

                ValidateUrlSafe(memo, "MemoType");
                ValidateUrlSafe(memo, "MemoFormat");
            }

            if (!everyElementIsAMemo)
            {
                return;
            }

            if (!TryMeasure(array, out int length))
            {
                return;
            }

            if (length > MaxSerializedLength)
            {
                throw new ValidationException(
                    $"Memos: the serialized array is {length} bytes and a node accepts at most " +
                    $"{MaxSerializedLength}. The limit is on the whole array, so splitting the " +
                    $"content across several memos does not help.");
            }
        }

        /// <summary>
        /// Measures a <c>Memos</c> array the way the node measures it, so a caller can show how much
        /// room is left before the limit instead of finding out when signing is refused.
        /// </summary>
        /// <param name="memos">
        /// The value of the transaction's <c>Memos</c> field: a collection of <c>{ "Memo": { ... } }</c>
        /// elements (or the equivalent JSON array), the shape <see cref="Validate"/> accepts. A
        /// transaction's typed <c>Memos</c> (<see cref="MemoWrapper"/> elements) go through the
        /// <see cref="TryGetSerializedLength(IEnumerable{MemoWrapper}, out int)"/> overload, which
        /// the compiler checks; this one takes anything and answers <c>false</c> for what is not an
        /// array of memos, such as a string or a single memo.
        /// </param>
        /// <param name="length">
        /// The length in bytes of the array as it would be serialized in the transaction, to compare
        /// with <see cref="MaxSerializedLength"/>; <c>0</c> when the method returns <c>false</c>.
        /// </param>
        /// <returns>
        /// <c>false</c> when there is nothing to measure: <paramref name="memos"/> is <c>null</c>, not
        /// an array, empty, holds an element that is not a <c>Memo</c> object, or holds content the
        /// binary codec cannot serialize (a value that is not hex, say), or is an object
        /// <c>System.Text.Json</c> cannot convert to JSON (a <c>double.NaN</c> value, a getter that
        /// throws). The method throws nothing but critical runtime failures such as
        /// <see cref="OutOfMemoryException"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This is the figure <see cref="Validate"/> compares with the limit, so a form and the
        /// signing check cannot disagree. rippled serializes the array with <c>STArray::add</c>:
        /// each element is its object start marker, the fields, and the object end marker; the
        /// array's own markers are not part of the length it compares.
        /// </para>
        /// <para>
        /// Each field costs one byte of field id plus its length prefix (one byte up to 192 bytes,
        /// two up to 12480) plus the value. A memo costs two more for its start and end markers. A
        /// single memo of <c>n</c> bytes of <c>MemoData</c> with a ten-byte <c>MemoFormat</c> such as
        /// <c>text/plain</c> and no <c>MemoType</c> therefore measures <c>n + 17</c> once <c>n</c> is
        /// 193 or more, and a memo of data alone measures <c>n + 5</c> (<c>n + 4</c> below 193, where
        /// the length prefix is one byte).
        /// </para>
        /// </remarks>
        public static bool TryGetSerializedLength(object memos, out int length)
        {
            length = 0;
            if (memos is null)
            {
                return false;
            }

            JsonNode node;
            try
            {
                node = memos as JsonNode ?? JsonSerializer.SerializeToNode(memos, XrplJsonOptions.Default);
            }
            catch (Exception e) when (!IsCritical(e))
            {
                // A value System.Text.Json cannot turn into a node is not a Memos array. Like the
                // codec, the serializer fails on the caller's object in unrelated types (a NaN
                // double: ArgumentException; a throwing getter or duplicate names:
                // InvalidOperationException; a custom converter: whatever it throws), so the
                // same guard applies and a Try* method throws only critical failures.
                return false;
            }

            return node is JsonArray array && TryMeasure(array, out length);
        }

        /// <summary>
        /// Measures the SDK's typed <c>Memos</c> (<c>TransactionCommon.Memos</c>,
        /// <see cref="MemoWrapper"/> elements) with the same rules as
        /// <see cref="TryGetSerializedLength(object, out int)"/>.
        /// </summary>
        /// <param name="memos">The memos of a transaction, or <c>null</c>.</param>
        /// <param name="length">
        /// The serialized length in bytes, to compare with <see cref="MaxSerializedLength"/>; <c>0</c>
        /// when the method returns <c>false</c>.
        /// </param>
        /// <returns>
        /// <c>false</c> for <c>null</c>, an empty sequence, a <see cref="MemoWrapper"/> without a
        /// <see cref="MemoWrapper.Memo"/>, or a memo whose members are not hex. A member left
        /// <c>null</c> is not written, so a memo of <c>MemoData</c> alone is measured without
        /// <c>MemoType</c> and <c>MemoFormat</c>.
        /// </returns>
        public static bool TryGetSerializedLength(IEnumerable<MemoWrapper> memos, out int length)
        {
            return TryGetSerializedLength((object)memos, out length);
        }

        private static bool TryMeasure(JsonArray array, out int length)
        {
            length = 0;
            if (array.Count == 0)
            {
                return false;
            }

            foreach (JsonNode element in array)
            {
                if (TryUnwrapMemo(element) is null)
                {
                    return false;
                }
            }

            try
            {
                StArray serialized = StArray.FromJson(array);
                BytesList sink = new BytesList();
                serialized.ToBytes(sink);
                length = sink.BytesLength();
                return true;
            }
            catch (Exception e) when (!IsCritical(e))
            {
                // The content is arbitrary user or JSON input, and the codec's failures on it come
                // in several unrelated types (InvalidJsonException, BinaryCodecException,
                // ArgumentException for a buffer of the wrong length, KeyNotFoundException for an
                // unknown enum name, NullReferenceException for a JSON null, InvalidOperationException
                // past the size cap, ...), so no list of them stays complete. Any of them means there
                // is no length to report. Nothing is hidden by answering false: building the
                // transaction runs the same codec on the same content, so a codec fault still
                // surfaces there, with the codec's own message.
                length = 0;
                return false;
            }
        }

        /// <summary>
        /// Failures that say nothing about the input and must not be turned into "nothing to measure".
        /// </summary>
        private static bool IsCritical(Exception e)
        {
            return e is OutOfMemoryException
                || e is StackOverflowException
                || e is AccessViolationException
                || e is ThreadAbortException
                || e is ThreadInterruptedException;
        }

        /// <summary>
        /// Returns the memo itself from an array element, or <c>null</c> when the element is not
        /// shaped like one.
        /// </summary>
        /// <remarks>
        /// On the wire a <c>Memos</c> element is an object with the single member <c>Memo</c> -
        /// rippled refuses an array holding anything else ("A memo array may contain only Memo
        /// objects"), and so does the codec, naming the member that does not belong. Anything else
        /// is therefore reported as <c>null</c> rather than thrown on.
        /// </remarks>
        private static JsonObject TryUnwrapMemo(JsonNode element)
        {
            if (element is JsonObject wrapper &&
                wrapper.Count == 1 &&
                wrapper.TryGetPropertyValue("Memo", out JsonNode inner) &&
                inner is JsonObject memo)
            {
                return memo;
            }

            return null;
        }

        private static void ValidateUrlSafe(JsonObject memo, string field)
        {
            if (!memo.TryGetPropertyValue(field, out JsonNode value) || value is null)
            {
                return;
            }

            // Not a string at all - a number, say. Reading it as one would throw
            // InvalidOperationException from inside the JSON reader: neither an exception a caller
            // of this SDK expects nor a message that names the field. The codec refuses it and says
            // which member is wrong, so it is left to do that.
            if (value is not JsonValue jsonValue || !jsonValue.TryGetValue(out string hex))
            {
                return;
            }

            if (string.IsNullOrEmpty(hex))
            {
                return;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                // Left to the codec, which refuses non-hex when it builds the blob and says so in
                // its own words. Reporting it twice, differently, would only add a second story.
                return;
            }

            foreach (byte character in decoded)
            {
                if (!AllowedInMemoType[character])
                {
                    throw new ValidationException(
                        $"Memos: {field} decodes to a character a URL may not contain " +
                        $"(byte 0x{character:X2}). A node allows only alphanumerics and " +
                        $"-._~:/?#[]@!$&'()*+,;=% there; arbitrary bytes belong in MemoData.");
                }
            }
        }

        private static bool[] BuildAllowedTable()
        {
            bool[] allowed = new bool[256];
            foreach (char character in UrlSafeCharacters)
            {
                allowed[character] = true;
            }

            return allowed;
        }
    }
}
