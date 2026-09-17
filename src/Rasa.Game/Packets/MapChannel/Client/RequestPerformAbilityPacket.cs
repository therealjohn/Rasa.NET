namespace Rasa.Packets.MapChannel.Client
{
    using Data;
    using Memory;

    public class RequestPerformAbilityPacket : ClientPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.RequestPerformAbility;

        public ActionId ActionId { get; set; }
        public int ActionArgId { get; set; }
        public ulong Target { get; set; }
        public int ItemId { get; set; }

        public override void Read(PythonReader pr)
        {
            if (pr.ReadTuple() != 4)
                throw new System.IO.InvalidDataException("Ability requests require action, rank, target and item fields.");
            ActionId = (ActionId)pr.ReadInt();
            ActionArgId = pr.ReadInt();
            Target = unchecked((ulong)pr.ReadLong());
            if (pr.PeekType() == PythonType.Int)
                ItemId = pr.ReadInt();
            else
            {
                pr.ReadNoneStruct();
                ItemId = 0;
            }
        }
    }
}
