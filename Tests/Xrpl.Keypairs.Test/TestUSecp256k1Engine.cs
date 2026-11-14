using System;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Keypairs.K256;

using static Xrpl.AddressCodec.Utils;

namespace Xrpl.Keypairs.Tests
{
    /// <summary>
    /// secp256k1 runs on NBitcoin.Secp256k1. The xrpl.js vectors in <see cref="TestKeypairs"/> pin the
    /// derived keys and the signature; these tests cover the paths those vectors do not reach.
    /// </summary>
    [TestClass]
    public class TestUSecp256k1Engine
    {
        [TestMethod]
        public void DeriveKeyBytes_MatchesTheSeedDerivation()
        {
            Random random = new Random(1113);
            for (int round = 0; round < 100; round++)
            {
                byte[] entropy = new byte[16];
                random.NextBytes(entropy);

                IXrplKeyPair expected = XrplKeypairs.DeriveKeypair(XrplKeypairs.GenerateSeed(entropy, "secp256k1"));
                (byte[] privateKey, byte[] publicKey) = XrplKeypairs.DeriveKeyBytes(entropy, "secp256k1");

                Assert.AreEqual(expected.Pk(), privateKey.ToHex(), $"private key, round {round}");
                Assert.AreEqual(expected.Id(), publicKey.ToHex(), $"public key, round {round}");
                CollectionAssert.AreEqual(publicKey, XrplKeypairs.DerivePublicKey(privateKey), $"DerivePublicKey, round {round}");
            }
        }

        [TestMethod]
        public void PublicDerivation_MatchesPrivateDerivation()
        {
            Random random = new Random(42);
            for (int round = 0; round < 50; round++)
            {
                byte[] seed = new byte[16];
                random.NextBytes(seed);
                byte[] privateGen = K256KeyGenerator.ComputePrivateGen(seed);
                byte[] publicGen = K256KeyGenerator.ComputePublicGenerator(privateGen);

                CollectionAssert.AreEqual(
                    K256KeyGenerator.ComputePublicKey(K256KeyGenerator.ComputeSecretKey(privateGen, 3)),
                    K256KeyGenerator.ComputePublicKey(publicGen, 3),
                    $"round {round}");
                Assert.AreEqual("00" + K256KeyGenerator.ComputeSecretKey(privateGen, 0).ToHex(), K256KeyGenerator.From128Seed(seed, 0).Pk(), $"round {round}");
                Assert.AreEqual("00" + privateGen.ToHex(), K256KeyGenerator.From128Seed(seed, -1).Pk(), $"root, round {round}");
            }
        }

        [TestMethod]
        public void Sign_EveryKeyFormSignsTheSame()
        {
            Random random = new Random(20261113);
            for (int round = 0; round < 50; round++)
            {
                byte[] scalar = K256KeyGenerator.ComputePrivateGen(RandomBytes(random, 16));
                byte[] message = RandomBytes(random, random.Next(0, 300));
                byte[] xrplKey = new byte[33];
                Buffer.BlockCopy(scalar, 0, xrplKey, 1, 32);

                byte[] expected = XrplKeypairs.Sign((ReadOnlySpan<byte>)message, xrplKey);
                CollectionAssert.AreEqual(expected, K256KeyPair.Sign(message, scalar), $"raw 32 bytes, round {round}");
                Assert.AreEqual(expected.ToHex(), XrplKeypairs.Sign(message, xrplKey.ToHex()), $"hex, round {round}");
                Assert.IsTrue(EcdsaSignature.IsStrictlyCanonical(expected), $"canonical, round {round}");
                Assert.IsTrue(K256KeyPair.Verify(expected, message, XrplKeypairs.DerivePublicKey(xrplKey)), $"verify, round {round}");
            }
        }

        [TestMethod]
        public void Sign_KeyWithLeadingZeroBytes_ReadsAsTheSameScalar()
        {
            byte[] scalar = new byte[32];
            scalar[30] = 0x12;
            scalar[31] = 0x34;
            byte[] message = { 1, 2, 3 };
            byte[] expected = K256KeyPair.Sign(message, scalar);

            CollectionAssert.AreEqual(expected, K256KeyPair.Sign(message, new byte[] { 0x12, 0x34 }));
            CollectionAssert.AreEqual(expected, K256KeyPair.Sign(message, new byte[] { 0, 0, 0, 0x12, 0x34 }));
            byte[] padded = new byte[40];
            padded[38] = 0x12;
            padded[39] = 0x34;
            CollectionAssert.AreEqual(expected, K256KeyPair.Sign(message, padded));
        }

        [TestMethod]
        public void Sign_InvalidScalar_Throws()
        {
            byte[] order = Secp256K1.Order().ToByteArray(isUnsigned: true, isBigEndian: true);
            byte[] message = { 1 };

            Assert.ThrowsExactly<ArgumentException>(() => K256KeyPair.Sign(message, new byte[32]));
            Assert.ThrowsExactly<ArgumentException>(() => K256KeyPair.Sign(message, order));
            byte[] tooLong = new byte[33];
            tooLong[0] = 1;
            Assert.ThrowsExactly<ArgumentException>(() => K256KeyPair.Sign(message, tooLong));
            Assert.ThrowsExactly<ArgumentException>(() => XrplKeypairs.Sign((ReadOnlySpan<byte>)message, new byte[33]));
        }

        [TestMethod]
        public void Verify_HighS_Verifies_OtherMessage_DoesNot()
        {
            byte[] scalar = K256KeyGenerator.ComputePrivateGen(new byte[16]);
            byte[] message = { 9, 8, 7 };
            byte[] publicKey = K256KeyGenerator.ComputePublicKey(scalar);
            byte[] signature = K256KeyPair.Sign(message, scalar);
            EcdsaSignature decoded = EcdsaSignature.DecodeFromDer(signature);
            byte[] highS = new EcdsaSignature(decoded.R, Secp256K1.Order() - decoded.S).EncodeToDer();

            Assert.IsFalse(EcdsaSignature.IsStrictlyCanonical(highS));
            Assert.IsTrue(K256KeyPair.Verify(highS, message, publicKey));
            Assert.IsFalse(K256KeyPair.Verify(signature, new byte[] { 9, 8, 6 }, publicKey));
            Assert.IsFalse(K256KeyPair.Verify(new EcdsaSignature(decoded.R, BigInteger.Zero).EncodeToDer(), message, publicKey));
            Assert.IsFalse(K256KeyPair.Verify(new EcdsaSignature(Secp256K1.Order(), decoded.S).EncodeToDer(), message, publicKey));
        }

        [TestMethod]
        public void Verify_InvalidPublicKey_Throws()
        {
            byte[] notOnCurve = new byte[33];
            notOnCurve[0] = 0x02;
            notOnCurve[32] = 0x05;
            byte[] signature = K256KeyPair.Sign(new byte[] { 1 }, K256KeyGenerator.ComputePrivateGen(new byte[16]));

            Assert.ThrowsExactly<ArgumentException>(() => K256KeyPair.Verify(signature, new byte[] { 1 }, notOnCurve));
            Assert.ThrowsExactly<ArgumentException>(() => K256KeyPair.Verify(signature, new byte[] { 1 }, new byte[32]));
        }

        [TestMethod]
        public void EncodeToDer_IsMinimalAndRoundTrips()
        {
            CollectionAssert.AreEqual(new byte[] { 0x30, 0x06, 0x02, 0x01, 0x00, 0x02, 0x01, 0x7F },
                new EcdsaSignature(BigInteger.Zero, new BigInteger(0x7F)).EncodeToDer());
            CollectionAssert.AreEqual(new byte[] { 0x30, 0x07, 0x02, 0x02, 0x00, 0x80, 0x02, 0x01, 0x01 },
                new EcdsaSignature(new BigInteger(0x80), BigInteger.One).EncodeToDer());

            Random random = new Random(5);
            for (int round = 0; round < 100; round++)
            {
                BigInteger r = new BigInteger(RandomBytes(random, random.Next(1, 40)), isUnsigned: true, isBigEndian: true);
                BigInteger s = new BigInteger(RandomBytes(random, random.Next(1, 160)), isUnsigned: true, isBigEndian: true);
                EcdsaSignature decoded = EcdsaSignature.DecodeFromDer(new EcdsaSignature(r, s).EncodeToDer());

                Assert.IsNotNull(decoded, $"round {round}");
                Assert.AreEqual(r, decoded.R, $"round {round}");
                Assert.AreEqual(s, decoded.S, $"round {round}");
            }
        }

        [TestMethod]
        public void DecodeFromDer_NonMinimalOrNotTwoIntegers_ReturnsNull()
        {
            byte[][] invalid =
            {
                new byte[] { 0x30, 0x81, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01 },
                new byte[] { 0x30, 0x07, 0x02, 0x02, 0x00, 0x01, 0x02, 0x01, 0x01 },
                new byte[] { 0x30, 0x07, 0x02, 0x02, 0xFF, 0x80, 0x02, 0x01, 0x01 },
                new byte[] { 0x30, 0x05, 0x02, 0x00, 0x02, 0x01, 0x01 },
                new byte[] { 0x30, 0x09, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01 },
                new byte[] { 0x30, 0x80, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x00, 0x00 },
                new byte[] { 0x30, 0x06, 0x02, 0x01, 0x01, 0x05, 0x01, 0x00 },
            };
            foreach (byte[] bytes in invalid)
            {
                Assert.IsNull(EcdsaSignature.DecodeFromDer(bytes), bytes.ToHex());
            }

            EcdsaSignature negative = EcdsaSignature.DecodeFromDer(new byte[] { 0x30, 0x06, 0x02, 0x01, 0x80, 0x02, 0x01, 0x01 });
            Assert.AreEqual(new BigInteger(0x80), negative.R);
        }

        [TestMethod]
        public void NullArguments_ThrowArgumentNullException()
        {
            byte[] key = K256KeyGenerator.ComputePrivateGen(new byte[16]);
            byte[] publicKey = K256KeyGenerator.ComputePublicKey(key);

            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.From128Seed(null, 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.ComputePrivateGen(null));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.ComputeScalar(null, 1));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.ComputeSecretKey(null, 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.ComputePublicGenerator(null));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.ComputePublicKey(null));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyGenerator.ComputePublicKey(null, 0));
            Assert.ThrowsExactly<ArgumentNullException>(() => new K256KeyPair(null));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyPair.Sign(null, key));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyPair.Sign(new byte[] { 1 }, null));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyPair.Verify(new byte[8], null, publicKey));
            Assert.ThrowsExactly<ArgumentNullException>(() => K256KeyPair.Verify(new byte[8], new byte[] { 1 }, null));
        }

        [TestMethod]
        public void ComputeScalar_LargeInput_DoesNotUseTheStack()
        {
            byte[] input = new byte[4 * 1024 * 1024];
            byte[] scalar = null;
            Exception failure = null;
            // A 256 KiB stack: an input copied to it would overflow and kill the process.
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                try
                {
                    scalar = K256KeyGenerator.ComputeScalar(input, 7);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }, 256 * 1024);
            thread.Start();
            thread.Join();

            Assert.IsNull(failure);
            Assert.AreEqual(32, scalar.Length);
            CollectionAssert.AreEqual(scalar, K256KeyGenerator.ComputeScalar(input, 7));
        }

        private static byte[] RandomBytes(Random random, int length)
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }
    }
}
