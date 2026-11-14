using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Keypairs.K256;

namespace Xrpl.Keypairs.Tests
{
    [TestClass]
    public class TestUEcdsaDer
    {
        [TestMethod]
        public void DecodeFromDer_RoundTrip_Works()
        {
            EcdsaSignature signature = new EcdsaSignature(new System.Numerics.BigInteger(12345), new System.Numerics.BigInteger(67890));
            EcdsaSignature decoded = EcdsaSignature.DecodeFromDer(signature.EncodeToDer());
            Assert.IsNotNull(decoded);
            Assert.AreEqual(signature.R, decoded.R);
            Assert.AreEqual(signature.S, decoded.S);
        }

        [TestMethod]
        public void DecodeFromDer_Malformed_ReturnsNull()
        {
            byte[][] malformed =
            {
                null,
                new byte[0],
                new byte[] { 0x30 },
                new byte[] { 0x30, 0x20, 0x02, 0x01 },
                new byte[] { 0x30, 0x00 },
                new byte[] { 0x30, 0x03, 0x02, 0x01, 0x01 },
                new byte[] { 0x02, 0x01, 0x01 },
                new byte[] { 0x30, 0x06, 0x04, 0x01, 0x01, 0x04, 0x01, 0x01 },
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF },
            };

            foreach (byte[] bytes in malformed)
            {
                Assert.IsNull(EcdsaSignature.DecodeFromDer(bytes), bytes == null ? "null" : BitConverter.ToString(bytes));
            }
        }

        [TestMethod]
        public void DecodeFromDer_TrailingBytes_ReturnsNull()
        {
            byte[] der = new EcdsaSignature(new System.Numerics.BigInteger(12345), new System.Numerics.BigInteger(67890)).EncodeToDer();
            byte[] withTrailer = new byte[der.Length + 1];
            der.CopyTo(withTrailer, 0);

            Assert.IsNull(EcdsaSignature.DecodeFromDer(withTrailer));
        }
    }
}
