using System;
using System.Linq;
using System.Text.RegularExpressions;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/hashes/index.ts

namespace Xrpl.Utils.Hashes
{
    //todo double need check
    public static class Hashes
    {
        const int HEX = 16;
        const int BYTE_LENGTH = 4;
        const byte MASK = 0xff;
        public static string AddressToHex(this string address)
        {
            return XrplCodec.DecodeAccountID(address).ToHex();
        }

        public static string LedgerSpaceHex(this LedgerSpace name)
        {
            return ((int)name).ToString("X4");
        }

        public static string LedgerSpaceHex(string name)
        {
            if (!Enum.TryParse(name, false, out LedgerSpace space) || !Enum.IsDefined(typeof(LedgerSpace), space))
            {
                throw new ArgumentException($"Unknown ledger space '{name}'.", nameof(name));
            }

            return space.LedgerSpaceHex();
        }
        /// <summary>
        /// check currency code for HEX 
        /// </summary>
        /// <param name="code">currency code</param>
        /// <returns></returns>
        // Exact length plus a per-character scan: unlike a ^…$ pattern, "40 hex chars + \n" is rejected
        public static bool IsHexCurrencyCode(this string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            return AssetIdentity.IsHexCurrencyCode(code);
        }

        /// <summary>
        /// checks and generates a token code for transmission to the network
        /// </summary>
        /// <param name="currency">Currency code</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If Currency code length more than 40 characters</exception>
        public static string CurrencyToHex(this string currency)
        {
            var cur_code = currency.Trim();
            if (cur_code.Length == 3)
                return cur_code;

            if (cur_code.IsHexCurrencyCode())
                return cur_code;

            cur_code = cur_code.ConvertStringToHex();

            if (cur_code.Length > 40)
                throw new XrplException("wrong currency code format");

            cur_code += new string('0', 40 - cur_code.Length);

            return cur_code;

        }
        /// <summary>
        /// Hash the given binary transaction data with the single-signing prefix (<c>STX\0</c>).<br/>
        /// See [Serialization Format](https://xrpl.org/serialization.html).
        /// </summary>
        /// <remarks>
        /// This is the hash a single signer signs, as xrpl.js <c>hashTx</c> computes it - not the
        /// transaction ID. The ID of a signed transaction is <see cref="HashLedger.HashSignedTx(string)"/>.
        /// </remarks>
        /// <param name="txBlobHex">The transaction blob for signing, as a hexadecimal string.</param>
        /// <returns>The hash to sign.</returns>
        public static string HashTx(this string txBlobHex)
        {

            var prefix = HashPrefix.TRANSACTION_SIGN.ToString("X").ToUpper();
            return (prefix + txBlobHex).Sha512Half();
        }


        public static string HashPaymentChannel(string address, string dstAddress, int sequence)
        {
            return (LedgerSpace.Paychan.LedgerSpaceHex() + address.AddressToHex() + dstAddress.AddressToHex() +
                    sequence.ToString("X").PadLeft(BYTE_LENGTH * 2, '0')).Sha512Half();
        }

        /// <summary>
        /// Duplicate of <see cref="HashTx(string)"/>: the single-signing hash, not the transaction ID.
        /// </summary>
        [Obsolete("Use HashTx for the single-signing hash, or HashLedger.HashSignedTx for the transaction ID.")]
        public static string HashTX(this string txBlobHex)
        {
            string prefix = ((int)HashPrefix.TRANSACTION_SIGN).ToString("X").ToUpper();
            return (prefix + txBlobHex).Sha512Half();
        }

        public static string HashAccountRoot(this string address)
        {
            return (LedgerSpace.Account.LedgerSpaceHex() + address.AddressToHex()).Sha512Half();
        }

        public static string HashSignerListId(this string address)
        {
            return (LedgerSpace.SignerList.LedgerSpaceHex() + address.AddressToHex() + "00000000").Sha512Half();
        }

        /// <summary>
        /// Compute the ledger object index of an <c>Offer</c> entry.
        /// Mirrors rippled's <c>keylet::offer(account, sequence)</c>:
        /// <c>sha512Half(uint16(LedgerNameSpace::OFFER) || account(20) || uint32(sequence))</c>.
        /// </summary>
        /// <param name="address">Classic address of the account that placed the offer.</param>
        /// <param name="sequence">Sequence (or ticket sequence) of the <c>OfferCreate</c> that placed it.</param>
        /// <returns>The 64-character hexadecimal object ID of the offer.</returns>
        public static string HashOfferId(string address, uint sequence)
        {
            return (LedgerSpace.Offer.LedgerSpaceHex() + address.AddressToHex() + sequence.ToString("X8")).Sha512Half();
        }

        /// <inheritdoc cref="HashOfferId(string, uint)"/>
        /// <remarks>A sequence above <see cref="int.MaxValue"/> is taken as its two's-complement bit pattern.</remarks>
        public static string HashOfferId(string address, int sequence)
        {
            return HashOfferId(address, unchecked((uint)sequence));
        }

        /// <summary>
        /// Compute the ledger object index of a <c>RippleState</c> (trust line) entry.
        /// Mirrors rippled's <c>keylet::line(a, b, currency)</c>:
        /// <c>sha512Half(uint16(LedgerNameSpace::TRUST_LINE) || low(20) || high(20) || currency(20))</c>,
        /// where low/high order the two AccountIDs as unsigned byte strings.
        /// </summary>
        /// <param name="address1">Classic address of one side of the trust line.</param>
        /// <param name="address2">Classic address of the other side; the order of the two does not matter.</param>
        /// <param name="currency">A 3-character ISO code, a 40-character hex code, or a longer name encoded the way <see cref="CurrencyToHex"/> does.</param>
        /// <returns>The 64-character hexadecimal object ID of the trust line.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="currency"/> is XRP, which has no trust lines.</exception>
        public static string HashTrustline(string address1, string address2, string currency)
        {
            string address1Hex = address1.AddressToHex();
            string address2Hex = address2.AddressToHex();

            // Equal-length uppercase hex compares ordinally in the same order as the unsigned bytes it encodes.
            bool swap = string.CompareOrdinal(address1Hex, address2Hex) > 0;
            string lowAddressHex = swap ? address2Hex : address1Hex;
            string highAddressHex = swap ? address1Hex : address2Hex;

            string prefix = LedgerSpace.RippleState.LedgerSpaceHex();
            return (prefix + lowAddressHex + highAddressHex + CurrencyCodeHex(currency)).Sha512Half();
        }

        /// <summary>
        /// The 20-byte currency code as hex. Unlike <see cref="CurrencyToHex"/>, which leaves an ISO code
        /// as-is for JSON, this places the ISO code at bytes 12-14 the way the ledger stores it.
        /// </summary>
        private static string CurrencyCodeHex(string currency)
        {
            string code = currency.Trim();
            string hex = code.Length == 3
                ? Xrpl.BinaryCodec.Types.Currency.EncodeCurrency(code).ToHex()
                : code.CurrencyToHex();

            // rippled maps "XRP" to the all-zero currency, and TrustSet refuses both forms.
            if (string.Equals(code, "XRP", StringComparison.Ordinal) || hex.All(c => c == '0'))
            {
                throw new ArgumentException("XRP has no trust lines", nameof(currency));
            }

            return hex;
        }

        public static string HashEscrow(string address, int sequence)
        {
            return (LedgerSpace.Escrow.LedgerSpaceHex() + address.AddressToHex() + sequence.ToString("X").PadLeft(BYTE_LENGTH * 2, '0')).Sha512Half();
        }

        /// <summary>
        /// Compute the ledger object index (object ID) for an XLS-70 Credential ledger entry.
        /// Mirrors rippled's <c>keylet::credential(subject, issuer, credentialType)</c>:
        /// <c>sha512Half(uint16(LedgerNameSpace::CREDENTIAL) || subject(20) || issuer(20) || credentialType)</c>.
        /// </summary>
        /// <param name="subject">Classic XRPL address of the credential subject.</param>
        /// <param name="issuer">Classic XRPL address of the credential issuer.</param>
        /// <param name="credentialTypeHex">Credential type as an uppercase hexadecimal string (1..64 bytes -> 2..128 hex chars).</param>
        /// <returns>The 64-character (32-byte) hexadecimal object ID of the credential ledger entry.</returns>
        /// <exception cref="ArgumentException">Thrown when arguments are missing or malformed.</exception>
        public static string HashCredential(string subject, string issuer, string credentialTypeHex)
        {
            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("subject is required", nameof(subject));
            }

            if (string.IsNullOrEmpty(issuer))
            {
                throw new ArgumentException("issuer is required", nameof(issuer));
            }

            if (string.IsNullOrEmpty(credentialTypeHex))
            {
                throw new ArgumentException("credentialTypeHex is required", nameof(credentialTypeHex));
            }

            string normalized = credentialTypeHex.Trim().ToUpperInvariant();
            if (normalized.Length == 0 || normalized.Length % 2 != 0 || !Regex.IsMatch(normalized, "^[0-9A-F]+$"))
            {
                throw new ArgumentException("credentialTypeHex must be an even-length hexadecimal string", nameof(credentialTypeHex));
            }

            if (normalized.Length > 128)
            {
                throw new ArgumentException("credentialTypeHex cannot exceed 64 bytes (128 hex characters)", nameof(credentialTypeHex));
            }

            return (LedgerSpace.Credential.LedgerSpaceHex()
                    + subject.AddressToHex()
                    + issuer.AddressToHex()
                    + normalized).Sha512Half();
        }

        /// <summary>
        /// Get the owner of an <c>NFTokenPage</c> ledger entry from the page's ledger index.
        /// Mirrors rippled's <c>keylet::nftpage_min(owner)</c> and <c>keylet::nftpage_max(owner)</c>: the high
        /// 160 bits of a page index are the owner's AccountID and the low 96 bits (<c>nft::kPageMask</c>) come from
        /// an NFTokenID. An NFTokenPage carries no <c>Owner</c> field, so this is the only way to tell whose page it is
        /// without asking the ledger.
        /// </summary>
        /// <param name="pageIndex">
        /// The 64-character hexadecimal ledger index of the page, e.g. the <c>index</c> of an <c>account_objects</c>
        /// entry or a <c>LedgerIndex</c> from transaction metadata. Upper and lower case are both accepted;
        /// whitespace and a <c>0x</c> prefix are not.
        /// </param>
        /// <returns>The classic address of the account that owns the page.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pageIndex"/> is null, not 64 characters long or not hexadecimal.</exception>
        /// <remarks>
        /// The result only means something for the index of an <c>NFTokenPage</c>: any other 256-bit ledger index
        /// (an offer, a trust line) is a hash and its first 20 bytes are not an account.
        /// </remarks>
        public static string GetNFTokenPageOwner(string pageIndex)
        {
            const int indexHexLength = 64;
            const int accountIdHexLength = 40;

            if (pageIndex is null || pageIndex.Length != indexHexLength)
            {
                throw new ArgumentException("pageIndex must be a 64-character hexadecimal string", nameof(pageIndex));
            }

            // Checked explicitly: Hex.Decode would also throw, but with an exception type that does not name the argument.
            foreach (char c in pageIndex)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!isHex)
                {
                    throw new ArgumentException("pageIndex must be a 64-character hexadecimal string", nameof(pageIndex));
                }
            }

            return XrplCodec.EncodeAccountID(pageIndex.Substring(0, accountIdHexLength).FromHex());
        }

    }
}

