using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Sugar;

namespace XrplTests.Sugar;

/// <summary>
/// The sponsorship that decides whether a sponsor must sign is the one with the transaction's initiator, as in rippled
/// (<c>keylet::sponsorship(sponsor, tx.getInitiator())</c>): the <c>Delegate</c> when there is one, otherwise the <c>Account</c>.
/// </summary>
[TestClass]
public class TestUSponsorSignatureRequired
{
    private const string Sponsor = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c";

    private const string Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

    private const string Delegate = "rLPV1SBMbiTMno43PxhBEMWK9pz1hXYk53";

    private const uint SponsorFee = 0x00000001;

    private static Dictionary<string, object> Transaction(string @delegate = null)
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["Account"] = Account,
            ["Sponsor"] = Sponsor,
            ["SponsorFlags"] = SponsorFee,
        };

        if (@delegate is not null)
        {
            tx["Delegate"] = @delegate;
        }

        return tx;
    }

    private static Task<bool> Required(Dictionary<string, object> tx, string sponsee)
        => SubmitSugar.IsSponsorSignatureRequired(Node.Answering(sponsee), tx, Sponsor);

    [TestMethod]
    public async Task TestUTheSponsorshipOfTheAccountDecidesWithoutADelegate()
    {
        Assert.IsTrue(await Required(Transaction(), Account));
    }

    [TestMethod]
    public async Task TestUTheSponsorshipOfTheDelegateDecidesWhenThereIsOne()
    {
        Assert.IsTrue(await Required(Transaction(Delegate), Delegate));
    }

    [TestMethod]
    public async Task TestUTheSponsorshipOfTheAccountSaysNothingAboutADelegatedTransaction()
    {
        Assert.IsFalse(await Required(Transaction(Delegate), Account));
    }

    /// <summary>Absence on one page says nothing: the sponsorship of the initiator may be on the next.</summary>
    [TestMethod]
    public async Task TestUTheSponsorshipOnALaterPageDecides()
    {
        Assert.IsTrue(await SubmitSugar.IsSponsorSignatureRequired(Node.Answering(Account, pagesBefore: 2), Transaction(), Sponsor));
    }

    /// <summary>Answers <c>account_objects</c> with one sponsorship that requires the signature for the fee.</summary>
    public class Node : DispatchProxy
    {
        private string _sponsee;

        private int _pagesBefore;

        public static IXrplClient Answering(string sponsee, int pagesBefore = 0)
        {
            IXrplClient proxy = Create<IXrplClient, Node>();
            ((Node)(object)proxy)._sponsee = sponsee;
            ((Node)(object)proxy)._pagesBefore = pagesBefore;

            return proxy;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            Assert.AreEqual(nameof(IXrplClient.AccountObjects), targetMethod.Name);

            AccountObjects answer = _pagesBefore-- > 0
                ? new AccountObjects { AccountObjectList = [new LOSponsorship { Sponsee = Delegate }], Marker = "next" }
                : new AccountObjects
                {
                    AccountObjectList =
                    [
                        new LOSponsorship { Sponsee = _sponsee, Flags = SponsorshipFlags.lsfSponsorshipRequireSignForFee },
                    ],
                };

            return Task.FromResult(new XrplResponse<AccountObjects>(answer, default, null, null, null, null, false));
        }
    }
}
