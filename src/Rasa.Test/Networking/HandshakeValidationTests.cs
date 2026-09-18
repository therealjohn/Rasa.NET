using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Networking
{
    using Rasa.Cryptography;

    [TestClass]
    public class HandshakeValidationTests
    {
        [TestMethod]
        [DataRow(0, false)]
        [DataRow(2, false)]
        [DataRow(4, false)]
        [DataRow(100, false)]
        [DataRow(0, true)]
        [DataRow(2, true)]
        [DataRow(4, true)]
        [DataRow(100, true)]
        public void AuthChecksumCoversTheFrameAtEveryBufferOffset(int offset, bool corrupt)
        {
            var data = new byte[offset + 64];
            for (var i = 0; i < offset; i++)
                data[i] = 0xA5;
            for (var i = 0; i < 13; i++)
                data[offset + i] = (byte)(i + 1);
            var length = 13;
            AuthCryptManager.Encrypt(data, offset, ref length, 64);
            if (corrupt)
                data[offset] ^= 128;

            Assert.AreEqual(!corrupt, AuthCryptManager.Decrypt(data, offset, length));
            for (var i = 0; i < offset; i++)
                Assert.AreEqual((byte)0xA5, data[i]);
        }

        [TestMethod]
        public void TruncatedAuthCredentialsFailBeforeBlockDecryption()
        {
            using var reader = new BinaryReader(new MemoryStream(new byte[8]));

            Assert.ThrowsExactly<EndOfStreamException>(() =>
                new Rasa.Packets.Auth.Client.LoginPacket().Read(reader));
        }

        [TestMethod]
        [DataRow(-1)]
        [DataRow(0)]
        [DataRow(65)]
        public void GameKeyLengthUsesTheExisting64ByteBound(int length)
        {
            using var reader = new BinaryReader(new MemoryStream(BitConverter.GetBytes(length)));

            Assert.ThrowsExactly<InvalidDataException>(() =>
                new Rasa.Packets.Login.Client.ClientKeyPacket().Read(reader));
        }

        [TestMethod]
        public void TruncatedGameKeyFailsBeforeBigNumberParsing()
        {
            using var reader = new BinaryReader(new MemoryStream(new byte[] { 4, 0, 0, 0, 1 }));

            Assert.ThrowsExactly<EndOfStreamException>(() =>
                new Rasa.Packets.Login.Client.ClientKeyPacket().Read(reader));
        }

        [TestMethod]
        public void TruncatedQueueKeyIsNotAcceptedAsAnEmptyString()
        {
            using var reader = new BinaryReader(new MemoryStream(new byte[] { 10, 0, 0, 0 }));

            Assert.ThrowsExactly<EndOfStreamException>(() =>
                new Rasa.Packets.Queue.Client.ClientKeyPacket().Read(reader));
        }
    }
}
