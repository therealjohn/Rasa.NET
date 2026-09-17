using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Memory;
    using Rasa.Packets;
    using Rasa.Packets.Mission.Server;

    [TestClass]
    public class MissionTrackerTests
    {
        [TestMethod]
        public void OneFieldLifecyclePacketsUseTheObservedMissionIdTuple()
        {
            var packets = new PythonPacket[]
            {
                new MissionCompletedPacket(321),
                new MissionRewardedPacket(321),
                new MissionFailedPacket(321),
                new MissionDiscardedPacket(321),
                new MissionClearedPacket(321)
            };

            foreach (var packet in packets)
            {
                var bytes = MissionTestContext.Encode(packet);
                CollectionAssert.AreEqual(new byte[] { 0x81, 0x1E, 0x41, 0x01 }, bytes,
                    packet.GetType().Name);
                using var stream = new MemoryStream(bytes);
                using var reader = new PythonReader(new BinaryReader(stream));
                Assert.AreEqual(1, reader.ReadTuple(), packet.GetType().Name);
                Assert.AreEqual(321U, reader.ReadUInt(), packet.GetType().Name);
                Assert.AreEqual(stream.Length, stream.Position, packet.GetType().Name);
            }
        }

        [TestMethod]
        public void MissionCompleteableKeepsItsObservedTwoFieldTuple()
        {
            var bytes = MissionTestContext.Encode(new MissionCompleteablePacket(321, true));

            CollectionAssert.AreEqual(new byte[] { 0x82, 0x1E, 0x41, 0x01, 0x01 }, bytes);
            using var stream = new MemoryStream(bytes);
            using var reader = new PythonReader(new BinaryReader(stream));
            Assert.AreEqual(2, reader.ReadTuple());
            Assert.AreEqual(321U, reader.ReadUInt());
            Assert.IsTrue(reader.ReadBool());
            Assert.AreEqual(stream.Length, stream.Position);
        }
    }
}
