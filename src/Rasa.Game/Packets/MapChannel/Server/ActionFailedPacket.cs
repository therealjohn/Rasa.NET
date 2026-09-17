namespace Rasa.Packets.MapChannel.Server
{
    using Data;
    using Memory;

    public sealed class ActionFailedPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode => GameOpcode.ActionFailed;
        public ActionId ActionId { get; }
        public int ActionArgId { get; }

        public ActionFailedPacket(ActionId actionId, int actionArgId)
        {
            ActionId = actionId;
            ActionArgId = actionArgId;
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(2);
            pw.WriteInt((int)ActionId);
            pw.WriteInt(ActionArgId);
        }
    }
}
