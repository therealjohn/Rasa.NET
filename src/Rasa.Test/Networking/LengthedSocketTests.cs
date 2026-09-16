using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Networking
{
    using Rasa.Memory;
    using Rasa.Networking;

    [TestClass]
    [DoNotParallelize]
    public class LengthedSocketTests
    {
        [ClassInitialize]
        public static void Initialize(TestContext context)
        {
            if (Logger.Config == null)
                Logger.UpdateConfig(new Logger.LoggerConfig());
            BufferManager.Initialize(8192, 8, 8);
            LengthedSocket.InitializeEventArgsPool(64);
        }

        [TestMethod]
        public void PartialFollowingFrameReusesTheEntireBuffer()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var transport = new LengthedSocket(socket, SizeType.Dword, false);
            var data = BufferManager.RequestBuffer();
            try
            {
                data.BaseOffset += 1024;
                data.ByteCount = 4;
                BitConverter.GetBytes(7996).CopyTo(data.Buffer, data.BaseOffset);

                Assert.IsFalse(transport.PrepareFrame(data));

                Assert.AreEqual(data.RealBaseOffset, data.BaseOffset);
                Assert.AreEqual(8000, data.Length);
                Assert.AreEqual(7996, BitConverter.ToInt32(data.Buffer, data.BaseOffset));
                Assert.AreEqual(4, data.ByteCount);
            }
            finally
            {
                BufferManager.FreeBuffer(data);
            }
        }

        [TestMethod]
        public void PartialHeaderAtBufferEndIsCompacted()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var transport = new LengthedSocket(socket, SizeType.Dword, false);
            var data = BufferManager.RequestBuffer();
            try
            {
                data.BaseOffset += BufferManager.BlockSize - 2;
                data.Length = data.MaxLength;
                data.ByteCount = 2;
                data[0] = 4;
                data[1] = 0;

                Assert.IsFalse(transport.PrepareFrame(data));

                Assert.AreEqual(data.RealBaseOffset, data.BaseOffset);
                Assert.AreEqual(BufferManager.BlockSize, data.Length);
                Assert.AreEqual((byte)4, data[0]);
                Assert.AreEqual(2, data.ByteCount);
            }
            finally
            {
                BufferManager.FreeBuffer(data);
            }
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(8193)]
        [DataRow(int.MaxValue)]
        [DataRow(-1)]
        public void InvalidTransportLengthHasAnExplicitDataError(int length)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var transport = new LengthedSocket(socket, SizeType.Dword, true);
            var data = BufferManager.RequestBuffer();
            try
            {
                data.ByteCount = 4;
                BitConverter.GetBytes(length).CopyTo(data.Buffer, data.BaseOffset);

                Assert.ThrowsExactly<InvalidDataException>(() => transport.PrepareFrame(data));
            }
            finally
            {
                BufferManager.FreeBuffer(data);
            }
        }

        [TestMethod]
        public async Task FailedDecryptionDoesNotDeliverPayload()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await sender.ConnectAsync(listener.LocalEndPoint);
            using var accepted = await listener.AcceptAsync();
            var transport = new LengthedSocket(accepted, SizeType.Dword, false) { AutoReceive = false };
            var outcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.OnDecrypt = _ => false;
            transport.OnReceive = _ => outcome.TrySetResult(false);
            transport.OnError = _ => outcome.TrySetResult(true);
            transport.ReceiveAsync();
            using var output = new NetworkStream(sender, false);
            await output.WriteAsync(new byte[] { 4, 0, 0, 0, 1, 2, 3, 4 });

            Assert.IsTrue(await outcome.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                "Rejected ciphertext must raise a transport error, not invoke the payload handler.");
        }

        [TestMethod]
        [DataRow((byte)0, false)]
        [DataRow((byte)1, true)]
        [DataRow((byte)8, true)]
        [DataRow((byte)9, false)]
        [DataRow((byte)15, false)]
        [DataRow((byte)17, true)]
        public void GameCipherPaddingIsValidated(byte padding, bool accepted)
        {
            var key = new Rasa.Cryptography.ClientCryptData();
            Rasa.Cryptography.GameCryptManager.Initialize(key, new byte[64]);
            var data = BufferManager.RequestBuffer();
            try
            {
                data.Offset = 4;
                data.Length = 28;
                data[4] = padding;
                var length = data.RemainingLength;
                Rasa.Cryptography.GameCryptManager.Encrypt(data.Buffer, data.BaseOffset + data.Offset,
                    ref length, length, key);

                Assert.AreEqual(accepted, Rasa.Game.Client.DecryptFrame(data, key));
                Assert.AreEqual(accepted ? 4 + (padding & 15) : 4, data.Offset);
            }
            finally
            {
                BufferManager.FreeBuffer(data);
            }
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(7)]
        [DataRow(9)]
        public void InvalidCipherLengthIsRejectedBeforeDecryption(int length)
        {
            var data = BufferManager.RequestBuffer();
            try
            {
                data.Length = length;

                Assert.IsFalse(Rasa.Game.Client.DecryptFrame(data, new Rasa.Cryptography.ClientCryptData()));
            }
            finally
            {
                BufferManager.FreeBuffer(data);
            }
        }
    }
}
