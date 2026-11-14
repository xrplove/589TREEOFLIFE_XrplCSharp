using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;

using Xrpl.Keypairs;
using Xrpl.Wallet;

using XrplTests;

namespace Xrpl.Tests.Wallet.Tests.Mnemonic;

[TestClass]
public class TestUMnemonic
{
    private string mnemonic = "assault rare scout seed design extend noble drink talk control guitar quote";
    private string publicKey = "035953FCD81D001CF634EB44A87940F3F98ADF2483D09C914BAED0539BE50F385D";
    private string privateKey = "0013FC461CA5799F1357C8130AF703CBA7E9C28E072C6CA8F7DEF8601CDE98F394";

    [TestMethod]
    public void TestDefaultDerivationPath()
    {
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, null, "secp256k1");
        Assert.AreEqual(wallet.PublicKey, publicKey);
        Assert.AreEqual(wallet.PrivateKey, privateKey);
    }

    [TestMethod]
    public void TestInputDerivationPath()
    {
        string derivationPath = "m/44'/144'/0'/0/0";
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, derivationPath, null, "secp256k1");
        Assert.AreEqual(wallet.PublicKey, publicKey);
        Assert.AreEqual(wallet.PrivateKey, privateKey);
    }

    [TestMethod]
    public void TestInputPassPhrase()
    {
        string passPhrase = "my strong password";
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, null, "secp256k1", passPhrase);
        Assert.AreNotEqual(wallet.PublicKey, publicKey);
        Assert.AreNotEqual(wallet.PrivateKey, privateKey);
    }

    [TestMethod]
    public void TestRegularKeypairSeed()
    {
        string masterAddress = "rUAi7pipxGpYfPNg3LtPcf2ApiS8aw9A93";
        string mnemonic = "I IRE BOND BOW TRIO LAID SEAT GOAL HEN IBIS IBIS DARE";
        rKeypair regularKeyPair = new rKeypair
        {
            PublicKey = "0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020",
            PrivateKey = "001ACAAEDECE405B2A958212629E16F2EB46B153EEE94CDD350FDEFF52795525B7",
        };
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, masterAddress, null, "rfc1751", "secp256k1");
        XrplWallet wallet2 = XrplWallet.FromMnemonic(mnemonic, masterAddress, null, "rfc1751", "ed25519");

        Assert.AreEqual(wallet.PublicKey, regularKeyPair.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, regularKeyPair.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, masterAddress);
    }

    // This needs to throw an error
    [TestMethod]
    public void TestThrowsB39Seed()
    {
        string mnemonic = "draw attack antique swing base employ blur above palace lucky glide clap pen use illegal";

        Helper.ThrowsException<ArgumentException>(() => XrplWallet.FromMnemonic(mnemonic, null, null, "rfc1751", "ed25519"), "Word 2 is not an RFC1751 word.");
    }

    [TestMethod]
    public void TestEDPRFC1751SeedLower()
    {
        string mnemonic = "cab beth hank bird mend sign gild any kern hyde chat stub";
        string expectedSeed = "snVB4iTWYqsWZaj1hkvAy1QzqNbAg";

        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, "rfc1751", "secp256k1");

        Assert.AreEqual(expectedSeed, wallet.Seed);
    }

    [TestMethod]
    public void TestGenerateMnemonic_Default12Words()
    {
        string[] words = XrplWallet.GenerateMnemonic();
        Assert.AreEqual(12, words.Length);
        foreach (var word in words)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(word));
        }
    }

    [TestMethod]
    public void TestGenerateMnemonic_15Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(15);
        var wallet = XrplWallet.FromMnemonic(string.Join(' ', words));
        Assert.AreEqual(15, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_18Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(18);
        Assert.AreEqual(18, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_21Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(21);
        Assert.AreEqual(21, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_24Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(24);
        Assert.AreEqual(24, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_InvalidWordCount()
    {
        var ex1 = Helper.ThrowsException<ArgumentException>(() => XrplWallet.GenerateMnemonic(10));
        Assert.IsTrue(ex1.Message.Contains("Invalid word count: 10"));
        
        var ex2 = Helper.ThrowsException<ArgumentException>(() => XrplWallet.GenerateMnemonic(13));
        Assert.IsTrue(ex2.Message.Contains("Invalid word count: 13"));
    }

    [TestMethod]
    public void TestGenerateMnemonic_CreateWalletRoundtrip()
    {
        string[] words = XrplWallet.GenerateMnemonic(24);
        string mnemonic = string.Join(" ", words);
        
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic);
        
        Assert.IsNotNull(wallet);
        Assert.IsFalse(string.IsNullOrEmpty(wallet.PublicKey));
        Assert.IsFalse(string.IsNullOrEmpty(wallet.PrivateKey));
        Assert.IsFalse(string.IsNullOrEmpty(wallet.ClassicAddress));
    }

    [TestMethod]
    public void TestGenerateMnemonic_Randomness()
    {
        string[] words1 = XrplWallet.GenerateMnemonic();
        string[] words2 = XrplWallet.GenerateMnemonic();
        
        bool areDifferent = !words1.SequenceEqual(words2);
        Assert.IsTrue(areDifferent, "Two generated mnemonics should be different");
    }

    [TestMethod]
    public void TestValidateMnemonicWord_ValidWord()
    {
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("abandon"));
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("zoo"));
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("ability"));
    }

    [TestMethod]
    public void TestValidateMnemonicWord_CaseInsensitive()
    {
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("Abandon"));
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("ABANDON"));
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("aBaNdOn"));
    }

    [TestMethod]
    public void TestValidateMnemonicWord_InvalidWord()
    {
        Assert.IsFalse(XrplWallet.ValidateMnemonicWord("xyz123"));
        Assert.IsFalse(XrplWallet.ValidateMnemonicWord("notaword"));
        Assert.IsFalse(XrplWallet.ValidateMnemonicWord("abandonn"));
    }

    [TestMethod]
    public void TestValidateMnemonicWord_EmptyAndNull()
    {
        Assert.IsFalse(XrplWallet.ValidateMnemonicWord(""));
        Assert.IsFalse(XrplWallet.ValidateMnemonicWord("   "));
        Assert.IsFalse(XrplWallet.ValidateMnemonicWord(null));
    }

    [TestMethod]
    public void TestValidateMnemonicWord_WithWhitespace()
    {
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord(" abandon "));
        Assert.IsTrue(XrplWallet.ValidateMnemonicWord("  zoo  "));
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_PrefixMatch()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("aban");
        Assert.IsTrue(suggestions.Length > 0);
        Assert.Contains("abandon", suggestions);
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_ExactMatch()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("abandon");
        Assert.IsTrue(suggestions.Length == 1);
        Assert.AreEqual("abandon", suggestions[0]);
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_TypoCorrection()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("abandonn");
        Assert.IsTrue(suggestions.Length > 0);
        Assert.IsTrue(suggestions.Contains("abandon"));
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_MaxSuggestions()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("a", 3);
        Assert.AreEqual(3, suggestions.Length);
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_EmptyInput()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("");
        Assert.AreEqual(0, suggestions.Length);

        string[] suggestionsNull = XrplWallet.SuggestMnemonicWords(null);
        Assert.AreEqual(0, suggestionsNull.Length);
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_NoMatch()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("zzzzzzzzzzz");
        Assert.AreEqual(0, suggestions.Length);
    }

    [TestMethod]
    public void TestSuggestMnemonicWords_PrefixBeforeFuzzy()
    {
        string[] suggestions = XrplWallet.SuggestMnemonicWords("ab", 5);
        Assert.IsTrue(suggestions.Length > 0);
        foreach (var s in suggestions.Take(Math.Min(suggestions.Length, 3)))
        {
            Assert.IsTrue(s.StartsWith("ab"), $"Expected prefix match, got '{s}'");
        }
    }

    [TestMethod]
    public void TestValidateMnemonicChecksum_ValidMnemonic()
    {
        string[] words = "assault rare scout seed design extend noble drink talk control guitar quote".Split(' ');
        Assert.IsTrue(XrplWallet.ValidateMnemonicChecksum(words));
    }

    [TestMethod]
    public void TestValidateMnemonicChecksum_InvalidLastWord()
    {
        string[] words = "assault rare scout seed design extend noble drink talk control guitar abandon".Split(' ');
        Assert.IsFalse(XrplWallet.ValidateMnemonicChecksum(words));
    }

    [TestMethod]
    public void TestValidateMnemonicChecksum_InvalidWordInPhrase()
    {
        string[] words = "assault rare scout seed design extend noble drink talk control guitar notaword".Split(' ');
        Assert.IsFalse(XrplWallet.ValidateMnemonicChecksum(words));
    }

    [TestMethod]
    public void TestValidateMnemonicChecksum_WrongWordCount()
    {
        string[] words = "assault rare scout".Split(' ');
        Assert.IsFalse(XrplWallet.ValidateMnemonicChecksum(words));
    }

    [TestMethod]
    public void TestValidateMnemonicChecksum_NullAndEmpty()
    {
        Assert.IsFalse(XrplWallet.ValidateMnemonicChecksum(null));
        Assert.IsFalse(XrplWallet.ValidateMnemonicChecksum(Array.Empty<string>()));
    }

    [TestMethod]
    public void TestValidateMnemonicChecksum_GeneratedMnemonic()
    {
        string[] words = XrplWallet.GenerateMnemonic(12);
        Assert.IsTrue(XrplWallet.ValidateMnemonicChecksum(words));

        string[] words24 = XrplWallet.GenerateMnemonic(24);
        Assert.IsTrue(XrplWallet.ValidateMnemonicChecksum(words24));
    }

    [TestMethod]
    public void TestUHighFirstByteKey_SignsVerifiably()
    {
        // The BIP-39 test vector: its private key begins with 0x90, which a signed read of the
        // raw 32 bytes takes as a negative number.
        XrplWallet wallet = XrplWallet.FromMnemonic("abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about");
        byte[] message = System.Text.Encoding.UTF8.GetBytes("hello");

        Assert.AreEqual("0090802A50AA84EFB6CDB225F17C27616EA94048C179142FECF03F4712A07EA7A4", wallet.PrivateKey);
        Assert.IsTrue(XrplKeypairs.Verify(message, XrplKeypairs.Sign(message, wallet.PrivateKey), wallet.PublicKey));
    }
}
