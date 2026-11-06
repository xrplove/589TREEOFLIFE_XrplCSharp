using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace Xrpl.Models.Utils;

public static class BatchNormalizer
{
    private const uint TF_INNER_BATCH_TXN = (uint)XrplGlobalFlags.tfInnerBatchTxn;

    /// <summary>
    /// Normalises an inner transaction under the XLS-56 rules:
    /// - adds the tfInnerBatchTxn flag;
    /// - removes TxnSignature, Signers, LastLedgerSequence;
    /// - forces Fee = "0" (as a string) and SigningPubKey = "".
    /// Returns a new JsonObject; source is left untouched.
    /// </summary>
    public static JsonObject NormalizeInnerTransaction(this JsonObject source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        // Normalise a copy. A caller who hands over a transaction is not offering to
        // have it stripped of its signature fields, and the object overload already
        // behaves this way for every input that is not already a JsonObject.
        JsonObject normalized = (JsonObject)source.DeepClone();

        normalized.Remove("TxnSignature");
        normalized.Remove("Signers");
        normalized.Remove("LastLedgerSequence");

        normalized["Fee"] = "0";
        normalized["SigningPubKey"] = "";

        uint flags = 0;
        JsonNode fv = normalized["Flags"];
        if (fv != null)
        {
            if (fv is JsonValue jv)
            {
                // Flags set from code are CLR-typed JsonValues (uint, int, double, ...) that only
                // answer TryGetValue for their own type. Uint32.FromJson reads every one of them;
                // a value it cannot read is refused, since dropping it would replace the caller's flags.
                if (jv.GetValueKind() == JsonValueKind.Number)
                {
                    try
                    {
                        flags = Xrpl.BinaryCodec.Types.Uint32.FromJson(jv);
                    }
                    catch (Exception ex) when (ex is OverflowException || ex is FormatException)
                    {
                        throw new ValidationException($"Flags must be a UInt32: {ex.Message}");
                    }
                }
                else if (jv.TryGetValue(out string sv) && uint.TryParse(sv, out uint u))
                    flags = u;
            }
        }

        if ((flags & TF_INNER_BATCH_TXN) == 0)
            flags |= TF_INNER_BATCH_TXN;

        normalized["Flags"] = flags;

        return normalized;
    }

    /// <summary>
    /// Normalises an inner transaction (object -> JsonObject).
    /// </summary>
    public static JsonObject NormalizeInnerTransaction(object source)
    {
        if (source is JsonObject jo)
            return NormalizeInnerTransaction(jo);

        return NormalizeInnerTransaction(
            JsonNode.Parse(JsonSerializer.Serialize(source, XrplJsonOptions.Default))?.AsObject());
    }

    /// <summary>
    /// Normalises every inner transaction of a Batch and fills the sequence numbers the caller left out.
    /// An outer or inner transaction with a <c>TicketSequence</c> gets <c>Sequence</c> 0 and consumes no
    /// account sequence; a non-zero inner <c>Sequence</c> the caller set is kept, and later inner
    /// transactions of that account continue after it.
    /// </summary>
    /// <exception cref="ValidationException">When RawTransactions is missing or malformed, or an inner
    /// transaction has no Account.</exception>
    public static async Task NormalizeBatchTransaction(
        this IXrplClient client,
        Dictionary<string, object> tx,
        CancellationToken cancellationToken = default)
    {
        if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
            throw new ValidationException("Batch transaction must have RawTransactions field.");

        var raws = ToRawList(rawTransactions);
        tx["RawTransactions"] = raws;

        var nextSeqByAccount = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var minNextByAccount = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        async Task<uint> GetNextSeqAsync(string account)
        {
            if (nextSeqByAccount.TryGetValue(account, out var seq))
                return seq;

            var probe = new Dictionary<string, object> { ["Account"] = account };
            var ai = await client.AccountInfo(new AccountInfoRequest(account)
            {
                LedgerIndex = new LedgerIndex(LedgerIndexType.Current)
            }, cancellationToken).Typed();
            // account_info for the current ledger always returns a full AccountRoot with a Sequence;
            // a missing AccountData (or a missing Sequence within it) means a malformed node response
            // and must fail loudly rather than dereference a null AccountData or silently treat it as 0.
            if (ai.AccountData is null)
            {
                throw new ValidationException($"account_info response for '{account}' did not include account_data.");
            }

            uint? start = ai.AccountData.Sequence;
            if (start == null)
            {
                throw new ValidationException($"account_info response for '{account}' did not include the account's Sequence.");
            }
            // A caller-set inner Sequence seen before this account's first lookup still has to be
            // stepped over, whichever of the two is further ahead.
            uint next = minNextByAccount.TryGetValue(account, out uint floor) ? Math.Max(start.Value, floor) : start.Value;
            nextSeqByAccount[account] = next;
            return next;
        }

        // Advances the account past a Sequence the caller chose, never backwards: an inner
        // transaction numbered below one already assigned must not make the next filled-in inner
        // reuse a number.
        void StepOver(string account, uint callerSeq)
        {
            uint after = SequenceAfter(account, callerSeq);
            if (nextSeqByAccount.TryGetValue(account, out uint current))
                nextSeqByAccount[account] = Math.Max(current, after);
            else
                minNextByAccount[account] = minNextByAccount.TryGetValue(account, out uint floor) ? Math.Max(floor, after) : after;
        }

        void Bump(string account)
        {
            if (nextSeqByAccount.TryGetValue(account, out var val))
                nextSeqByAccount[account] = SequenceAfter(account, val);
        }

        var rootAccount = $"{tx["Account"]}";
        if (HasValue(tx, "TicketSequence"))
        {
            // A ticketed transaction consumes no sequence number, and rippled rejects a non-zero
            // Sequence next to a TicketSequence (temSEQ_AND_TICKET). Sequence itself is a required
            // field, so it is written as 0, and the account's inner transactions start from the
            // ledger's Sequence rather than one past it.
            if (!HasValue(tx, "Sequence"))
                tx["Sequence"] = 0u;
        }
        else if (!HasValue(tx, "Sequence"))
        {
            var seq = await GetNextSeqAsync(rootAccount);
            tx["Sequence"] = seq;
            nextSeqByAccount[rootAccount] = SequenceAfter(rootAccount, seq);
        }
        else
        {
            var seq = ToUInt(tx["Sequence"]);
            nextSeqByAccount[rootAccount] = SequenceAfter(rootAccount, seq);
        }

        foreach (var wrapper in raws)
        {
            if (!wrapper.TryGetValue("RawTransaction", out object? rawTxObj) || rawTxObj is null)
                throw new ValidationException("Each item in RawTransactions must contain 'RawTransaction'.");

            var normalized = NormalizeInnerTransaction(rawTxObj);
            var rawTx = JsonSerializer.Deserialize<Dictionary<string, object>>(
                normalized.ToJsonString(), XrplJsonOptions.Default)!;
            wrapper["RawTransaction"] = rawTx;

            var account = rawTx.TryGetValue("Account", out object accObj)
                ? accObj?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(account))
                throw new ValidationException("Each RawTransaction must have an 'Account' field.");

            // Batch preflight requires each inner transaction to carry exactly one of a non-zero
            // Sequence and a TicketSequence (temSEQ_AND_TICKET otherwise), so a ticketed inner gets
            // Sequence 0 and a caller-chosen Sequence is kept. A Sequence of 0 without a ticket can
            // never pass and is filled like an absent one.
            if (HasValue(rawTx, "TicketSequence"))
            {
                if (!HasValue(rawTx, "Sequence"))
                    rawTx["Sequence"] = 0u;
                continue;
            }

            if (HasValue(rawTx, "Sequence") && ToUInt(rawTx["Sequence"]) is uint callerSeq and not 0)
            {
                StepOver(account, callerSeq);
                continue;
            }

            var next = await GetNextSeqAsync(account);
            rawTx["Sequence"] = next;
            Bump(account);
        }
    }

    private static uint SequenceAfter(string account, uint sequence)
    {
        if (sequence == uint.MaxValue)
            throw new ValidationException($"Sequence {sequence} of '{account}' is the last UInt32; no transaction can follow it.");
        return sequence + 1;
    }

    private static bool HasValue(Dictionary<string, object> tx, string field)
        => tx.TryGetValue(field, out object? value) && value is not null;

    public static List<Dictionary<string, object>> ToRawList(object rawTransactions)
    {
        return rawTransactions switch
        {
            JsonArray ja => ja.Select(n => JsonSerializer.Deserialize<Dictionary<string, object>>(
                                n.ToJsonString(), XrplJsonOptions.Default))
                         .ToList(),
            IEnumerable ie when ie is not string => ie.Cast<object>()
                .Select(o => o as Dictionary<string, object>
                          ?? JsonSerializer.Deserialize<Dictionary<string, object>>(
                              JsonSerializer.Serialize(o, XrplJsonOptions.Default), XrplJsonOptions.Default)!)
                .ToList(),
            _ => throw new ValidationException("RawTransactions must be array/collection.")
        };
    }

    /// <summary>
    /// Computes the transactionID of a normalised inner transaction.
    /// The algorithm: txid = SHA512Half( HashPrefix.TXN + STObject(tx).ToBytes() ).
    /// </summary>
    public static string ComputeInnerTxId(this JsonObject normalizedInnerTx)
    {
        // Strict: this id is what the outer Batch signature commits to. Parsed leniently, a
        // member the codec does not know would be dropped from the bytes being hashed, so the
        // signature would fix an inner transaction other than the one the caller was shown - and
        // nothing would say so. Strict without the signing filter, because an id covers the whole
        // transaction, signing fields and not.
        var st = Xrpl.BinaryCodec.Types.StObject.FromJsonStrict(normalizedInnerTx);
        var bytes = st.ToBytes();

        var prefix = Xrpl.BinaryCodec.Util.Bits.GetBytes((uint)Xrpl.BinaryCodec.Hashing.HashPrefix.TransactionId);
        var buf = new byte[prefix.Length + bytes.Length];
        Buffer.BlockCopy(prefix, 0, buf, 0, prefix.Length);
        Buffer.BlockCopy(bytes, 0, buf, prefix.Length, bytes.Length);

        var hash32 = Xrpl.BinaryCodec.Hashing.Sha512.Half(buf);
        return ToHex(hash32);
    }

    private static string ToHex(byte[] data)
    {
        char[] c = new char[data.Length * 2];
        int b;
        for (int i = 0; i < data.Length; i++)
        {
            b = data[i] >> 4;
            c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
            b = data[i] & 0xF;
            c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
        }
        return new string(c).ToLowerInvariant();
    }

    private static uint ToUInt(object? v)
    {
        if (v is null) throw new ValidationException("Sequence is null.");
        return v switch
        {
            uint u => u,
            int i when i >= 0 => (uint)i,
            long l when l >= 0 => checked((uint)l),
            string s => uint.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            JsonValue jv when jv.TryGetValue<long>(out long lv) => checked((uint)lv),
            _ => Convert.ToUInt32(v, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
