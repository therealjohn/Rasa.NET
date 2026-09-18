using System.Net.Sockets;
using System.Threading;

namespace Rasa.Login
{
    using Cryptography;
    using Memory;
    using Networking;
    using Packets.Login.Client;
    using Packets.Login.Server;

    public class LoginClient
    {
        public LengthedSocket Socket { get; }
        public LoginManager Manager { get; }
        public ClientCryptData Data { get; } = new ClientCryptData();

        // ReSharper disable once InconsistentNaming
        public BigNum PrivateKey { get; } = new BigNum();
        public BigNum PublicKey { get; } = new BigNum();
        public BigNum K { get; } = new BigNum();
        private int _closed;

        public LoginClient(LoginManager manager, LengthedSocket socket)
        {
            Manager = manager;

            Socket = socket;
            Socket.AutoReceive = false;
            Socket.OnReceive += OnReceive;
            Socket.OnError += OnError;
            Socket.OnDrop += OnDrop;

            DHKeyExchange.GeneratePrivateAndPublicA(PrivateKey, PublicKey);

            Socket.Send(new ServerKeyPacket
            {
                PublicKey = PublicKey,
                Prime = DHKeyExchange.ConstantPrime,
                Generator = DHKeyExchange.ConstantGenerator
            });

            Socket.OnEncrypt += OnEncrypt;
            Socket.ReceiveAsync();
        }

        private void OnEncrypt(BufferData data, ref int length)
        {
            GameCryptManager.Encrypt(data.Buffer, data.BaseOffset + data.Offset, ref length, data.RemainingLength, Data);
        }

        private void OnReceive(BufferData data)
        {
            var packet = new ClientKeyPacket();
            using var reader = data.GetReader();
            packet.Read(reader);

            DHKeyExchange.GenerateServerK(PrivateKey, packet.B, K);

            var key = new byte[64];
            K.WriteToBigEndian(key, 0, key.Length);

            GameCryptManager.Initialize(Data, key);

            Socket.Send(new ClientKeyOkPacket());

            Cleanup();

            Manager.ExchangeDone(this);
        }

        private void OnError(SocketAsyncEventArgs args)
        {
            Close();
        }

        /// <summary>The socket gave up on this connection; the reason is already logged.</summary>
        private void OnDrop(string reason)
        {
            Close();
        }

        private void Cleanup()
        {
            Socket.AutoReceive = true;
            Socket.OnReceive = null;
            Socket.OnError = null;
            Socket.OnDrop = null;
            Socket.OnEncrypt = null;
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            Socket.Close();
            Manager.Disconnect(this);
        }
    }
}
