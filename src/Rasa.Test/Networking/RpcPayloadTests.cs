using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Networking
{
    using Rasa.Data;
    using Rasa.Memory;
    using Rasa.Packets.Game.Client;
    using Rasa.Packets.Protocol;

    [TestClass]
    public class RpcPayloadTests
    {
        [ClassInitialize]
        public static void Initialize(TestContext context)
        {
            if (Logger.Config == null)
                Logger.UpdateConfig(new Logger.LoggerConfig());
        }

        [TestMethod]
        public void UnknownMethodIsNotReportedAsSuccessfullyDecoded()
        {
            var message = CreateMessage(uint.MaxValue, new byte[] { 0x4F, 0x66 });

            Assert.IsFalse(message.ReadPacket());
        }

        [TestMethod]
        public void ValidMethodPreservesItsPayload()
        {
            var message = CreateMessage((uint)GameOpcode.RequestFamilyName, new byte[] { 0x4F, 0x81, 0x10, 0x66 });

            Assert.IsTrue(message.ReadPacket());
            Assert.IsInstanceOfType<RequestFamilyNamePacket>(message.Packet);
            Assert.AreEqual(0, ((RequestFamilyNamePacket)message.Packet).LangId);
        }

        [TestMethod]
        public void MethodPayloadRejectsTrailingBytes()
        {
            var message = CreateMessage((uint)GameOpcode.RequestFamilyName, new byte[] { 0x4F, 0x81, 0x10, 0x66, 0 });

            Assert.IsFalse(message.ReadPacket());
        }

        [TestMethod]
        public void InvalidPythonTypeIsAProtocolError()
        {
            var message = CreateMessage((uint)GameOpcode.RequestFamilyName, new byte[] { 0x4F, 0x81, 0x00, 0x66 });

            Assert.ThrowsExactly<InvalidDataException>(() => message.ReadPacket());
        }

        [TestMethod]
        [DataRow((byte)0x6F)]
        [DataRow((byte)0x7F)]
        [DataRow((byte)0x8F)]
        public void NegativeCollectionCountsAreRejected(byte type)
        {
            using var binary = new BinaryReader(new MemoryStream(new byte[] { type, 255, 255, 255, 255 }));
            using var reader = new PythonReader(binary);

            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                if (type == 0x6F)
                    reader.ReadDictionary();
                else if (type == 0x7F)
                    reader.ReadList();
                else
                    reader.ReadTuple();
            });
        }

        [TestMethod]
        [DataRow((byte)0x4F)]
        [DataRow((byte)0x5F)]
        public void NegativeStringLengthsAreRejected(byte type)
        {
            using var binary = new BinaryReader(new MemoryStream(new byte[] { type, 255, 255, 255, 255 }));
            using var reader = new PythonReader(binary);

            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                if (type == 0x4F)
                    reader.ReadString();
                else
                    reader.ReadUnicodeString();
            });
        }

        [TestMethod]
        public void MalformedRpcDoesNotPreventAnotherPeerFromDispatching()
        {
            var malformed = Encode(CreateMessage((uint)GameOpcode.RequestFamilyName,
                new byte[] { 0x4F, 0x81, 0x00, 0x66 }));
            var valid = Encode(CreateMessage((uint)GameOpcode.RequestFamilyName,
                new byte[] { 0x4F, 0x81, 0x10, 0x66 }));
            using var badPeer = new ProtocolPacketDecoder();
            using var goodPeer = new ProtocolPacketDecoder();
            badPeer.Append(malformed, 0, malformed.Length);
            goodPeer.Append(valid, 0, valid.Length);
            var dispatched = 0;
            Exception error = null;

            void Handle(ProtocolPacket packet)
            {
                Assert.IsTrue(((CallServerMethodMessage)packet.Message).ReadPacket());
                dispatched++;
            }

            Assert.IsFalse(badPeer.ProcessPending(Handle, failure => error = failure));
            Assert.IsInstanceOfType<InvalidDataException>(error);
            Assert.AreEqual(0, dispatched);
            Assert.IsTrue(goodPeer.ProcessPending(Handle, _ => Assert.Fail("Valid RPC was rejected.")));
            Assert.AreEqual(1, dispatched);
        }

        [TestMethod]
        public void DisconnectBeforeCharacterSelectionDoesNotSaveAnUnassignedCharacter()
        {
            var client = new Rasa.Game.Client(null, new Rasa.Game.Handlers.ClientPacketHandler());
            Assert.AreEqual(0U, client.Player.Id);

            client.SaveCharacter();
        }

        private static byte[] Encode(CallServerMethodMessage message)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            new ProtocolPacket(message, ClientMessageOpcode.CallServerMethod, false, 0).Write(writer);
            return stream.ToArray();
        }

        private static CallServerMethodMessage CreateMessage(uint method, byte[] payload)
        {
            using var stream = new MemoryStream();
            using (var binary = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var writer = new ProtocolBufferWriter(binary, ProtocolBufferFlags.DontFragment))
            {
                writer.WriteUInt(method);
                writer.WriteArray(payload);
            }
            stream.Position = 0;
            using var input = new BinaryReader(stream);
            using var reader = new ProtocolBufferReader(input, ProtocolBufferFlags.DontFragment);
            var message = new CallServerMethodMessage { Subtype = CallServerMethodSubtype.UserMethodById };
            message.Read(reader);
            return message;
        }
    }
}
