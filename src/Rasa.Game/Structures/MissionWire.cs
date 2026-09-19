namespace Rasa.Structures
{
    using Memory;

    internal static class MissionWire
    {
        internal static void WriteBool(PythonWriter writer, bool value)
        {
            if (value)
                writer.WriteTrueStruct();
            else
                writer.WriteNoneStruct();
        }
    }
}
