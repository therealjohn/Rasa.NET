using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Networking
{
    using Rasa.Memory;

    [TestClass]
    public class ProtocolValueReaderTests
    {
        [TestMethod]
        public void UnsupportedFlagsAreInvalidInput()
        {
            using var binary = new BinaryReader(new MemoryStream(new byte[] { 64 }));
            using var reader = new ProtocolBufferReader(binary, ProtocolBufferFlags.DontFragment);

            Assert.ThrowsExactly<InvalidDataException>(() => reader.ReadProtocolFlags());
        }

        [TestMethod]
        public void IncorrectChecksumIsInvalidInput()
        {
            using var binary = new BinaryReader(new MemoryStream(new byte[] { 0 }));
            using var reader = new ProtocolBufferReader(binary, ProtocolBufferFlags.DontFragment);

            Assert.ThrowsExactly<InvalidDataException>(() => reader.ReadXORCheck(1));
        }

        [TestMethod]
        public void TruncatedCountDoesNotBecomeAValidLength()
        {
            using var binary = new BinaryReader(new MemoryStream(new byte[] { 0xC1 }));
            using var reader = new ProtocolBufferReader(binary, ProtocolBufferFlags.DontFragment);

            Assert.ThrowsExactly<EndOfStreamException>(() => reader.ReadCount());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TruncatedValuesAreRejected(bool text)
        {
            using var binary = new BinaryReader(new MemoryStream(new byte[] { 5, 65 }));
            using var reader = new ProtocolBufferReader(binary, ProtocolBufferFlags.DontFragment);

            Assert.ThrowsExactly<EndOfStreamException>(() =>
            {
                if (text)
                    reader.ReadString();
                else
                    reader.ReadArray();
            });
        }
    }
}
