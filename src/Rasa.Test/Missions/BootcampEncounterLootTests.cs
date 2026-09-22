using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Structures;

    [TestClass]
    [DoNotParallelize]
    public class BootcampEncounterLootTests
    {
        [TestMethod]
        [DataRow(0, 5)]
        [DataRow(14, 5)]
        [DataRow(15, 4)]
        [DataRow(24, 4)]
        [DataRow(25, 3)]
        [DataRow(29, 3)]
        [DataRow(30, 2)]
        [DataRow(54, 2)]
        [DataRow(55, 1)]
        [DataRow(99, 1)]
        public void DropProfileHasExactIndependentChanceBoundaries(int percentile, int count)
        {
            var drops = BootcampThraxLoot.Roll((minimum, maximum) => maximum == 100 ? percentile : minimum).ToArray();
            Assert.AreEqual(count, drops.Length);
            Assert.AreEqual((41666U, 1U), drops.Single(drop => drop.TemplateId == 41666));
            Assert.IsTrue(drops.All(drop => drop.Quantity > 0));
            if (percentile < 55)
                Assert.AreEqual(12U, drops.Single(drop => drop.TemplateId == 28).Quantity);
            if (percentile < 30)
                Assert.AreEqual(8U, drops.Single(drop => drop.TemplateId == 56).Quantity);
        }

        [TestMethod]
        public void OptionalAmmunitionCanReachItsInclusiveQuantityCeiling()
        {
            var drops = BootcampThraxLoot.Roll((minimum, maximum) => maximum == 100 ? 0 : maximum - 1).ToArray();
            Assert.AreEqual(24U, drops.Single(drop => drop.TemplateId == 28).Quantity);
            Assert.AreEqual(16U, drops.Single(drop => drop.TemplateId == 56).Quantity);
            Assert.AreEqual(1U, drops.Single(drop => drop.TemplateId == 41666).Quantity);
        }

        [TestMethod]
        public void BothProvidersAuthorTheSameEncounterContentAndRollback()
        {
            var sqlite = new Rasa.Migrations.SqliteWorld.BootcampCombat();
            var mysql = new Rasa.Migrations.MySqlWorld.BootcampCombat();
            CollectionAssert.AreEqual(
                sqlite.UpOperations.Cast<SqlOperation>().Select(operation => operation.Sql).ToArray(),
                mysql.UpOperations.Cast<SqlOperation>().Select(operation => operation.Sql).ToArray());
            CollectionAssert.AreEqual(
                sqlite.DownOperations.Cast<SqlOperation>().Select(operation => operation.Sql).ToArray(),
                mysql.DownOperations.Cast<SqlOperation>().Select(operation => operation.Sql).ToArray());
        }

        [TestMethod]
        public void RouteHasTwelveGroundedPacksOfThreeOrFourLevelEightToThirteenThrax()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            var pools = harness.WorldContext.SpawnPoolEntries.AsNoTracking()
                .Where(pool => pool.MapContextId == 1985 && pool.Id >= 510230 && pool.Id <= 510271)
                .OrderBy(pool => pool.Id).ToArray();
            Assert.AreEqual(42, pools.Length, "The cave, base and crash-site route needs twelve authored Thrax packs.");
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 0);
            var creatures = harness.BootcampMap.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList).Distinct()
                .Where(creature => creature.SpawnPool?.DbId is >= 510230 and <= 510271).ToArray();
            Assert.AreEqual(42, creatures.Length);
            CollectionAssert.AreEquivalent(new uint[] { 8, 9, 10, 11, 12, 13 },
                creatures.Select(creature => creature.Level).Distinct().ToArray());
            var start = new Vector3(279.05f, 121.02865f, 66.07f);
            foreach (var creature in creatures)
            {
                Assert.AreEqual(Factions.Bane, creature.Faction);
                Assert.IsTrue(creature.Level >= 8 && creature.Level <= 13);
                var ground = harness.BootcampMap.NavMesh.Nearest(creature.Position);
                Assert.IsTrue(ground.HasValue);
                Assert.IsTrue(Vector3.Distance(ground.Value, creature.Position) < 0.5f);
                Assert.IsTrue(creature.Actions.Count > 0);
                harness.BootcampMap.NavMesh.FindPath(start, creature.Position, out var complete);
                Assert.IsTrue(complete, $"Spawn {creature.SpawnPool.DbId} is not on the playable route.");
            }
            foreach (var (first, count) in new[]
                     {
                         (510230U, 3), (510233U, 4), (510237U, 3), (510240U, 4),
                         (510244U, 3), (510247U, 4), (510251U, 3), (510254U, 4),
                         (510258U, 3), (510261U, 4), (510265U, 3), (510268U, 4)
                     })
            {
                var pack = creatures.Where(creature =>
                    creature.SpawnPool.DbId >= first && creature.SpawnPool.DbId < first + count).ToArray();
                Assert.AreEqual(count, pack.Length);
                Assert.IsTrue(pack.All(creature => Vector3.Distance(creature.Position, pack[0].Position) < 6));
            }
        }

        [TestMethod]
        public void PlayerKillCreatesClaimableGuaranteedSkullWithoutDuplicatingTheClaim()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 0);
            var thrax = harness.BootcampMap.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList).First(creature => creature.DbId == 510216);
            harness.MovePlayerTo(thrax);
            CellManager.Instance.UpdateVisibility(harness.Client);
            MissileManager.Instance.MissileLaunch(harness.BootcampMap,
                new ActionData(harness.Client.Player, ActionId.WeaponAttack, 1, thrax.EntityId, 0), 10000);
            MissileManager.Instance.DoWork(harness.BootcampMap, 1000);

            Assert.AreEqual(CharacterState.Dead, thrax.State);
            Assert.AreNotEqual(0UL, thrax.CorpseLootEntityId);
            var loot = harness.BootcampMap.LootDispensers[thrax.CorpseLootEntityId];
            var skull = loot.LootItems.SingleOrDefault(item => item.ItemTemplateId == 41666);
            Assert.IsNotNull(skull, "Every eligible Bootcamp Thrax kill must drop its skull.");
            Assert.AreEqual(1U, skull.ItemQuantity);

            var request = new RequestLootAllFromCorpsePacket { EntityId = loot.EntityId };
            LootDispenserManager.Instance.RequestLootAllFromCorpse(harness.Client, request);
            var inventory = harness.Client.Player.Inventory.PersonalInventory;
            Assert.AreEqual(1, inventory.Count(id => id == skull.EntityId));
            LootDispenserManager.Instance.RequestLootAllFromCorpse(harness.Client, request);
            Assert.AreEqual(1, inventory.Count(id => id == skull.EntityId));
            Assert.IsTrue(loot.FullyLooted);
            Assert.IsFalse(loot.IsLootable);
        }

        [TestMethod]
        public void PopulatedRouteThraxRespawnsAfterTwoMinutesWithANewLife()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 0);
            var thrax = harness.BootcampMap.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList).Single(creature => creature.SpawnPool?.DbId == 510230);
            harness.MovePlayerTo(thrax);
            CellManager.Instance.UpdateVisibility(harness.Client);
            MissileManager.Instance.MissileLaunch(harness.BootcampMap,
                new ActionData(harness.Client.Player, ActionId.WeaponAttack, 1, thrax.EntityId, 0), 10000);
            MissileManager.Instance.DoWork(harness.BootcampMap, 1000);
            Assert.AreEqual(CharacterState.Dead, thrax.State);
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 119999);
            Assert.IsFalse(harness.BootcampMap.MapCellInfo.Cells.Values.SelectMany(cell => cell.CreatureList)
                .Any(creature => creature.SpawnPool == thrax.SpawnPool && creature.State != CharacterState.Dead));
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 1);
            var replacement = harness.BootcampMap.MapCellInfo.Cells.Values.SelectMany(cell => cell.CreatureList)
                .Single(creature => creature.SpawnPool == thrax.SpawnPool && creature.State != CharacterState.Dead);
            Assert.AreNotEqual(thrax.EntityId, replacement.EntityId);
            Assert.AreEqual(8U, replacement.Level);
            Assert.AreEqual(0UL, replacement.CorpseLootEntityId);
        }

        [TestMethod]
        public void EveryOptionalDropUsesARealTemplateAndCanBeClaimedTogether()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 0);
            var thrax = harness.BootcampMap.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList).First(creature => creature.DbId == 510216);
            thrax.State = CharacterState.Dead;
            thrax.Attributes[Attributes.Health].Current = 0;
            var manager = new LootDispenserManager(harness.Context, missionManager: harness.Manager,
                lootRoll: (minimum, maximum) => minimum);
            var loot = manager.Create(harness.Client, thrax);
            CollectionAssert.AreEquivalent(new uint[] { 41666, 28, 56, 44917, 41665 },
                loot.LootItems.Select(item => item.ItemTemplateId).ToArray());
            foreach (var item in loot.LootItems)
            {
                Assert.IsTrue(item.Item.Id > 0);
                Assert.AreSame(item.Item, EntityManager.Instance.GetItem(item.EntityId));
                Assert.AreEqual(LootQuality.Normal, (LootQuality)item.Item.ItemTemplate.QualityId);
            }
            Assert.AreEqual(LootQuality.Normal, loot.LootQuality);
            harness.MovePlayerTo(thrax);
            CellManager.Instance.UpdateVisibility(harness.Client);

            manager.RequestLootAllFromCorpse(harness.Client,
                new RequestLootAllFromCorpsePacket { EntityId = loot.EntityId });

            Assert.IsTrue(loot.FullyLooted);
            Assert.IsTrue(loot.LootItems.All(item => item.Taken));
            Assert.IsTrue(loot.LootItems.All(item =>
                harness.Client.Player.Inventory.PersonalInventory.Contains(item.EntityId)));
        }

        [TestMethod]
        public void TizziksScenarioCorpseKeepsItsSkullAvailableAfterTheOneSecondScenarioCleanup()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, 0);
            harness.SeedMission(harness.Client.Player.Id, 1992, (uint)MissionState.Completed, true);
            var deSimone = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.CorporalDeSimoneCreatureId);
            harness.MovePlayerTo(deSimone);
            CellManager.Instance.UpdateVisibility(harness.Client);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, deSimone.EntityId, 1994));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(harness.Client, deSimone.EntityId, 1994, 4, 1));
            Assert.IsTrue(harness.Manager.TryGetAreaDefinition(1994, 439, out var exit));
            var previous = harness.Client.Player.Position;
            harness.MovePlayerTo(exit.Position);
            CellManager.Instance.UpdateVisibility(harness.Client);
            Assert.IsTrue(new MissionAreaService(() => harness.Manager)
                .RecordAcceptedMovement(harness.Client, previous, exit.Position));
            var boss = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.TizzikGiCreatureId);
            Assert.IsNotNull(boss.SpawnPool.ScenarioKey);
            BootcampRuntimeTestHarness.PrepareDirectDamageClient(harness.Client);
            harness.MovePlayerTo(boss);
            CellManager.Instance.UpdateVisibility(harness.Client);
            MissileManager.Instance.MissileLaunch(harness.BootcampMap,
                new ActionData(harness.Client.Player, ActionId.WeaponAttack, 1, boss.EntityId, 0), 10000);
            MissileManager.Instance.DoWork(harness.BootcampMap, 1000);
            var loot = harness.BootcampMap.LootDispensers[boss.CorpseLootEntityId];

            Assert.IsFalse(LootDispenserManager.Instance.AdvanceCorpseLifetime(harness.BootcampMap, boss, 1001),
                "Scenario cleanup must not discard the boss's unclaimed skull.");
            Assert.IsTrue(harness.Client.Player.Attributes[Attributes.Health].Current > 0, "The looter must be alive.");
            Assert.AreEqual(EntityType.Character, EntityManager.Instance.GetEntityType(harness.Client.Player.EntityId));
            Assert.AreSame(boss, EntityManager.Instance.Creatures[boss.EntityId]);
            Assert.IsTrue(harness.BootcampMap.MapCellInfo.Cells.Values.Any(cell => cell.CreatureList.Contains(boss)));
            LootDispenserManager.Instance.RequestCorpseLooting(harness.Client,
                new RequestCorpseLootingPacket { EntityId = loot.EntityId });
            Assert.AreEqual(harness.Client.Player.EntityId, loot.CurrentLooter,
                $"Boss corpse context {boss.MapContextId}, owner context {harness.Client.Player.MapContextId}, " +
                $"distance {Vector3.Distance(boss.Position, harness.Client.Player.Position)}, health {boss.Attributes[Attributes.Health].Current}.");
            Assert.IsFalse(LootDispenserManager.Instance.AdvanceCorpseLifetime(harness.BootcampMap, boss, 120000),
                "Opening the boss corpse must retain the normal extended looting lifetime.");
            LootDispenserManager.Instance.RequestLootAllFromCorpse(harness.Client,
                new RequestLootAllFromCorpsePacket { EntityId = loot.EntityId });
            Assert.IsTrue(loot.LootItems.Single(item => item.ItemTemplateId == 41666).Taken);
            Assert.IsTrue(loot.FullyLooted);
        }

    }
}
