using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Ledger;
using Xrpl.Sugar;

namespace XrplTests.Sugar;

/// <summary>The loan a payment leaves is a copy built field by field, so a field it forgets is lost without a word.</summary>
[TestClass]
public class TestULoanPaymentProjection
{
    [TestMethod]
    public void TestUThePaymentLeavesTheSponsorOfTheReserve()
    {
        MethodInfo withPayments = typeof(LoanPayments).GetMethod("WithPayments", BindingFlags.NonPublic | BindingFlags.Static)!;
        object state = RuntimeHelpers.GetUninitializedObject(withPayments.GetParameters()[1].ParameterType);
        LOLoan loan = new LOLoan { Sponsor = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c" };

        LOLoan after = (LOLoan)withPayments.Invoke(null, [loan, state, 1u, null, null, null])!;

        Assert.AreEqual(loan.Sponsor, after.Sponsor);
    }
}
