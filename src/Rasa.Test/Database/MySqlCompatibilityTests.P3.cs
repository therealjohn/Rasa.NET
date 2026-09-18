using System;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;

namespace Rasa.Test.Database
{
    using Rasa.Context;
    using Rasa.Context.Char;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.Char.Character;
    using Rasa.Repositories.Char.CharacterAbilityDrawer;
    using Rasa.Repositories.Char.CharacterInventory;
    using Rasa.Repositories.Char.CharacterMission;
    using Rasa.Repositories.Char.CharacterSkills;
    using Rasa.Repositories.Char.Items;
    using Rasa.Structures.Char;
    using Rasa.Structures;

    public partial class MySqlCompatibilityTests
    {
        [ClassInitialize]
        public static void InitializeP3Storage(TestContext context)
        {
            if (Logger.Config == null)
                Logger.UpdateConfig(new Logger.LoggerConfig());
        }

        [TestMethod]
        public void P3AbilityTraySelectionMigrationPreservesRowsAndDefaultsToZero()
        {
            using var database = DisposableDatabase.Create(typeof(MySqlCharContext), 64);
            string[] applied;
            using (var historical = database.CreateContext())
            {
                historical.GetService<IMigrator>().Migrate("20260916173014_Net10IdentityMetadata");
                SeedP3Character(historical);
                applied = historical.Database.GetAppliedMigrations().ToArray();
            }
            using (var upgraded = database.CreateContext())
            {
                upgraded.Database.Migrate();
                var upgradedMigrations = upgraded.Database.GetAppliedMigrations().ToArray();
                CollectionAssert.AreEqual(applied, upgradedMigrations.Take(applied.Length).ToArray());
                Assert.AreEqual("20260917130734_AbilityTraySelection", upgradedMigrations[applied.Length]);
                Assert.IsFalse(upgraded.Database.GetPendingMigrations().Any());
                Assert.AreEqual("0", upgraded.Database.SqlQueryRaw<string>(
                    "SELECT COLUMN_DEFAULT AS Value FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'character' " +
                    "AND COLUMN_NAME = 'current_ability_slot'").Single());
            }
            using var reopened = CreateP3UnitOfWork(database);
            var character = reopened.Characters.Get(123);
            Assert.AreEqual(17U, character.AccountId);
            Assert.AreEqual("P3Preserved", character.Name);
            Assert.AreEqual(4000U, character.Experience);
            Assert.AreEqual((byte)9, character.Level);
            Assert.AreEqual(100, character.Credit);
            Assert.AreEqual((byte)0, character.CurrentAbilitySlot);
            Assert.AreEqual("P3Family", character.GameAccount.FamilyName);
        }

        [TestMethod]
        public void P3ProgressionTransactionReopensExperienceAndLevelTogether()
        {
            using var database = CreateP3Database();
            using (var unit = CreateP3UnitOfWork(database))
                unit.ExecuteTransaction(() => unit.Characters.UpdateCharacterProgression(123, 12345, 10));

            using var reopened = CreateP3UnitOfWork(database);
            var character = reopened.Characters.Get(123);
            Assert.AreEqual(12345U, character.Experience);
            Assert.AreEqual((byte)10, character.Level);
            Assert.AreEqual(100, character.Credit);
        }

        [TestMethod]
        public void P3TrainingAndTrayTransactionReopensUpdatedRanksAndSelection()
        {
            using var database = CreateP3Database();
            using (var seed = CreateP3UnitOfWork(database))
                seed.ExecuteTransaction(() =>
                {
                    seed.CharacterSkills.AddOrUpdate(123, 165, 401, 1);
                    seed.CharacterAbilityDrawers.AddOrUpdate(123, 24, 401, 1);
                });
            using (var unit = CreateP3UnitOfWork(database))
                unit.ExecuteTransaction(() =>
                {
                    unit.CharacterSkills.AddOrUpdate(123, 165, 401, 3);
                    unit.CharacterSkills.AddOrUpdate(123, 49, 194, 1);
                    unit.CharacterAbilityDrawers.AddOrUpdate(123, 24, 401, 3);
                    unit.CharacterAbilityDrawers.AddOrUpdate(123, 0, 194, 1);
                    unit.Characters.UpdateCharacterAbilitySlot(123, 24);
                });

            using var reopened = CreateP3UnitOfWork(database);
            var skills = reopened.CharacterSkills.GetCharacterSkills(123).OrderBy(skill => skill.SkillId).ToArray();
            CollectionAssert.AreEqual(new[] { (49U, 194, 1), (165U, 401, 3) },
                skills.Select(skill => (skill.SkillId, skill.AbilityId, skill.SkillLevel)).ToArray());
            var drawer = reopened.CharacterAbilityDrawers.GetCharacterAbilities(123).OrderBy(slot => slot.AbilitySlot);
            CollectionAssert.AreEqual(new[] { (0, 194, 1U), (24, 401, 3U) },
                drawer.Select(slot => (slot.AbilitySlot, slot.AbilityId, slot.AbilityLevel)).ToArray());
            Assert.AreEqual((byte)24, reopened.Characters.Get(123).CurrentAbilitySlot);
        }

        [TestMethod]
        public void P4MissionRowsPersistForOneCharacter()
        {
            using var database = DisposableDatabase.Create(typeof(MySqlCharContext), 64);
            using (var historical = database.CreateContext())
            {
                historical.GetService<IMigrator>().Migrate("20260917130734_AbilityTraySelection");
                SeedP3Character(historical);
                historical.Database.ExecuteSqlRaw(
                    "INSERT INTO character_mission (character_id, mission_id, mission_state) VALUES (123, 321, 0)");
            }
            using (var upgraded = database.CreateContext())
            {
                upgraded.Database.Migrate();
                Assert.AreEqual(0, upgraded.Database.SqlQueryRaw<int>(
                    "SELECT completeable AS Value FROM character_mission " +
                    "WHERE character_id = 123 AND mission_id = 321").Single());
                Assert.AreEqual(0, upgraded.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM character_mission_objective").Single());
            }
            using (var unit = CreateP3UnitOfWork(database))
            {
                unit.CharacterMissions.Add(new CharacterMissionEntry(123, 429, 4)
                {
                    Completeable = true
                });
                unit.CharacterMissionProgress.AddObjectives(new[]
                {
                    new CharacterMissionObjectiveEntry(123, 429, 5, 1)
                    {
                        Counters =
                        {
                            new CharacterMissionObjectiveCounterEntry(123, 429, 5, 3, 4)
                        },
                        ItemCounters =
                        {
                            new CharacterMissionObjectiveItemCounterEntry(123, 429, 5, 200, 6)
                        }
                    }
                });
                unit.CharacterMissionProgress.SetObjectiveState(123, 429, 5, 2);
                unit.CharacterMissionProgress.SetCounter(123, 429, 5, 3, 7);
                unit.CharacterMissionProgress.SetItemCounter(123, 429, 5, 200, 8);
            }

            using var reopened = CreateP3UnitOfWork(database);
            var missions = reopened.CharacterMissions.Get(123).OrderBy(mission => mission.MissionId).ToArray();
            Assert.AreEqual(2, missions.Length);
            Assert.AreEqual((321U, 0U, false),
                (missions[0].MissionId, missions[0].MissionState, missions[0].Completeable));
            Assert.AreEqual((429U, 4U, true),
                (missions[1].MissionId, missions[1].MissionState, missions[1].Completeable));
            var objective = reopened.CharacterMissionProgress.Get(123, 429)
                .Missions[429].Objectives[5];
            Assert.AreEqual((byte)2, objective.State);
            Assert.AreEqual(7U, objective.Counters[3]);
            Assert.AreEqual(8U, objective.ItemCounters[200]);
        }

        [TestMethod]
        public void P4CompetingMissionRewardTransactionsGrantAtMostOnce()
        {
            using var database = CreateP3Database();
            using (var seed = CreateP3UnitOfWork(database))
                seed.CharacterMissions.Add(new CharacterMissionEntry(123, 429, 0)
                {
                    Completeable = true
                });
            using var barrier = new Barrier(2);
            var contextIds = new ConcurrentBag<Guid>();
            var rejectedErrorNumbers = new ConcurrentBag<int>();

            bool TryGrant()
            {
                using var context = (MySqlCharContext)database.CreateContext();
                contextIds.Add(context.ContextId.InstanceId);
                using var unit = CreateP3UnitOfWork(context);
                var granted = false;
                try
                {
                    unit.ExecuteTransaction(() =>
                    {
                        var mission = unit.CharacterMissions.Get(123, 429);
                        if (mission?.MissionState != 0 || !mission.Completeable)
                            return;
                        Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(10)),
                            "Both MySQL transactions must read the active mission before either writes rewards.");
                        unit.Characters.UpdateCharacterProgression(123, 4100, 9);
                        unit.Characters.UpdateCharacterCredits(123, 107);
                        var itemId = unit.Items.CreateItem(
                            new P3ItemChange { ItemTemplateId = 28, StackSize = 3 });
                        unit.CharacterInventories.AddInvItem(17, 123, 1, 50, itemId);
                        mission.MissionState = 4;
                        mission.Completeable = false;
                        granted = true;
                    });
                    return granted;
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    var mysqlError = FindMySqlException(error);
                    Assert.IsNotNull(mysqlError, "The rejected competitor must be a live MySQL persistence conflict.");
                    rejectedErrorNumbers.Add(mysqlError.Number);
                    return false;
                }
            }

            var results = Task.WhenAll(Task.Run(TryGrant), Task.Run(TryGrant))
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, contextIds.Distinct().Count(), "Competing requests must use distinct DbContext instances.");
            Assert.AreEqual(1, results.Count(result => result));
            CollectionAssert.AreEqual(new[] { 1213 }, rejectedErrorNumbers.ToArray(),
                "The losing serializable transaction must be rejected as a MySQL deadlock.");
            using var reopenedContext = (MySqlCharContext)database.CreateContext();
            using var reopened = CreateP3UnitOfWork(reopenedContext);
            var character = reopened.Characters.Get(123);
            Assert.AreEqual(4100U, character.Experience);
            Assert.AreEqual(107, character.Credit);
            var mission = reopened.CharacterMissions.Get(123, 429);
            Assert.AreEqual(4U, mission.MissionState);
            Assert.IsFalse(mission.Completeable);
            Assert.AreEqual(3U, reopenedContext.ItemEntries.Single().StackSize);
            Assert.AreEqual(1, reopenedContext.CharacterInventoryEntries.Count());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void P3ItemInventoryCreditsAndAmmoBatchCommitsOrRollsBackAfterImmediateSaves(bool failAfterSaves)
        {
            using var database = CreateP3Database();
            using (var seed = (MySqlCharContext)database.CreateContext())
            {
                seed.ItemEntries.AddRange(
                    new ItemEntry(new P3ItemChange { ItemTemplateId = 145, StackSize = 1 }) { ItemId = 31, AmmoCount = 7 },
                    new ItemEntry(new P3ItemChange { ItemTemplateId = 28, StackSize = 10 }) { ItemId = 32 });
                seed.CharacterInventoryEntries.AddRange(
                    new CharacterInventoryEntry(17, 123, 9, 0, 31),
                    new CharacterInventoryEntry(17, 123, 1, 50, 32));
                seed.SaveChanges();
            }

            using var context = (MySqlCharContext)database.CreateContext();
            using var unit = CreateP3UnitOfWork(context);
            var saved = 0;
            context.SavedChanges += (_, _) =>
            {
                Assert.IsNotNull(context.Database.CurrentTransaction, "Immediate saves must use the outer transaction.");
                Assert.AreEqual(IsolationLevel.Serializable,
                    context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel);
                saved++;
            };
            uint createdId = 0;
            void WriteBatch(bool fail)
            {
                unit.Items.UpdateItemStackSize(new P3ItemChange { Id = 32, StackSize = 5 });
                createdId = unit.Items.CreateItem(new P3ItemChange { ItemTemplateId = 28, StackSize = 3 });
                Assert.AreNotEqual(0U, createdId);
                unit.CharacterInventories.AddInvItem(17, 123, 1, 51, createdId);
                unit.Characters.UpdateCharacterCredits(123, 109);
                unit.Items.UpdateAmmo(new P3ItemChange { Id = 31, CurrentAmmo = 12 });
                Assert.AreEqual(5, saved, "All five repository SaveChanges calls must reach MySQL before failure.");
                if (fail)
                {
                    // This existing inventory row is not tracked: MySQL, not EF identity tracking, rejects its key.
                    unit.CharacterInventories.AddInvItem(17, 123, 1, 52, 31);
                }
            }

            if (failAfterSaves)
            {
                var error = Assert.ThrowsExactly<DbUpdateException>(() => unit.ExecuteTransaction(() => WriteBatch(true)));
                Assert.IsInstanceOfType<MySqlException>(error.InnerException);
                Assert.AreEqual(1062, ((MySqlException)error.InnerException).Number, "Expected a live duplicate-key failure.");
                Assert.AreEqual(5, saved);
                Assert.IsFalse(context.ChangeTracker.Entries().Any(), "Rollback must discard failed tracked writes.");
                AssertP3Batch(database, createdId, false);
                saved = 0;
            }

            // Reusing the failed unit also proves its tracker and transaction were released for a clean retry.
            unit.ExecuteTransaction(() => WriteBatch(false));
            Assert.AreEqual(5, saved);
            AssertP3Batch(database, createdId, true);
        }

        private static void AssertP3Batch(DisposableDatabase database, uint createdId, bool committed)
        {
            using var context = (MySqlCharContext)database.CreateContext();
            using var reopened = CreateP3UnitOfWork(context);
            Assert.AreEqual(committed ? 12U : 7U, reopened.Items.GetItem(31).AmmoCount);
            Assert.AreEqual(committed ? 5U : 10U, reopened.Items.GetItem(32).StackSize);
            Assert.AreEqual(committed ? 109 : 100, reopened.Characters.Get(123).Credit);
            Assert.AreEqual(committed ? 3 : 2, context.ItemEntries.Count());
            var inventory = reopened.CharacterInventories.GetItems(17).OrderBy(item => item.ItemId).ToArray();
            Assert.AreEqual(committed ? 3 : 2, inventory.Length);
            Assert.AreEqual((123U, 9U, 0U, 31U),
                (inventory[0].CharacterId, inventory[0].InventoryType, inventory[0].SlotId, inventory[0].ItemId));
            Assert.AreEqual((123U, 1U, 50U, 32U),
                (inventory[1].CharacterId, inventory[1].InventoryType, inventory[1].SlotId, inventory[1].ItemId));
            var created = reopened.Items.GetItem(createdId);
            if (committed)
            {
                Assert.IsNotNull(created);
                Assert.AreEqual(28U, created.ItemTemplateId);
                Assert.AreEqual(3U, created.StackSize);
                Assert.AreEqual((123U, 1U, 51U, createdId),
                    (inventory[2].CharacterId, inventory[2].InventoryType, inventory[2].SlotId, inventory[2].ItemId));
            }
            else
            {
                Assert.IsNull(created, "The new item must not survive a rolled-back batch.");
            }
        }

        private static DisposableDatabase CreateP3Database()
        {
            var database = DisposableDatabase.Create(typeof(MySqlCharContext), 64);
            try
            {
                using var context = database.CreateContext();
                context.Database.Migrate();
                SeedP3Character(context);
                return database;
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }

        private static void SeedP3Character(RasaDbContextBase context)
        {
            // Historical inserts deliberately omit the new column, exercising its live database default on upgrade.
            context.Database.ExecuteSqlRaw(
                "INSERT INTO account (id, email, name, family_name) VALUES (17, 'p3@example.invalid', 'P3Account', 'P3Family')");
            context.Database.ExecuteSqlRaw(
                "INSERT INTO `character` (id, account_id, slot, name, race, class, gender, scale, experience, level, " +
                "credit, body, mind, spirit, map_context_id, coord_x, coord_y, coord_z, rotation) " +
                "VALUES (123, 17, 1, 'P3Preserved', 1, 1, 0, 1, 4000, 9, 100, 0, 0, 0, 1220, 1, 2, 3, 0)");
        }

        private static ICharUnitOfWork CreateP3UnitOfWork(DisposableDatabase database) =>
            CreateP3UnitOfWork((MySqlCharContext)database.CreateContext());

        private static ICharUnitOfWork CreateP3UnitOfWork(MySqlCharContext context) =>
            new CharUnitOfWork(context,
                gameAccounts: null, censoredWords: null, characters: new CharacterRepository(context),
                characterAbilityDrawers: new CharacterAbilityDrawerRepository(context), characterAppearances: null,
                characterInventories: new CharacterInventoryRepository(context), characterLockboxes: null,
                characterLogoses: null, characterMissions: new CharacterMissionRepository(context), characterOptions: null,
                characterSkills: new CharacterSkillsRepository(context), characterTeleporters: null,
                characterTitles: null, clans: null, clanInventories: null, clanMembers: null,
                friends: null, ignoreds: null, items: new ItemRepository(context), userOptions: null);

        private static MySqlException FindMySqlException(Exception error)
        {
            while (error != null)
            {
                if (error is MySqlException mysqlError)
                    return mysqlError;
                error = error.InnerException;
            }
            return null;
        }

        private sealed class P3ItemChange : IItemChange
        {
            public uint Id { get; set; }
            public uint ItemTemplateId { get; set; }
            public uint Color { get; set; }
            public string Crafter { get; set; } = "";
            public int CurrentHitPoints { get; set; }
            public uint StackSize { get; set; }
            public uint CurrentAmmo { get; set; }
        }
    }
}
