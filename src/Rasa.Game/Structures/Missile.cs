namespace Rasa.Structures
{
    using Data;

    internal sealed record LightningArc(Creature Target, int Damage)
    {
        internal ulong EntityId { get; } = Target.EntityId;
        internal long TargetLifetime { get; } = Target.ActionLifetime;
    }

    public class Missile
    {
        private int _triggered;
        internal bool TryTrigger() => System.Threading.Interlocked.CompareExchange(ref _triggered, 1, 0) == 0;
        internal LightningArc Arc { get; set; }
        public int DamageA { get; set; }
        public int DamageB { get; set; }
        public ActionId ActionId { get; set; }
        public uint ActionArgId { get; set; }
        public bool IsAbility { get; set; }         // set to true to use PerformAbility instead of Windup/Recovery
        public ulong TargetEntityId { get; set; }    // the entityId of the destination (it is possible that the object does no more exist on arrival)
        public Actor TargetActor { get; set; }
        public Actor Source { get; set; }
        internal long SourceLifetime { get; set; }
        internal long TargetLifetime { get; set; }
        public long TriggerTime { get; set; }       // amount of milliseconds left before the missile is triggered, is decreased on every tick
        public MissileArgs Args = new MissileArgs();
    }
}
