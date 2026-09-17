namespace Rasa.Packets.MapChannel.Server
{
    using Data;
    using Memory;
    using Structures;

    public class UpdatePowerPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.UpdatePower;

        private readonly ActorAttributes _power;
        public ActorAttributes Power => _power.Snapshot();
        public int WhoId { get; }

        public UpdatePowerPacket(ActorAttributes power, int whoId)
        {
            _power = power.Snapshot();
            WhoId = whoId;
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(4);
            pw.WriteInt(_power.Current);
            pw.WriteInt(_power.CurrentMax);
            pw.WriteInt(_power.RefreshAmount);
            pw.WriteInt(WhoId);
        }
    }
}
