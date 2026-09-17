namespace Rasa.Packets.MapChannel.Server
{
    using Data;
    using Memory;
    using Structures;

    public class UpdateChiPacket : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.UpdateChi;

        private readonly ActorAttributes _chi;
        public ActorAttributes Chi => _chi.Snapshot();
        public int WhoId { get; }

        public UpdateChiPacket(ActorAttributes chi, int whoId)
        {
            _chi = chi.Snapshot();
            WhoId = whoId;
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(4);
            pw.WriteInt(_chi.Current);
            pw.WriteInt(_chi.CurrentMax);
            pw.WriteInt(_chi.RefreshAmount);
            pw.WriteInt(WhoId);
        }
    }
}