using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Networking
{
    using Rasa.Packets.Protocol;

    [TestClass]
    public class ProtocolPacketDecoderTests
    {
        [TestMethod]
        public void EveryFragmentBoundaryPreservesThePacket()
        {
            var bytes = ProtocolFramingTests.CreatePing(123);
            for (var split = 1; split < bytes.Length; split++)
            {
                using var decoder = new ProtocolPacketDecoder();
                decoder.Append(bytes, 0, split);
                Assert.IsNull(decoder.ReadNext(), $"Split {split} decoded before the packet was complete.");
                decoder.Append(bytes, split, bytes.Length - split);
                Assert.AreEqual(123U, ((PingMessage)decoder.ReadNext().Message).ClientTime);
                Assert.IsNull(decoder.ReadNext());
            }
        }

        [TestMethod]
        public void TimeoutDoesNotDelayFollowingPacket()
        {
            using var decoder = new ProtocolPacketDecoder();
            var timeout = new byte[] { 4, 0, 255, 0 };
            var ping = ProtocolFramingTests.CreatePing(123);
            decoder.Append(timeout, 0, timeout.Length);
            decoder.Append(ping, 0, ping.Length);

            var packet = decoder.ReadNext();

            Assert.IsNotNull(packet);
            Assert.AreEqual(123U, ((PingMessage)packet.Message).ClientTime);
            Assert.IsNull(decoder.ReadNext());
        }

        [TestMethod]
        public void OutOfOrderFrameDoesNotDelayFollowingPacket()
        {
            using var decoder = new ProtocolPacketDecoder();
            var initial = ProtocolFramingTests.CreatePing(1, 1, 10);
            var old = ProtocolFramingTests.CreatePing(2, 1, 9);
            var next = ProtocolFramingTests.CreatePing(3, 1, 11);
            decoder.Append(initial, 0, initial.Length);
            Assert.AreEqual(1U, ((PingMessage)decoder.ReadNext().Message).ClientTime);
            decoder.Append(old, 0, old.Length);
            decoder.Append(next, 0, next.Length);

            var packet = decoder.ReadNext();

            Assert.IsNotNull(packet);
            Assert.AreEqual(3U, ((PingMessage)packet.Message).ClientTime);
            Assert.AreEqual(11U, decoder.ReceiveSequence[1]);
        }

        [TestMethod]
        public void InvalidPeerInputDoesNotAffectAnotherDecoder()
        {
            using var invalid = new ProtocolPacketDecoder();
            using var valid = new ProtocolPacketDecoder();
            invalid.Append(new byte[] { 0, 0, 0, 0 }, 0, 4);
            var ping = ProtocolFramingTests.CreatePing(123);
            valid.Append(ping, 0, ping.Length);

            Assert.ThrowsExactly<InvalidDataException>(() => invalid.ReadNext());
            Assert.AreEqual(123U, ((PingMessage)valid.ReadNext().Message).ClientTime);
        }

        [TestMethod]
        public void DisposedDecoderRejectsLateInput()
        {
            using var decoder = new ProtocolPacketDecoder();
            decoder.Append(new byte[] { 14 }, 0, 1);
            decoder.Dispose();

            Assert.IsFalse(decoder.Append(new byte[] { 0 }, 0, 1));
            Assert.IsNull(decoder.ReadNext());
        }

        [TestMethod]
        public void InputFailureRejectsOnlyTheAffectedPeer()
        {
            using var invalid = new ProtocolPacketDecoder();
            using var valid = new ProtocolPacketDecoder();
            invalid.Append(new byte[] { 0, 0 }, 0, 2);
            var ping = ProtocolFramingTests.CreatePing(123);
            valid.Append(ping, 0, ping.Length);
            Exception rejected = null;
            uint received = 0;

            Assert.IsFalse(invalid.ProcessPending(_ => Assert.Fail("Invalid input was dispatched."),
                error => rejected = error));
            Assert.IsTrue(valid.ProcessPending(packet => received = ((PingMessage)packet.Message).ClientTime,
                _ => Assert.Fail("The valid peer was rejected.")));

            Assert.IsInstanceOfType<InvalidDataException>(rejected);
            Assert.AreEqual(123U, received);
            Assert.IsFalse(invalid.Append(ping, 0, ping.Length));
        }

        [TestMethod]
        public void MalformedPayloadIsRejectedAtTheDispatchBoundary()
        {
            using var decoder = new ProtocolPacketDecoder();
            var ping = ProtocolFramingTests.CreatePing(123);
            decoder.Append(ping, 0, ping.Length);
            var expected = new InvalidDataException("Malformed method payload.");
            Exception rejected = null;

            Assert.IsFalse(decoder.ProcessPending(_ => throw expected, error => rejected = error));
            Assert.AreSame(expected, rejected);
            Assert.IsNull(decoder.ReadNext());
        }

        [TestMethod]
        public void UnexpectedApplicationErrorsAreNotSuppressed()
        {
            using var decoder = new ProtocolPacketDecoder();
            var ping = ProtocolFramingTests.CreatePing(123);
            decoder.Append(ping, 0, ping.Length);

            Assert.ThrowsExactly<InvalidOperationException>(() => decoder.ProcessPending(
                _ => throw new InvalidOperationException("Application failure."),
                _ => Assert.Fail("Application failure was mistaken for invalid protocol input.")));
        }

        [TestMethod]
        public async Task ConcurrentReceiveAndDecodePreservePacketOrder()
        {
            using var decoder = new ProtocolPacketDecoder();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var producer = Task.Run(() =>
            {
                for (uint value = 0; value < 500; value++)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var bytes = ProtocolFramingTests.CreatePing(value);
                    Assert.IsTrue(decoder.Append(bytes, 0, bytes.Length));
                }
            });
            var consumer = Task.Run(() =>
            {
                for (uint expected = 0; expected < 500;)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var packet = decoder.ReadNext();
                    if (packet == null)
                    {
                        Thread.Yield();
                        continue;
                    }
                    Assert.AreEqual(expected++, ((PingMessage)packet.Message).ClientTime);
                }
            });

            await Task.WhenAll(producer, consumer);
            Assert.IsNull(decoder.ReadNext());
        }
    }
}
