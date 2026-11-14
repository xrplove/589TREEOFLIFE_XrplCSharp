using Microsoft.VisualStudio.TestTools.UnitTesting;


using System;
using System.Linq;

using Xrpl.Wallet;

using XrplTests;

namespace Xrpl.Tests.Wallet.Tests.XummNumbers;

[TestClass]
public class TestUXummNumbers
{
    string[] xummNumbers = new[] { "556863", "404730", "402495", "038856", "113360", "465825", "112585", "283320" };
    string xummNumbersRow => string.Join(" ", this.xummNumbers);

    string wallet_num = "rNUhe55ffGjezrVwTQfpL73aP5qKdofZMy";
    string wallet_seed = "snjnsXBywtRUzVQagnjfXHwo97x1E";
    private string wallet_private_key = "00FC5D3ACE7236F40683947E68575316959D07C2425EE12F41E534EA4295BABFAA";

    [TestMethod]
    public void TestVerify_EntropyFromXummNumbers()
    {
        XrplWallet result = XrplWallet.FromXummNumbers(xummNumbers, "secp256k1");
        //sEdVLhsR1xkLWWLX9KbErLTo6EEaHFi WRONG SEED
        //rJP8D3Mpntnp7T1YZyz51xwtdeYKcz5hpR WRONG ADDRESS

        Assert.AreEqual(result.Seed, wallet_seed);
        Assert.AreEqual(result.ClassicAddress, wallet_num);
        Assert.AreEqual(result.PrivateKey, wallet_private_key);
    }

    [TestMethod]
    public void TestVerify_InValid_EntropyFromXummNumbers()
    {
        var numbers = new string[8];
        Array.Copy(xummNumbers, numbers, 8);
        numbers[0] = "556862";
        Helper.ThrowsException<ArgumentException>(() => XrplWallet.FromXummNumbers(numbers), "Invalid secret numbers or checksum mismatch");
    }

    [TestMethod]
    public void TestVerify_CheckXummSum()
    {
        var number = xummNumbers[0];
        var position = 0;
        var valid_sum = XummExtension.CheckXummSum(position, number);
        Assert.IsTrue(valid_sum);

        number = xummNumbers[2];
        position = 2;
        valid_sum = XummExtension.CheckXummSum(position, number);
        Assert.IsTrue(valid_sum);

        number = xummNumbers[6];
        position = 6;
        valid_sum = XummExtension.CheckXummSum(position, number);
        Assert.IsTrue(valid_sum);

        number = xummNumbers[7];
        position = 7;
        valid_sum = XummExtension.CheckXummSum(position, number);
        Assert.IsTrue(valid_sum);
    }
    [TestMethod]
    public void TestVerify_InValid_CheckXummSum()
    {
        var number = xummNumbers[3];
        var position = 2;
        var valid_sum = XummExtension.CheckXummSum(position, number);
        Assert.AreNotEqual(true, valid_sum);
    }

    [TestMethod]
    public void TestCalculateChecksum()
    {
        int value = 55686;
        int position = 0;
        int checksum = XummExtension.CalculateChecksum(position, value);
        Assert.AreEqual(3, checksum);

        value = 40249;
        position = 2;
        checksum = XummExtension.CalculateChecksum(position, value);
        Assert.AreEqual(5, checksum);
    }

    [TestMethod]
    public void TestEntropyToSecretNumbers()
    {
        byte[] entropy = new byte[] { 0xD9, 0x8E, 0x62, 0xDA, 0x62, 0x3F, 0x09, 0x78, 0x1B, 0xB0, 0x71, 0xD1, 0x1B, 0xA9, 0x45, 0xB8 };
        string[] secretNumbers = XummExtension.EntropyToSecretNumbers(entropy);

        Assert.AreEqual(8, secretNumbers.Length);
        foreach (var num in secretNumbers)
        {
            Assert.AreEqual(6, num.Length);
        }
    }

    [TestMethod]
    public void TestRoundtripEntropyConversion()
    {
        byte[] originalEntropy = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 };

        string[] secretNumbers = XummExtension.EntropyToSecretNumbers(originalEntropy);
        byte[] recoveredEntropy = XummExtension.EntropyFromXummNumbers(secretNumbers);

        CollectionAssert.AreEqual(originalEntropy, recoveredEntropy);
    }

    [TestMethod]
    public void TestRandomSecretNumbers()
    {
        string[] random1 = XummExtension.RandomSecretNumbers();
        string[] random2 = XummExtension.RandomSecretNumbers();

        Assert.AreEqual(8, random1.Length);
        Assert.AreEqual(8, random2.Length);

        bool allValid1 = XummExtension.CheckXummNumbers(random1);
        bool allValid2 = XummExtension.CheckXummNumbers(random2);
        Assert.IsTrue(allValid1);
        Assert.IsTrue(allValid2);

        bool areDifferent = !random1.SequenceEqual(random2);
        Assert.IsTrue(areDifferent, "Two random secret numbers should be different");
    }

    [TestMethod]
    public void TestParseSecretString()
    {
        string secretString = xummNumbersRow;
        string[] parsed = XummExtension.ParseSecretString(secretString);

        CollectionAssert.AreEqual(xummNumbers, parsed);
    }

    [TestMethod]
    public void TestFromSecretString()
    {
        string secretString = xummNumbersRow;
        XrplWallet wallet = XrplWallet.FromSecretString(secretString);

        Assert.AreEqual(wallet_seed, wallet.Seed);
        Assert.AreEqual(wallet_num, wallet.ClassicAddress);
    }

    [TestMethod]
    public void TestGetSecretNumbers()
    {
        XrplWallet wallet = XrplWallet.FromXummNumbers(xummNumbers);
        string[] recoveredNumbers = wallet.GetSecretNumbers();

        Assert.IsNotNull(recoveredNumbers);
        Assert.AreEqual(8, recoveredNumbers.Length);
        CollectionAssert.AreEqual(xummNumbers, recoveredNumbers);
    }

    [TestMethod]
    public void TestGetSecretString()
    {
        XrplWallet wallet = XrplWallet.FromXummNumbers(xummNumbers);
        string secretString = wallet.GetSecretString();

        Assert.IsNotNull(secretString);
        Assert.AreEqual(xummNumbersRow, secretString);
    }

    [TestMethod]
    public void TestFullRoundtrip()
    {
        string[] original = XummExtension.RandomSecretNumbers();
        XrplWallet wallet = XrplWallet.FromXummNumbers(original, XrplWallet.Ed25519);
        string[] recovered = wallet.GetSecretNumbers();

        CollectionAssert.AreEqual(original, recovered);
        Assert.AreEqual(string.Join(" ", original), wallet.GetSecretString());
    }
}