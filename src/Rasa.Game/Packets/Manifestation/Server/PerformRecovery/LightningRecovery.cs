using System.Linq;

namespace Rasa.Packets.MapChannel.Server.PerformRecovery
{
    using Data;
    using Memory;
    using Structures;

    public class LightningRecovery : ServerPythonPacket
    {
        public override GameOpcode Opcode { get; } = GameOpcode.PerformRecovery;
        private readonly ActionId _actionId;
        private readonly uint _actionArgId;
        private readonly ulong[] _hitEntities;
        private readonly PrimaryHit[] _hits;

        private sealed record PrimaryHit(RawHit Raw, RawHit[] Arcs);

        private sealed record RawHit(ulong EntityId, DamageType DamageType, uint Reflected,
            uint Filtered, uint Absorbed, uint Resisted, long FinalAmt, int IsCritical,
            int DeathBlow, uint CoverModifier, int WasImune)
        {
            internal static RawHit Capture(HitData hit) => new(hit.EntityId, hit.DamageType,
                hit.Reflected, hit.Filtered, hit.Absorbed, hit.Resisted, hit.FinalAmt,
                hit.IsCritical, hit.DeathBlow, hit.CoverModifier, hit.WasImune);

            internal void Write(PythonWriter pw)
            {
                pw.WriteTuple(12);
                pw.WriteUInt((uint)DamageType);
                pw.WriteUInt(Reflected);
                pw.WriteUInt(Filtered);
                pw.WriteUInt(Absorbed);
                pw.WriteUInt(Resisted);
                pw.WriteLong(FinalAmt);
                pw.WriteInt(IsCritical);
                pw.WriteInt(DeathBlow);
                pw.WriteUInt(CoverModifier);
                pw.WriteInt(WasImune);
                pw.WriteList(0);
                pw.WriteList(0);
            }
        }

        public LightningRecovery(Missile missile)
        {
            _actionId = missile.ActionId;
            _actionArgId = missile.ActionArgId;
            _hitEntities = missile.Args.HitEntities.ToArray();
            _hits = missile.Args.HitData.Select(hit => new PrimaryHit(
                RawHit.Capture(hit) with { DamageType = DamageType.Electrical, FinalAmt = missile.DamageA },
                hit.LightningArcs.Select(RawHit.Capture).ToArray())).ToArray();
        }

        public override void Write(PythonWriter pw)
        {
            pw.WriteTuple(6);
            pw.WriteUInt((uint)_actionId);
            pw.WriteUInt(_actionArgId);
            pw.WriteList(_hitEntities.Length);
            foreach (var entity in _hitEntities)
                pw.WriteULong(entity);
            pw.WriteList(0);                    // misses
            pw.WriteList(0);                    // misses data
            pw.WriteList(_hits.Length);
            foreach (var hit in _hits)
            {
                pw.WriteTuple(2);
                hit.Raw.Write(pw);
                pw.WriteTuple(1);               // OnHitData - ArcData
                pw.WriteList(hit.Arcs.Length);
                foreach (var arc in hit.Arcs)
                {
                    pw.WriteTuple(2);
                    pw.WriteULong(arc.EntityId);
                    arc.Write(pw);
                }
            }
        }
    }
}
