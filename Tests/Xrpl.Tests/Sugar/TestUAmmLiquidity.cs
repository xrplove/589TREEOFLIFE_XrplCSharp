using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestUAmmLiquidity
{
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static readonly AmmPool Pool = new AmmPool
    {
        Asset = Usd,
        Asset2 = Xrp,
        Balance = XrplNumber.Parse("1000"),
        Balance2 = XrplNumber.Parse("100000000"),
        LpTokenBalance = XrplNumber.Parse("316227.766016838"),
        TradingFee = 500,
    };

    [TestMethod]
    [DataRow("10", "0")]
    [DataRow("10", "-1")]
    [DataRow("-10", "1")]
    public void EffectivePrice_NotPositive_IsTheBadAmountPreflightReturns(string amount, string price)
    {
        // AMMDeposit and AMMWithdraw preflight refuse an EPrice that is not positive, and a
        // negative Amount, before any equation runs (invalidAMMAmount).
        AmmQuote deposit = AmmLiquidity.DepositWithEffectivePrice(Pool, Usd, XrplNumber.Parse(amount), XrplNumber.Parse(price));
        AmmQuote withdraw = AmmLiquidity.WithdrawWithEffectivePrice(Pool, Usd, XrplNumber.Parse(amount), XrplNumber.Parse(price), holderLpTokens: 1000);

        Assert.AreEqual("temBAD_AMOUNT", deposit.Refusal);
        Assert.AreEqual("temBAD_AMOUNT", withdraw.Refusal);
    }

    /// <summary>
    /// <c>LendingMath.RippleSeconds</c> now clamps a time before 2000 to 0, as <c>DateTimeUtils.ToRippleTime</c>
    /// does, so two such times compare equal (they were negative and ordered before). Ledger times are
    /// never negative, so this only shows for hand-built inputs.
    /// </summary>
    [TestMethod]
    public void FromAmmInfo_SlotAndCloseTimeBeforeTheEpoch_BothCountAsZeroSeconds()
    {
        AMMInfo amm = new AMMInfo
        {
            Amount = new Currency { CurrencyCode = "USD", Issuer = Usd.Issuer, Value = "1000" },
            Amount2 = new Currency { CurrencyCode = "XRP", Value = "100" },
            LPTokenBalance = new Currency { CurrencyCode = "LPT", Issuer = Usd.Issuer, Value = "316" },
            TradingFee = 500,
            AuctionSlot = new AuctionSlot
            {
                Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                DiscountedFee = 50,
                Expiration = new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        };

        AmmPool pool = AmmPool.FromAmmInfo(amm, "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.AreEqual((ushort)500, pool.TradingFee);
    }
}
