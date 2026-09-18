using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Memory;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Structures;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class LightningConsolidationTests
    {
        [TestMethod]
        public void LightningArcSelectionIsBoundedDistinctAndDeterministic()
        {
            using var world = new WorldTestContext();
            var client = world.CreateClient();
            CellManager.Instance.AddToWorld(client);
            var primary = AddTarget(world, new Vector3(10, 0, 0));
            var farther = AddTarget(world, new Vector3(16, 0, 0));
            var tiedA = AddTarget(world, new Vector3(13, 4, 0));
            var tiedB = AddTarget(world, new Vector3(13, -4, 0));
            var expected = tiedA.EntityId < tiedB.EntityId ? tiedA : tiedB;

            foreach (var cell in world.Map.MapCellInfo.Cells.Values)
            {
                cell.CreatureList.Reverse();
                if (cell.CreatureList.Contains(expected))
                    cell.CreatureList.Add(expected);
            }

            var selected = AbilityManager.SelectLightningArcTargets(
                world.Map, client.Player, primary, 5, 1);

            Assert.AreEqual(1, selected.Count);
            Assert.AreSame(expected, selected.Single());
            Assert.IsFalse(selected.Contains(primary));
            Assert.IsFalse(selected.Contains(farther));
            Cleanup(world, primary, farther, tiedA, tiedB);
        }

        [TestMethod]
        public void LightningArcSelectionRejectsInvalidTargetsAndNonfiniteRange()
        {
            using var world = new WorldTestContext();
            var client = world.CreateClient();
            CellManager.Instance.AddToWorld(client);
            var primary = AddTarget(world, new Vector3(10, 0, 0));
            var friendly = AddTarget(world, new Vector3(11, 0, 0));
            friendly.Faction = Factions.AFS;
            var dead = AddTarget(world, new Vector3(12, 0, 0));
            dead.State = CharacterState.Dead;
            var otherMap = AddTarget(world, new Vector3(13, 0, 0));
            otherMap.MapContextId++;
            var missingHealth = AddTarget(world, new Vector3(14, 0, 0));
            missingHealth.Attributes.Remove(Attributes.Health);

            Assert.AreEqual(0, AbilityManager.SelectLightningArcTargets(
                world.Map, client.Player, primary, float.NaN, 1).Count);
            Assert.AreEqual(0, AbilityManager.SelectLightningArcTargets(
                world.Map, client.Player, primary, 12, 4).Count);
            Cleanup(world, primary, friendly, dead, otherMap, missingHealth);
        }

        [TestMethod]
        public void AbilityRecoveryEncodesArcHitsUnderTheirPrimaryAndSnapshotsThem()
        {
            var packet = new AbilityRecoveryPacket(
                ActionId.AaRecruitLightning, 2, AbilityRecoveryPacket.HitDataKind.Damage)
            {
                ArcData = true
            };
            var primary = new AbilityHit
            {
                EntityId = 0x100000007,
                Amount = 240,
                DamageType = DamageType.Electrical
            };
            primary.Arcs.Add(new AbilityHit
            {
                EntityId = 0x200000009,
                Amount = 210,
                DamageType = DamageType.Electrical
            });
            packet.Hits.Add(primary);
            var expected = Encode(packet);

            primary.Amount = 9999;
            primary.Arcs.Single().Amount = 9999;
            primary.Arcs.Clear();

            CollectionAssert.AreEqual(expected, Encode(packet));
            using var stream = new MemoryStream(expected);
            using var reader = new PythonReader(new BinaryReader(stream));
            Assert.AreEqual(6, reader.ReadTuple());
            Assert.AreEqual((uint)ActionId.AaRecruitLightning, reader.ReadUInt());
            Assert.AreEqual(2u, reader.ReadUInt());
            Assert.AreEqual(1, reader.ReadList());
            Assert.AreEqual(0x100000007UL, reader.ReadULong());
            Assert.AreEqual(0, reader.ReadList());
            Assert.AreEqual(0, reader.ReadList());
            Assert.AreEqual(1, reader.ReadList());
            Assert.AreEqual(2, reader.ReadTuple());
            ReadRaw(reader, 240);
            Assert.AreEqual(1, reader.ReadTuple());
            Assert.AreEqual(1, reader.ReadList());
            Assert.AreEqual(2, reader.ReadTuple());
            Assert.AreEqual(0x200000009UL, reader.ReadULong());
            ReadRaw(reader, 210);
            Assert.AreEqual(stream.Length, stream.Position);
        }

        [TestMethod]
        public void ActionTableArcPropertiesDriveRadiusAndDamage()
        {
            var level = new ActionLevelInfo();
            level.Properties[AbilityProperty.ArcRadius] = 12;
            level.Properties[AbilityProperty.ArcDamage] = 210;
            level.Properties[AbilityProperty.DamageScaleType] = 0;

            var spec = AbilityManager.GetLightningArcSpec(level, 15);

            Assert.AreEqual(12f, spec.Radius);
            Assert.AreEqual(210, spec.Damage);
            Assert.AreEqual(1, spec.MaximumTargets);
        }

        private static Creature AddTarget(WorldTestContext world, Vector3 position)
        {
            var target = new Creature
            {
                EntityClass = EntityClasses.HumanBaseMale,
                MapContextId = world.Map.MapInfo.MapContextId,
                Position = position,
                State = CharacterState.Normal,
                Faction = Factions.Bane,
                AppearanceData = new(),
                Attributes = new Dictionary<Attributes, ActorAttributes>
                {
                    [Attributes.Health] = new(Attributes.Health, 100, 100, 100, 0, 0),
                    [Attributes.Armor] = new(Attributes.Armor, 0, 0, 0, 0, 0)
                }
            };
            CellManager.Instance.AddToWorld(world.Map, target);
            return target;
        }

        private static void Cleanup(WorldTestContext world, params Creature[] targets)
        {
            foreach (var target in targets)
                CellManager.Instance.RemoveCreatureFromWorld(world.Map, target);
        }

        private static byte[] Encode(AbilityRecoveryPacket packet)
        {
            using var stream = new MemoryStream();
            using var writer = new PythonWriter(new BinaryWriter(stream));
            packet.Write(writer);
            return stream.ToArray();
        }

        private static void ReadRaw(PythonReader reader, long amount)
        {
            Assert.AreEqual(12, reader.ReadTuple());
            Assert.AreEqual((uint)DamageType.Electrical, reader.ReadUInt());
            Assert.AreEqual(0u, reader.ReadUInt());
            Assert.AreEqual(0u, reader.ReadUInt());
            Assert.AreEqual(0u, reader.ReadUInt());
            Assert.AreEqual(0u, reader.ReadUInt());
            Assert.AreEqual(amount, reader.ReadLong());
            Assert.AreEqual(0, reader.ReadInt());
            reader.ReadInt();
            Assert.AreEqual(0u, reader.ReadUInt());
            Assert.AreEqual(0, reader.ReadInt());
            Assert.AreEqual(0, reader.ReadList());
            Assert.AreEqual(0, reader.ReadList());
        }
    }
}
