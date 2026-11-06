using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/trustSet.ts 

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Enum representing values of <see cref="ITrustSet"/> transaction flags.
    /// </summary>
    [Flags]
    public enum TrustSetFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,
        /// <summary>
        /// Authorize the other party to hold currency issued by this account.<br/>
        /// (No effect unless using the asfRequireAuth AccountSet flag.)<br/>
        /// Cannot be unset.
        /// </summary>
        tfSetfAuth = 0x00010000,
        /// <summary>
        /// Enable the No Ripple flag, which blocks rippling between two trust lines.<br/>
        /// of the same currency if this flag is enabled on both.
        /// </summary>
        tfSetNoRipple = 0x00020000,
        /// <summary>
        /// Disable the No Ripple flag, allowing rippling on this trust line.
        /// </summary>
        tfClearNoRipple = 0x00040000,
        /// <summary>
        /// Freeze the trust line.
        /// </summary>
        tfSetFreeze = 0x00100000,
        /// <summary>
        /// Unfreeze the trust line.
        /// </summary>
        tfClearFreeze = 0x00200000,
        /// <summary>
        /// 
        /// </summary>
        tfSetDeepFreeze = 0x00400000,
        /// <summary>
        /// 
        /// </summary>
        tfClearDeepFreeze = 0x00800000,

    }

    /// <inheritdoc cref="ITrustSet" />
    public class TrustSet : TransactionRequest, ITrustSet, IJsonOnDeserializing
    {
        /// <summary>
        /// A TrustSet to submit, with <see cref="TrustSetFlags.tfSetNoRipple"/> preset. Assign <see cref="Flags"/> to
        /// replace the preset, or null to send no flags. A TrustSet read from JSON does not get the preset.
        /// </summary>
        public TrustSet()
        {
            TransactionType = TransactionType.TrustSet;
            Flags = TrustSetFlags.tfSetNoRipple;
        }

        // The serializer runs the constructor first, and a Flags the JSON does not carry is never assigned. rippled reads
        // a missing Flags as no flags, so keeping the preset would report, and re-sign, a different transaction.
        void IJsonOnDeserializing.OnDeserializing() => Flags = null;

        /// <inheritdoc />
        public new TrustSetFlags? Flags
        {
            get => base.Flags.HasValue ? (TrustSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency LimitAmount { get; set; }

        /// <inheritdoc />
        public uint? QualityIn { get; set; }

        /// <inheritdoc />
        public uint? QualityOut { get; set; }
    }

    /// <summary>
    /// Create or modify a trust line linking two accounts.
    /// </summary>
    /// <code>
    /// ```typescript  const trustSetTx: TrustSet =
    /// {
    /// 	TransactionType: 'TrustSet',
    /// 	Account: wallet2.getClassicAddress(),
    /// 	LimitAmount:{
    /// 	currency: 'FOO',
    /// 	issuer: wallet1.getClassicAddress(),
    /// 	value: '10000000000',
    ///     },
    /// 	Flags:{
    /// 	tfSetNoRipple: true
    ///     }
    /// }
    /// </code>
    public interface ITrustSet : ITransactionCommon
    {
        /// <summary>
        /// <see cref="ITrustSet"/> transaction flags
        /// </summary>
        new TrustSetFlags? Flags { get; set; }
        /// <summary>
        /// Object defining the trust line to create or modify, in the format of a Currency Amount.
        /// </summary>
        Currency LimitAmount { get; set; }
        /// <summary>
        /// Value incoming balances on this trust line at the ratio of this number per 1,000,000,000 units.<br/>
        /// A value of 0 is shorthand for treating balances at face value.
        /// </summary>
        uint? QualityIn { get; set; }
        /// <summary>
        /// Value outgoing balances on this trust line at the ratio of this number per 1,000,000,000 units.<br/>
        /// A value of 0 is shorthand for treating balances at face value.
        /// </summary>
        uint? QualityOut { get; set; }
    }

    /// <inheritdoc cref="ITrustSet" />
    public class TrustSetResponse : TransactionResponse, ITrustSet
    {
        /// <inheritdoc />
        public new TrustSetFlags? Flags
        {
            get => base.Flags.HasValue ? (TrustSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency LimitAmount { get; set; }
        /// <inheritdoc />
        public uint? QualityIn { get; set; }

        /// <inheritdoc />
        public uint? QualityOut { get; set; }
    }

    public partial class Validation
    {
        //https://github.com/XRPLF/xrpl.js/blob/b40a519a0d949679a85bf442be29026b76c63a22/packages/xrpl/src/models/transactions/trustSet.ts#L127
        /// <summary>
        /// Verify the form and type of a TrustSet at runtime.
        /// </summary>
        /// <param name="tx"> A TrustSet Transaction.</param>
        /// <exception cref="ValidationException">When the TrustSet is malformed.</exception>
        public static void ValidateTrustSet(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            if (!tx.TryGetValue("LimitAmount", out var LimitAmount) || LimitAmount is null)
                throw new ValidationException("TrustSet: missing field LimitAmount");
            // TODO: Review this function
            if (!Common.IsAmount(LimitAmount))
                throw new ValidationException("TrustSet: invalid LimitAmount");

            if (tx.TryGetValue("QualityIn", out var QualityIn) && !Common.IsUInt32(QualityIn))
                throw new ValidationException("TrustSet: QualityIn must be a number");

            if (tx.TryGetValue("QualityOut", out var QualityOut) && !Common.IsUInt32(QualityOut))
                throw new ValidationException("TrustSet: QualityOut must be a number");
        }
    }
}
