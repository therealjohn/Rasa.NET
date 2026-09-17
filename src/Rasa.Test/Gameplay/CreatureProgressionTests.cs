extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Structures;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class CreatureProgressionTests
    {
        [TestMethod]
        public void DeathWithoutAKillerIsSafeAndChangesSpawnCountsOnlyOnce()
        {
            using var context = new ProgressionTestContext();
            var observer = context.CreateClient();
            CellManager.Instance.AddToWorld(observer);
            var creature = AddCreature(context);
            WorldTestContext.Drain(observer);
            var manager = new CreatureManager(null);
            try
            {
                manager.HandleCreatureKill(context.World.Map, creature, null);
                manager.HandleCreatureKill(context.World.Map, creature, null);

                Assert.AreEqual(CharacterState.Dead, creature.State);
                Assert.AreEqual(0, creature.SpawnPool.AliveCreatures);
                Assert.AreEqual(1, creature.SpawnPool.DeadCreatures);
                Assert.AreEqual(0L, creature.SpawnPool.UpdateTimer);
                Assert.AreEqual(0U, observer.Player.Experience);
                Assert.AreEqual(0, context.World.Map.LootDispensers.Count);
                Assert.AreEqual(1, WorldTestContext.Drain(observer).Select(packet => packet.Message)
                    .OfType<CallMethodMessage>().Count(packet => packet.Packet is StateChangePacket));
                Assert.IsTrue(EntityManager.Instance.Creatures.ContainsKey(creature.EntityId));
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.World.Map, creature);
            }
        }

        [TestMethod]
        public void FinalKillerReceivesOnePersistedRewardDespiteDuplicateAndMissingCells()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var progression = new ManifestationManager(database);
            var manager = new CreatureManager(null, progression);
            var killer = context.CreateClient();
            var observer = context.CreateClient();
            killer.Player.Experience = 2990;
            CellManager.Instance.AddToWorld(killer);
            CellManager.Instance.AddToWorld(observer);
            var creature = AddCreature(context);
            database.Seed(killer);
            WorldTestContext.Drain(killer);
            WorldTestContext.Drain(observer);
            var cells = context.World.Map.MapCellInfo.Cells;
            cells[killer.Player.Cells[1, 1]].ClientList.Add(killer);
            cells[killer.Player.Cells[2, 2]].ClientList.Insert(0, null);
            killer.Player.Cells[0, 0] = uint.MaxValue;
            try
            {
                manager.HandleCreatureKill(context.World.Map, creature, killer.Player);
                manager.HandleCreatureKill(context.World.Map, creature, observer.Player);
                manager.HandleCreatureKill(context.World.Map, creature, killer.Player);

                Assert.AreEqual(CharacterState.Dead, creature.State);
                Assert.AreEqual(0, creature.SpawnPool.AliveCreatures);
                Assert.AreEqual(1, creature.SpawnPool.DeadCreatures);
                Assert.AreEqual(1, database.SaveAttempts);
                Assert.AreEqual((byte)2, killer.Player.Level);
                Assert.IsTrue(killer.Player.Experience >= 3080 && killer.Player.Experience <= 3100);
                var saved = database.Read(killer.Player.Id);
                Assert.AreEqual(killer.Player.Experience, saved.Experience);
                Assert.AreEqual((byte)2, saved.Level);
                Assert.AreEqual(0U, observer.Player.Experience);
                var ownerPackets = WorldTestContext.Drain(killer).Select(packet => packet.Message)
                    .OfType<CallMethodMessage>().Select(packet => packet.Packet).ToList();
                Assert.AreEqual(1, ownerPackets.OfType<StateChangePacket>().Count());
                Assert.AreEqual(1, ownerPackets.OfType<ExperienceChangedPacket>().Count());
                Assert.AreEqual(killer.Player.Experience, ownerPackets.OfType<ExperienceChangedPacket>().Single().XPInfo.Total);
                var observerPackets = WorldTestContext.Drain(observer).Select(packet => packet.Message)
                    .OfType<CallMethodMessage>().Select(packet => packet.Packet).ToList();
                Assert.AreEqual(1, observerPackets.OfType<StateChangePacket>().Count());
                Assert.AreEqual(0, observerPackets.OfType<ExperienceChangedPacket>().Count());
                Assert.AreEqual(2U, observerPackets.OfType<LevelPacket>().Single().Level);
                var loot = context.World.Map.LootDispensers.Values.Single();
                Assert.AreEqual(killer.Player.EntityId, loot.Owner);
                Assert.AreEqual(creature.EntityId, loot.AttachedTo);

                BehaviorManager.Instance.MapChannelThink(context.World.Map, 20000);

                Assert.AreEqual(0, creature.SpawnPool.DeadCreatures);
                Assert.AreEqual(0, creature.SpawnPool.AliveCreatures);
                Assert.IsFalse(EntityManager.Instance.Creatures.ContainsKey(creature.EntityId));
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.World.Map, creature);
            }
        }

        [TestMethod]
        [DataRow("OtherLifetime")]
        [DataRow("OtherContext")]
        [DataRow("Disconnected")]
        [DataRow("Removed")]
        public void StaleKillerMembershipCannotReceiveExperienceOrLoot(string state)
        {
            using var context = new ProgressionTestContext();
            using var otherWorld = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new CreatureManager(null, new ManifestationManager(database));
            var killer = context.CreateClient();
            CellManager.Instance.AddToWorld(killer);
            var creature = AddCreature(context);
            database.Seed(killer);
            WorldTestContext.Drain(killer);
            switch (state)
            {
                case "OtherLifetime":
                    killer.Player.MapChannel = otherWorld.World.Map;
                    otherWorld.World.Map.ClientList.Add(killer);
                    CellManager.Instance.AddToWorld(killer);
                    WorldTestContext.Drain(killer);
                    break;
                case "OtherContext":
                    killer.Player.MapContextId = 1148;
                    break;
                case "Disconnected":
                    killer.State = ClientState.Disconnected;
                    break;
                case "Removed":
                    killer.Player.RemoveFromMap = true;
                    break;
            }
            try
            {
                manager.HandleCreatureKill(context.World.Map, creature, killer.Player);

                Assert.AreEqual(CharacterState.Dead, creature.State);
                Assert.AreEqual(0U, killer.Player.Experience);
                Assert.AreEqual(0, database.OpenAttempts);
                Assert.AreEqual(0, context.World.Map.LootDispensers.Count);
                Assert.AreEqual(0, otherWorld.World.Map.LootDispensers.Count);
                Assert.AreEqual(0U, database.Read(killer.Player.Id).Experience);
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.World.Map, creature);
            }
        }

        [TestMethod]
        public void ACreatureFromAnotherMapLifetimeCannotBeKilledOrRewarded()
        {
            using var context = new ProgressionTestContext();
            using var oldWorld = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new CreatureManager(null, new ManifestationManager(database));
            var killer = context.CreateClient();
            CellManager.Instance.AddToWorld(killer);
            var creature = AddCreature(oldWorld);
            database.Seed(killer);
            WorldTestContext.Drain(killer);
            try
            {
                manager.HandleCreatureKill(context.World.Map, creature, killer.Player);

                Assert.AreEqual(CharacterState.Idle, creature.State);
                Assert.AreEqual(1, creature.SpawnPool.AliveCreatures);
                Assert.AreEqual(0, creature.SpawnPool.DeadCreatures);
                Assert.AreEqual(0, database.OpenAttempts);
                Assert.AreEqual(0U, killer.Player.Experience);
                Assert.AreEqual(0, context.World.Map.LootDispensers.Count);
                Assert.AreEqual(0, WorldTestContext.Drain(killer).Count);
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(oldWorld.World.Map, creature);
            }
        }

        [TestMethod]
        public void SimultaneousDeathNotificationsAwardOnlyOnce()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new CreatureManager(null, new ManifestationManager(database));
            var killer = context.CreateClient();
            CellManager.Instance.AddToWorld(killer);
            var creature = AddCreature(context);
            database.Seed(killer);
            WorldTestContext.Drain(killer);
            try
            {
                Parallel.For(0, 32, _ => manager.HandleCreatureKill(context.World.Map, creature, killer.Player));

                Assert.AreEqual(1, database.SaveAttempts);
                Assert.AreEqual(0, creature.SpawnPool.AliveCreatures);
                Assert.AreEqual(1, creature.SpawnPool.DeadCreatures);
                Assert.AreEqual(1, context.World.Map.LootDispensers.Count);
                Assert.AreEqual(1, WorldTestContext.Drain(killer).Select(packet => packet.Message)
                    .OfType<CallMethodMessage>().Count(packet => packet.Packet is ExperienceChangedPacket));
                Assert.AreEqual(killer.Player.Experience, database.Read(killer.Player.Id).Experience);
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.World.Map, creature);
            }
        }

        [TestMethod]
        public void FailedRewardSaveDoesNotRepeatDeathOrSuppressTheExistingLootCall()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new CreatureManager(null, new ManifestationManager(database));
            var killer = context.CreateClient();
            CellManager.Instance.AddToWorld(killer);
            var creature = AddCreature(context);
            database.Seed(killer);
            WorldTestContext.Drain(killer);
            database.BeforeSave = _ => throw new DbUpdateException("Fixture kill reward save failure.");
            try
            {
                manager.HandleCreatureKill(context.World.Map, creature, killer.Player);
                manager.HandleCreatureKill(context.World.Map, creature, killer.Player);

                Assert.AreEqual(CharacterState.Dead, creature.State);
                Assert.AreEqual(1, database.SaveAttempts);
                Assert.AreEqual(0, creature.SpawnPool.AliveCreatures);
                Assert.AreEqual(1, creature.SpawnPool.DeadCreatures);
                Assert.AreEqual(1, context.World.Map.LootDispensers.Count);
                Assert.AreEqual(0U, killer.Player.Experience);
                Assert.AreEqual(0U, database.Read(killer.Player.Id).Experience);
                Assert.AreEqual(0, WorldTestContext.Drain(killer).Select(packet => packet.Message)
                    .OfType<CallMethodMessage>().Count(packet => packet.Packet is ExperienceChangedPacket));
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.World.Map, creature);
            }
        }

        private static Creature AddCreature(ProgressionTestContext context)
        {
            var creature = new Creature
            {
                EntityClass = EntityClasses.HumanBaseMale,
                MapContextId = context.World.Map.MapInfo.MapContextId,
                Level = 1,
                State = CharacterState.Idle,
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                Attributes = Enum.GetValues<Attributes>().ToDictionary(
                    id => id, id => new ActorAttributes(id, 100, 100, 0, 0, 0)),
                SpawnPool = new SpawnPool
                {
                    MapContextId = context.World.Map.MapInfo.MapContextId,
                    AliveCreatures = 1,
                    RespawnTime = 1000,
                    UpdateTimer = 1000
                }
            };
            CellManager.Instance.AddToWorld(context.World.Map, creature);
            return creature;
        }
    }
}
