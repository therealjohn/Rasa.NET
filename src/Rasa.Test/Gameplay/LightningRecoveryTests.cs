using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Memory;
    using Rasa.Packets.MapChannel.Server.PerformRecovery;
    using Rasa.Structures;

    [TestClass]
    public class LightningRecoveryTests
    {
        [TestMethod]
        public void EmptyArcsRetainTheRankOneLiteralWireEncoding()
        {
            var missile = CreateMissile(180);
            missile.ActionArgId = 1;
            var expected = Convert.FromHexString(
                "861EC20011712F0700000001000000707071828C1D0D10101010" +
                "2FB4000000000000001010101070708170");

            CollectionAssert.AreEqual(expected, AbilityTestContext.Encode(new LightningRecovery(missile)));
            Assert.AreEqual(0, Decode(expected).Args.HitData.Single().LightningArcs.Count);
        }

        [TestMethod]
        public void ContributorArcLayoutMatchesLiteralBytesWithWideIdentityAndNondefaultRawFields()
        {
            var missile = CreateMissile();
            missile.Args.HitData.Single().LightningArcs.Add(CreateArc());
            var expected = Convert.FromHexString(
                "861EC20012712F0700000001000000707071828C1D0D10101010" +
                "2F2C010000000000001010101070708171822F0900000002000000" +
                "8C171D111E2C011F701101001D0D2F080706050403020111121D13117070");

            CollectionAssert.AreEqual(expected, AbilityTestContext.Encode(new LightningRecovery(missile)));
            var decoded = Decode(expected);
            Assert.AreEqual(300L, decoded.Args.HitData.Single().FinalAmt);
            AssertRaw(CreateArc(), decoded.Args.HitData.Single().LightningArcs.Single());
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(3)]
        public void ArcListsBelongToTheirPrimaryAndRetainEachHitsOwnDamage(int count)
        {
            var missile = CreateMissile();
            for (var index = 0; index < count; index++)
            {
                var arc = CreateArc();
                arc.EntityId += (ulong)index;
                arc.DamageType = (DamageType)(index + 1);
                arc.FinalAmt += index;
                missile.Args.HitData[0].LightningArcs.Add(arc);
            }
            missile.Args.HitEntities.Add(99);
            missile.Args.HitData.Add(new HitData { EntityId = 99 });
            var decoded = Decode(AbilityTestContext.Encode(new LightningRecovery(missile)));

            Assert.AreEqual(count, decoded.Args.HitData[0].LightningArcs.Count);
            Assert.AreEqual(0, decoded.Args.HitData[1].LightningArcs.Count);
            for (var index = 0; index < count; index++)
                AssertRaw(missile.Args.HitData[0].LightningArcs[index], decoded.Args.HitData[0].LightningArcs[index]);
            Assert.AreEqual(DamageType.Electrical, decoded.Args.HitData[0].DamageType);
            Assert.AreEqual(300L, decoded.Args.HitData[0].FinalAmt);
        }

        [TestMethod]
        public void QueuedRecoverySnapshotsEveryFieldAndCollectionBeforeItsFirstWrite()
        {
            var missile = CreateMissile();
            var primary = missile.Args.HitData.Single();
            var arc = CreateArc();
            primary.LightningArcs.Add(arc);
            var expected = AbilityTestContext.Encode(new LightningRecovery(missile));
            var packet = new LightningRecovery(missile);

            missile.ActionId = ActionId.WeaponAttack;
            missile.ActionArgId = 5;
            missile.DamageA = 9999;
            primary.EntityId = 1;
            primary.DamageType = DamageType.Fire;
            primary.Reflected = primary.Filtered = primary.Absorbed = primary.Resisted = 88;
            primary.FinalAmt = 9999;
            primary.IsCritical = primary.DeathBlow = primary.WasImune = 3;
            primary.CoverModifier = 99;
            arc.EntityId = 1;
            arc.DamageType = DamageType.Fire;
            arc.Reflected = arc.Filtered = arc.Absorbed = arc.Resisted = 88;
            arc.FinalAmt = 9999;
            arc.IsCritical = arc.DeathBlow = arc.WasImune = 3;
            arc.CoverModifier = 99;
            arc.TargetEffectIds.Add(98);
            arc.SourceEffectIds.Clear();
            primary.LightningArcs.Clear();
            primary.LightningArcs.Add(new HitData());
            missile.Args.HitEntities.Clear();
            missile.Args.HitData.Clear();
            missile.Args = new MissileArgs();

            CollectionAssert.AreEqual(expected, AbilityTestContext.Encode(packet));
            CollectionAssert.AreEqual(expected, AbilityTestContext.Encode(packet));
        }

        private static Missile CreateMissile(int damage = 300)
        {
            var missile = new Missile
            {
                ActionId = ActionId.AaRecruitLightning, ActionArgId = 2, DamageA = damage
            };
            missile.Args.HitEntities.Add(0x100000007UL);
            missile.Args.HitData.Add(new HitData { EntityId = 0x100000007UL, FinalAmt = damage });
            return missile;
        }

        private static HitData CreateArc() => new()
        {
            EntityId = 0x200000009UL, DamageType = DamageType.Sonic,
            Reflected = 17, Filtered = 300, Absorbed = 70000, Resisted = 13,
            FinalAmt = 0x0102030405060708L, IsCritical = 1, DeathBlow = 2,
            CoverModifier = 19, WasImune = 1,
            TargetEffectIds = new List<uint> { 77 }, SourceEffectIds = new List<uint> { 78 }
        };

        private static void AssertRaw(HitData expected, HitData actual)
        {
            Assert.AreEqual(expected.EntityId, actual.EntityId);
            Assert.AreEqual(expected.DamageType, actual.DamageType);
            Assert.AreEqual(expected.Reflected, actual.Reflected);
            Assert.AreEqual(expected.Filtered, actual.Filtered);
            Assert.AreEqual(expected.Absorbed, actual.Absorbed);
            Assert.AreEqual(expected.Resisted, actual.Resisted);
            Assert.AreEqual(expected.FinalAmt, actual.FinalAmt);
            Assert.AreEqual(expected.IsCritical, actual.IsCritical);
            Assert.AreEqual(expected.DeathBlow, actual.DeathBlow);
            Assert.AreEqual(expected.CoverModifier, actual.CoverModifier);
            Assert.AreEqual(expected.WasImune, actual.WasImune);
        }

        internal static Missile Decode(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new PythonReader(new BinaryReader(stream));
            Assert.AreEqual(6, reader.ReadTuple());
            var missile = new Missile { ActionId = (ActionId)reader.ReadUInt(), ActionArgId = reader.ReadUInt() };
            var count = reader.ReadList();
            for (var index = 0; index < count; index++)
                missile.Args.HitEntities.Add(reader.ReadULong());
            Assert.AreEqual(0, reader.ReadList());
            Assert.AreEqual(0, reader.ReadList());
            Assert.AreEqual(count, reader.ReadList());
            for (var index = 0; index < count; index++)
            {
                Assert.AreEqual(2, reader.ReadTuple());
                var primary = ReadRaw(reader, missile.Args.HitEntities[index]);
                missile.Args.HitData.Add(primary);
                Assert.AreEqual(1, reader.ReadTuple());
                var arcs = reader.ReadList();
                for (var arc = 0; arc < arcs; arc++)
                {
                    Assert.AreEqual(2, reader.ReadTuple());
                    primary.LightningArcs.Add(ReadRaw(reader, reader.ReadULong()));
                }
            }
            Assert.AreEqual(stream.Length, stream.Position, "No extra fields or effects may follow the supplied layout.");
            return missile;
        }

        private static HitData ReadRaw(PythonReader reader, ulong id)
        {
            Assert.AreEqual(12, reader.ReadTuple());
            var hit = new HitData
            {
                EntityId = id, DamageType = (DamageType)reader.ReadUInt(),
                Reflected = reader.ReadUInt(), Filtered = reader.ReadUInt(),
                Absorbed = reader.ReadUInt(), Resisted = reader.ReadUInt(),
                FinalAmt = reader.ReadLong(), IsCritical = reader.ReadInt(), DeathBlow = reader.ReadInt(),
                CoverModifier = reader.ReadUInt(), WasImune = reader.ReadInt()
            };
            Assert.AreEqual(0, reader.ReadList(), "Target effects remain empty.");
            Assert.AreEqual(0, reader.ReadList(), "Source effects remain empty.");
            return hit;
        }
    }
}
