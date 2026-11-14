using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Xrpl.AddressCodec.Tests
{
    /// <summary>
    /// <see cref="Utils.ToHex"/> and <see cref="Utils.FromHex"/> replaced BouncyCastle's <c>Hex</c>:
    /// the same output and the same accepted inputs, with documented exceptions for the rest.
    /// </summary>
    [TestClass]
    public class TestUHex
    {
        [TestMethod]
        public void RoundTrip_RandomBytes()
        {
            Random random = new Random(1);
            for (int length = 0; length < 70; length++)
            {
                byte[] bytes = new byte[length];
                random.NextBytes(bytes);

                string hex = bytes.ToHex();

                Assert.AreEqual(BitConverter.ToString(bytes).Replace("-", ""), hex);
                CollectionAssert.AreEqual(bytes, hex.FromHex());
                CollectionAssert.AreEqual(bytes, hex.ToLowerInvariant().FromHex());
            }
        }

        [TestMethod]
        public void FromHex_SkipsWhitespaceBetweenDigits()
        {
            CollectionAssert.AreEqual(new byte[] { 0x0A, 0x0B }, " 0a 0B\n".FromHex());
            CollectionAssert.AreEqual(new byte[] { 0x0A }, "0 a".FromHex());
            CollectionAssert.AreEqual(new byte[] { 0xAB }, "\ta\r\nb".FromHex());
            CollectionAssert.AreEqual(Array.Empty<byte>(), "\t".FromHex());
            CollectionAssert.AreEqual(Array.Empty<byte>(), "".FromHex());
        }

        [TestMethod]
        public void FromHex_Invalid_ThrowsFormatException()
        {
            string[] invalid = { "0", "abc", "0G", "zz", "-1", "0x00", "éé", "٠١", "ab\0", "a b" };
            foreach (string hex in invalid)
            {
                Assert.ThrowsExactly<FormatException>(() => hex.FromHex(), hex);
            }
        }

        [TestMethod]
        public void FromHex_Invalid_MessageDoesNotEchoTheInput()
        {
            const string key = "00D78B9735C3F26501C7337B8A5727FD53A6EFDBC6AA55984F098488561F985E2";
            FormatException odd = Assert.ThrowsExactly<FormatException>(() => key.FromHex());
            FormatException invalid = Assert.ThrowsExactly<FormatException>(() => (key + "Z").FromHex());

            Assert.IsFalse(odd.Message.Contains("D78B97"), odd.Message);
            Assert.IsFalse(invalid.Message.Contains("D78B97"), invalid.Message);
            Assert.IsFalse(invalid.Message.Contains("'Z'"), invalid.Message);
            StringAssert.Contains(invalid.Message, "index 65");
        }

        [TestMethod]
        public void Null_ThrowsArgumentNullException()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((string)null).FromHex());
            Assert.ThrowsExactly<ArgumentNullException>(() => ((byte[])null).ToHex());
        }
    }
}
