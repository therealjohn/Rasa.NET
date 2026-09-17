using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.World
{
    using Rasa.Memory;
    using Rasa.Packets.Protocol;
    using Rasa.Test.Networking;

    [TestClass]
    public class MovementEncodingTests
    {
        [TestMethod]
        [DataRow(-8388608)]
        [DataRow(-256)]
        [DataRow(-1)]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(8388607)]
        public void PackedCoordinatesPreserveSigned24BitValues(int packed)
        {
            using var stream = new MemoryStream();
            using (var binary = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var writer = new ProtocolBufferWriter(binary, ProtocolBufferFlags.DontFragment))
                writer.WritePackedFloat(packed);
            stream.Position = 0;
            using var input = new BinaryReader(stream);
            using var reader = new ProtocolBufferReader(input, ProtocolBufferFlags.DontFragment);

            Assert.AreEqual(packed / 256.0f, reader.ReadPackedFloat());
        }

        [TestMethod]
        public void DuplicateChannelSequenceDoesNotReplayMovement()
        {
            using var decoder = new ProtocolPacketDecoder();
            Append(decoder, 1, 10);
            Assert.AreEqual(1U, ((PingMessage)decoder.ReadNext().Message).ClientTime);
            Append(decoder, 2, 10);
            Append(decoder, 3, 11);

            Assert.AreEqual(3U, ((PingMessage)decoder.ReadNext().Message).ClientTime);
            Assert.IsNull(decoder.ReadNext());
        }

        [TestMethod]
        public void ChannelSequenceContinuesAcrossUnsignedWrap()
        {
            using var decoder = new ProtocolPacketDecoder();
            Append(decoder, 1, uint.MaxValue);
            Assert.AreEqual(1U, ((PingMessage)decoder.ReadNext().Message).ClientTime);
            Append(decoder, 2, 0);

            var wrapped = decoder.ReadNext();

            Assert.IsNotNull(wrapped);
            Assert.AreEqual(2U, ((PingMessage)wrapped.Message).ClientTime);
        }

        private static void Append(ProtocolPacketDecoder decoder, uint value, uint sequence)
        {
            var bytes = ProtocolFramingTests.CreatePing(value, 1, sequence);
            decoder.Append(bytes, 0, bytes.Length);
        }
    }
}
