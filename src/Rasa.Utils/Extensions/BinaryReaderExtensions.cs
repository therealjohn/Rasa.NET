using System;
using System.IO;
using System.Text;

namespace Rasa.Extensions
{
    public static class BinaryReaderExtensions
    {
        public static string ReadLengthedString(this BinaryReader reader)
        {
            var len = reader.ReadInt32();
            return Encoding.UTF8.GetString(reader.ReadBytesExactly(len));
        }

        public static byte[] ReadBytesExactly(this BinaryReader reader, int length)
        {
            if (length < 0)
                throw new InvalidDataException("Payload length cannot be negative.");
            if (reader.BaseStream.CanSeek && length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new EndOfStreamException("Payload length exceeds the available data.");

            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException("Incomplete payload.");
            return bytes;
        }

        public static string ReadUtf8StringOn(this BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);

            var index = Array.IndexOf<byte>(bytes, 0);
            if (index == -1)
                index = bytes.Length;

            return index == 0 ? "" : Encoding.UTF8.GetString(bytes, 0, index);
        }
    }
}
