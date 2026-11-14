using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// <see cref="XrplSigningKey"/> and the byte-based key API of <see cref="XrplKeypairs"/>
    /// (issue #347): every signature must match <see cref="XrplWallet"/> byte for byte. Both
    /// algorithms are deterministic - Ed25519 by design, secp256k1 through RFC 6979 - so the
    /// comparison is exact.
    /// </summary>
    [TestClass]
    public class TestUSigningKey
    {
        private const uint TfAllOrNothing = 0x00010000;
        private const uint TfInnerBatchTxn = 0x40000000;
        private const uint SpfSponsorFee = 1;

        private static readonly byte[] Entropy = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();
        private static readonly byte[] OtherEntropy = Enumerable.Range(1, 16).Select(i => (byte)(255 - i)).ToArray();
        private static readonly byte[] ThirdEntropy = Enumerable.Range(1, 16).Select(i => (byte)(i * 13 + 5)).ToArray();

        private static IEnumerable<object[]> Algorithms =>
            new[] { new object[] { XrplWallet.Ed25519 }, new object[] { XrplWallet.Secp256k1 } };

        private static Dictionary<string, object> ToDictionary(JsonObject tx) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(tx.ToJsonString(), XrplJsonOptions.Default);

        private static JsonObject Payment(string account, string destination) => new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = account,
            ["Destination"] = destination,
            ["Amount"] = "1000000",
            ["Sequence"] = 7u,
            ["Fee"] = "12",
        };

        private static void AssertSame(SignatureResult expected, SignatureResult actual)
        {
            Assert.AreEqual(expected.TxBlob, actual.TxBlob);
            Assert.AreEqual(expected.Hash, actual.Hash);
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void FromEntropy_MatchesTheWallet(string algorithm)
        {
            XrplWallet wallet = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            using XrplSigningKey key = XrplSigningKey.FromEntropy(Entropy, algorithm);

            Assert.AreEqual(wallet.PublicKey, key.PublicKey);
            Assert.AreEqual(wallet.ClassicAddress, key.ClassicAddress);
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void DeriveKeyBytes_MatchesTheWalletKeys(string algorithm)
        {
            XrplWallet wallet = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);

            (byte[] privateKey, byte[] publicKey) = XrplKeypairs.DeriveKeyBytes(Entropy, algorithm);

            Assert.AreEqual(wallet.PrivateKey, privateKey.ToHex());
            Assert.AreEqual(wallet.PublicKey, publicKey.ToHex());
            CollectionAssert.AreEqual(publicKey, XrplKeypairs.DerivePublicKey(privateKey));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void ByteSign_MatchesTheStringSign(string algorithm)
        {
            XrplWallet wallet = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            byte[] message = "5354580012000022800000002400000007".FromHex();

            byte[] signature = XrplKeypairs.Sign((ReadOnlySpan<byte>)message, wallet.PrivateKey.FromHex());

            Assert.AreEqual(XrplKeypairs.Sign(message, wallet.PrivateKey), signature.ToHex());
        }

        [TestMethod]
        public void ByteSign_RefusesAKeyThatIsNotInTheXrplForm()
        {
            byte[] message = { 1, 2, 3 };
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.Sign((ReadOnlySpan<byte>)message, new byte[32]));
            byte[] badPrefix = new byte[33];
            badPrefix[0] = 0x02;
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.Sign((ReadOnlySpan<byte>)message, badPrefix));
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.DeriveKeyBytes(new byte[15], XrplWallet.Ed25519));
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.DeriveKeyBytes(Entropy, "rsa"));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void KeyArray_IsPinned_AndNotMovedByCompaction(string algorithm)
        {
            XrplWallet wallet = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            using XrplSigningKey fromBytes = new XrplSigningKey(wallet.PrivateKey.FromHex());
            using XrplSigningKey fromEntropy = XrplSigningKey.FromEntropy(Entropy, algorithm);

            foreach (XrplSigningKey key in new[] { fromBytes, fromEntropy })
            {
                // Objects on the pinned object heap report the oldest generation from the start;
                // an ordinary new array is in generation 0.
                Assert.AreEqual(GC.MaxGeneration, GC.GetGeneration(key.KeyArray));

                IntPtr before = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(key.KeyArray, 0);
                for (int i = 0; i < 3; i++)
                {
                    _ = new byte[1024 * 64];
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                }
                Assert.AreEqual(before, System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(key.KeyArray, 0));
                CollectionAssert.AreEqual(wallet.PrivateKey.FromHex(), key.KeyBuffer.ToArray());
            }
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Sign_Single_IsByteIdentical(string algorithm)
        {
            XrplWallet wallet = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            XrplWallet destination = XrplWallet.FromEntropy(OtherEntropy);
            using XrplSigningKey key = new XrplSigningKey(wallet.PrivateKey.FromHex());

            JsonObject tx = Payment(wallet.ClassicAddress, destination.ClassicAddress);

            AssertSame(wallet.Sign(ToDictionary(tx)), key.Sign(ToDictionary(tx)));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Sign_Multisign_IsByteIdentical(string algorithm)
        {
            XrplWallet signer = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            XrplWallet owner = XrplWallet.FromEntropy(OtherEntropy);
            using XrplSigningKey key = XrplSigningKey.FromEntropy(Entropy, algorithm);

            JsonObject tx = Payment(owner.ClassicAddress, XrplWallet.FromEntropy(ThirdEntropy).ClassicAddress);

            AssertSame(
                signer.Sign(ToDictionary(tx), true, signer.ClassicAddress),
                key.Sign(ToDictionary(tx), true, key.ClassicAddress));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Sign_RegularKeyWithMasterAddress_IsByteIdentical(string algorithm)
        {
            string master = XrplWallet.FromEntropy(OtherEntropy).ClassicAddress;
            XrplWallet wallet = XrplWallet.FromEntropy(Entropy, master, algorithm);
            using XrplSigningKey key = XrplSigningKey.FromEntropy(Entropy, algorithm, master);

            Assert.AreEqual(master, key.ClassicAddress);
            JsonObject tx = Payment(master, XrplWallet.FromEntropy(ThirdEntropy).ClassicAddress);

            AssertSame(wallet.Sign(ToDictionary(tx)), key.Sign(ToDictionary(tx)));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Sign_SponsorCoSignature_IsByteIdentical(string algorithm)
        {
            XrplWallet sponsor = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            XrplWallet submitter = XrplWallet.FromEntropy(OtherEntropy);
            using XrplSigningKey key = XrplSigningKey.FromEntropy(Entropy, algorithm);

            JsonObject tx = Payment(submitter.ClassicAddress, XrplWallet.FromEntropy(ThirdEntropy).ClassicAddress);
            tx["SigningPubKey"] = submitter.PublicKey;
            tx["Sponsor"] = sponsor.ClassicAddress;
            tx["SponsorFlags"] = SpfSponsorFee;

            AssertSame(sponsor.Sign(ToDictionary(tx)), key.Sign(ToDictionary(tx)));
            AssertSame(
                sponsor.Sign(ToDictionary(tx), false, null, SignatureRole.Sponsor),
                key.Sign(ToDictionary(tx), false, null, SignatureRole.Sponsor));
        }

        private static JsonObject Batch(string outerAccount, string innerAccount, string destination)
        {
            JsonObject Inner(string account, uint sequence) => new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Account"] = account,
                ["Destination"] = destination,
                ["Amount"] = "1000000",
                ["Sequence"] = sequence,
                ["Fee"] = "0",
                ["SigningPubKey"] = "",
                ["Flags"] = TfInnerBatchTxn,
            };

            return new JsonObject
            {
                ["TransactionType"] = "Batch",
                ["Account"] = outerAccount,
                ["Sequence"] = 10u,
                ["Flags"] = TfAllOrNothing,
                ["Fee"] = "40",
                ["RawTransactions"] = new JsonArray
                {
                    new JsonObject { ["RawTransaction"] = Inner(outerAccount, 11u) },
                    new JsonObject { ["RawTransaction"] = Inner(innerAccount, 3u) },
                },
            };
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Sign_BatchOuterAndInnerSigners_AreByteIdentical(string algorithm)
        {
            XrplWallet outer = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            XrplWallet inner = XrplWallet.FromEntropy(OtherEntropy, algorithm: algorithm);
            using XrplSigningKey outerKey = XrplSigningKey.FromEntropy(Entropy, algorithm);
            using XrplSigningKey innerKey = XrplSigningKey.FromEntropy(OtherEntropy, algorithm);

            JsonObject batch = Batch(outer.ClassicAddress, inner.ClassicAddress, XrplWallet.FromEntropy(ThirdEntropy).ClassicAddress);

            SignatureResult walletInner = inner.Sign(ToDictionary(batch));
            AssertSame(walletInner, innerKey.Sign(ToDictionary(batch)));

            AssertSame(outer.Sign(walletInner.GetTxDictionary()), outerKey.Sign(walletInner.GetTxDictionary()));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Sign_BatchInnerMultisign_IsByteIdentical(string algorithm)
        {
            XrplWallet outer = XrplWallet.FromEntropy(ThirdEntropy);
            XrplWallet owner = XrplWallet.FromEntropy(OtherEntropy);
            XrplWallet signer = XrplWallet.FromEntropy(Entropy, algorithm: algorithm);
            using XrplSigningKey key = XrplSigningKey.FromEntropy(Entropy, algorithm);

            JsonObject batch = Batch(outer.ClassicAddress, owner.ClassicAddress, outer.ClassicAddress);

            AssertSame(
                signer.Sign(ToDictionary(batch), true, owner.ClassicAddress),
                key.Sign(ToDictionary(batch), true, owner.ClassicAddress));
        }

        [TestMethod]
        [DynamicData(nameof(Algorithms))]
        public void Dispose_ClearsTheKeyAndRefusesToSign(string algorithm)
        {
            XrplSigningKey key = XrplSigningKey.FromEntropy(Entropy, algorithm);
            Assert.IsTrue(key.KeyBuffer.ToArray().Any(b => b != 0));
            JsonObject tx = Payment(key.ClassicAddress, XrplWallet.FromEntropy(OtherEntropy).ClassicAddress);

            key.Dispose();
            key.Dispose();

            Assert.IsTrue(key.KeyBuffer.ToArray().All(b => b == 0));
            Assert.ThrowsExactly<ObjectDisposedException>(() => key.Sign(ToDictionary(tx)));
        }

        [TestMethod]
        public void Constructor_RefusesAKeyThatIsNotInTheXrplForm()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new XrplSigningKey(new byte[32]));
            byte[] badPrefix = new byte[33];
            badPrefix[0] = 0x02;
            Assert.ThrowsExactly<ArgumentException>(() => new XrplSigningKey(badPrefix));
        }

        [TestMethod]
        public void ClearedOrOutOfRangeSecp256k1Key_IsRefused()
        {
            byte[] message = { 1, 2, 3 };
            byte[] cleared = new byte[33];
            Assert.ThrowsExactly<ArgumentException>(() => new XrplSigningKey(cleared));
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.Sign((ReadOnlySpan<byte>)message, cleared));

            byte[] aboveOrder = new byte[33];
            for (int i = 1; i < 33; i++)
                aboveOrder[i] = 0xFF;
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.DerivePublicKey(aboveOrder));
        }

        [TestMethod]
        public void Constructor_CopiesTheCallersBuffer()
        {
            byte[] privateKey = XrplWallet.FromEntropy(Entropy).PrivateKey.FromHex();
            using XrplSigningKey key = new XrplSigningKey(privateKey);

            Array.Clear(privateKey, 0, privateKey.Length);

            JsonObject tx = Payment(key.ClassicAddress, XrplWallet.FromEntropy(OtherEntropy).ClassicAddress);
            AssertSame(XrplWallet.FromEntropy(Entropy).Sign(ToDictionary(tx)), key.Sign(ToDictionary(tx)));
        }
    }
}
