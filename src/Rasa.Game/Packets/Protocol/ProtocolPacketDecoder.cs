using System;
using System.IO;
using System.Text;

namespace Rasa.Packets.Protocol
{
    using Data;
    using Memory;

    internal sealed class ProtocolPacketDecoder : IDisposable
    {
        private readonly NonContiguousMemoryStream _input = new();
        private readonly object _sync = new();
        private bool _disposed;
        private readonly bool[] _receivedSequence = new bool[256];

        internal uint[] ReceiveSequence { get; } = new uint[256];

        internal bool Append(byte[] data, int offset, int count)
        {
            lock (_sync)
            {
                if (_disposed)
                    return false;

                if (count != 0)
                    _input.CopyFromArray(data, offset, count);
                return true;
            }
        }

        internal ProtocolPacket ReadNext()
        {
            lock (_sync)
            {
                while (!_disposed && _input.Length >= 2)
                {
                    using var reader = new BinaryReader(_input, Encoding.UTF8, true);
                    var size = reader.ReadUInt16();
                    _input.Position = 0;
                    if (size < ProtocolPacket.HeaderSize)
                        throw new InvalidDataException($"Protocol size {size} is smaller than the header.");
                    if (size > _input.Length)
                        return null;

                    var packet = new ProtocolPacket();
                    packet.Read(reader);
                    _input.RemoveBytes(size);
                    if (packet.Type == ClientMessageOpcode.None)
                        continue;

                    if (packet.Channel != 0)
                    {
                        if (_receivedSequence[packet.Channel] &&
                            unchecked((int)(packet.SequenceNumber - ReceiveSequence[packet.Channel])) <= 0)
                            continue;

                        _receivedSequence[packet.Channel] = true;
                        ReceiveSequence[packet.Channel] = packet.SequenceNumber;
                    }

                    return packet;
                }

                return null;
            }
        }

        internal bool ProcessPending(Action<ProtocolPacket> handlePacket, Action<Exception> rejectPacket)
        {
            try
            {
                ProtocolPacket packet;
                while ((packet = ReadNext()) != null)
                    handlePacket(packet);
                return true;
            }
            catch (Exception error) when (error is InvalidClientMessageException ||
                                          error is InvalidDataException ||
                                          error is EndOfStreamException)
            {
                Dispose();
                rejectPacket(error);
                return false;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _input.Dispose();
            }
        }
    }
}
