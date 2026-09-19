extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Configuration;
    using Rasa.Configuration.ConnectionStrings;
    using Rasa.Configuration.ContextSetup;
    using Rasa.Context.Char;
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Game.Handlers;
    using Rasa.Managers;
    using Rasa.Packets.Game.Client;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.Char.Character;
    using Rasa.Repositories.Char.CharacterAppearance;
    using Rasa.Repositories.Char.CharacterInventory;
    using Rasa.Repositories.Char.CharacterLockbox;
    using Rasa.Repositories.Char.CharacterLogos;
    using Rasa.Repositories.Char.CharacterStartingExperience;
    using Rasa.Repositories.Char.CharacterTeleporter;
    using Rasa.Repositories.Char.GameAccount;
    using Rasa.Repositories.Char.Items;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Services.DbContext;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Structures.World;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class CharacterStartingExperienceCreationTests
    {
        [TestMethod]
        public void CreatingCharacterStoresPendingDeployment11StartingExperience()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(17);
            var client = context.CreateClient(17);

            new CharacterManager(context).RequestCreateCharacterInSlot(
                client,
                CreatePacket(slot: 1, familyName: "Fixture", characterName: "Starter"));

            using var verify = context.Open();
            var character = new GameAccountRepository(verify).Get(17).GetCharacterBySlot(1);
            Assert.IsNotNull(character);

            var startingExperience = new CharacterStartingExperienceRepository(verify).Get(character.Id);
            Assert.IsNotNull(startingExperience);
            Assert.AreEqual("deployment_11", startingExperience.ContentRevision);
            Assert.AreEqual(CharacterStartingExperienceState.Pending, startingExperience.State);
        }

        [TestMethod]
        public void CreatingCharacterAddsOnlyOneStartingExperienceRow()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(18);
            var client = context.CreateClient(18);

            new CharacterManager(context).RequestCreateCharacterInSlot(
                client,
                CreatePacket(slot: 1, familyName: "Fixture", characterName: "SoloRow"));

            using var verify = context.Open();
            var character = new GameAccountRepository(verify).Get(18).GetCharacterBySlot(1);
            Assert.IsNotNull(character);
            Assert.AreEqual(
                1,
                verify.Set<CharacterStartingExperienceEntry>()
                    .Count(entry => entry.CharacterId == character.Id));
        }

        [TestMethod]
        public void CharacterCreationTransactionRollbackRemovesCharacterAppearancesAndStartingExperience()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(19);
            var client = context.CreateClient(19);
            using var unitOfWork = context.CreateChar();
            var manager = new CharacterManager(context);

            Assert.ThrowsExactly<DbUpdateException>(() =>
                unitOfWork.ExecuteTransaction(() =>
                {
                    var createdCharacterId = InvokeInternalCreate(
                        manager,
                        client,
                        CreatePacket(slot: 1, familyName: "Fixture", characterName: "Rollback"),
                        unitOfWork);
                    Assert.IsNotNull(createdCharacterId);
                    throw new DbUpdateException("Injected creation failure.");
                }));

            using var verify = context.Open();
            Assert.AreEqual(0, verify.CharacterEntries.Count(entry => entry.AccountId == 19 && entry.Slot == 1));
            Assert.AreEqual(
                0,
                verify.CharacterAppearanceEntries.Count(entry => entry.CharacterId > 0 &&
                    entry.Character.AccountId == 19 &&
                    entry.Character.Slot == 1));
            Assert.AreEqual(
                0,
                verify.CharacterStartingExperienceEntries.Count(entry => entry.CharacterId > 0 &&
                    entry.Character.AccountId == 19 &&
                    entry.Character.Slot == 1));
        }

        [TestMethod]
        public void CloningCharacterMarksTheCloneLegacyInsteadOfPending()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(20);
            context.SeedCharacter(20, 1, "Source", cloneCredits: 1);
            var client = context.CreateClient(20);

            new CharacterManager(context).RequestCloneCharacterToSlot(
                client,
                CreateClonePacket(sourceSlot: 1, slot: 2, characterName: "Cloney"));

            using var verify = context.Open();
            var clone = new GameAccountRepository(verify).Get(20).GetCharacterBySlot(2);
            Assert.IsNotNull(clone);

            var startingExperience = new CharacterStartingExperienceRepository(verify).Get(clone.Id);
            Assert.IsNotNull(startingExperience);
            Assert.AreEqual("legacy", startingExperience.ContentRevision);
            Assert.AreEqual(CharacterStartingExperienceState.Legacy, startingExperience.State);
        }

        private static RequestCreateCharacterInSlotPacket CreatePacket(
            byte slot,
            string familyName,
            string characterName) =>
            new()
            {
                SlotNum = slot,
                FamilyName = familyName,
                CharacterName = characterName,
                Gender = 0,
                Scale = 1,
                RaceId = Race.Human
            };

        private static RequestCloneCharacterToSlotPacket CreateClonePacket(
            byte sourceSlot,
            byte slot,
            string characterName) =>
            new()
            {
                CloneSlotNum = sourceSlot,
                SlotNum = slot,
                CharacterName = characterName,
                Gender = 0,
                Scale = 1,
                RaceId = Race.Human
            };

        private static uint? InvokeInternalCreate(
            CharacterManager manager,
            Client client,
            RequestCreateCharacterInSlotPacket packet,
            ICharUnitOfWork unitOfWork) =>
            (uint?)typeof(CharacterManager)
                .GetMethod("InternalCreate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(manager, new object[] { client, packet, unitOfWork });

        private sealed class CharacterCreationContext : IGameUnitOfWorkFactory, IDisposable
        {
            private readonly string _directory = Path.Combine(
                AppContext.BaseDirectory,
                "TestDatabases",
                Guid.NewGuid().ToString("N"));

            private readonly List<uint> _addedTemplates = new();
            private readonly List<EntityClasses> _addedClasses = new();

            private string Database => Path.Combine(_directory, "characters");

            internal CharacterCreationContext()
            {
                if (Logger.Config == null)
                    Logger.UpdateConfig(new Logger.LoggerConfig());

                Directory.CreateDirectory(_directory);
                PrepareStarterItems();
                using var context = Open();
                context.Database.Migrate();
            }

            internal SqliteCharContext Open() =>
                new(
                    Options.Create(new DatabaseConfiguration
                    {
                        Provider = "Sqlite",
                        Char = new DatabaseConnectionConfiguration { Database = Database }
                    }),
                    new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory()),
                    new SqliteDbContextPropertyModifier());

            internal void SeedAccount(uint accountId, string familyName = "Fixture")
            {
                using var context = Open();
                context.GameAccountEntries.Add(new GameAccountEntry
                {
                    Id = accountId,
                    Email = $"task4-{accountId}@example.invalid",
                    Name = $"Task4Account{accountId}",
                    FamilyName = familyName,
                    SelectedSlot = 0,
                    CanSkipBootcamp = false,
                    CreatedAt = DateTime.UtcNow,
                    LastLogin = DateTime.UtcNow,
                    LastIp = "127.0.0.1",
                    Level = 0
                });
                context.SaveChanges();
            }

            internal uint SeedCharacter(
                uint accountId,
                byte slot,
                string name,
                uint cloneCredits = 0)
            {
                using var context = Open();
                var character = new CharacterEntry
                {
                    AccountId = accountId,
                    Slot = slot,
                    Name = name,
                    Race = 1,
                    Class = 1,
                    Gender = 0,
                    Scale = 1,
                    Experience = 4000,
                    Level = 9,
                    Credit = 100,
                    Prestige = 50,
                    ActiveWeapon = 0,
                    CurrentAbilitySlot = 0,
                    Body = 0,
                    Mind = 0,
                    Spirit = 0,
                    CloneCredits = cloneCredits,
                    MapContextId = 1220,
                    CoordX = 1,
                    CoordY = 2,
                    CoordZ = 3,
                    Rotation = 0,
                    RunState = 1,
                    CrouchState = 0,
                    NumLogins = 0,
                    LastLogin = DateTime.UtcNow,
                    TotalTimePlayed = 0,
                    CreatedAt = DateTime.UtcNow,
                    LastPvPClan = DateTime.UtcNow
                };
                context.CharacterEntries.Add(character);
                context.SaveChanges();
                return character.Id;
            }

            internal Client CreateClient(uint accountId)
            {
                var client = new Client(this, new ClientPacketHandler())
                {
                    State = ClientState.CharacterSelection
                };
                typeof(Client).GetProperty(nameof(Client.AccountEntry))!
                    .SetValue(client, LoadAccount(accountId));
                return client;
            }

            public ICharUnitOfWork CreateChar()
            {
                var context = Open();
                return new CharUnitOfWork(
                    context,
                    gameAccounts: new GameAccountRepository(context),
                    censoredWords: null,
                    characters: new CharacterRepository(context),
                    characterAbilityDrawers: null,
                    characterAppearances: new CharacterAppearanceRepository(context),
                    characterInventories: new CharacterInventoryRepository(context),
                    characterLockboxes: new CharacterLockboxRepository(context),
                    characterLogoses: new CharacterLogosRepository(context),
                    characterMissions: null,
                    characterMissionDeadlines: null,
                    characterMissionProgress: null,
                    characterMissionScenario: null,
                    characterOptions: null,
                    characterQualifications: null,
                    characterSkills: null,
                    characterStartingExperience: new CharacterStartingExperienceRepository(context),
                    characterTeleporters: new CharacterTeleporterRepository(context),
                    characterTitles: null,
                    auctions: null,
                    clans: null,
                    clanInventories: null,
                    clanMembers: null,
                    clanLockboxLogs: null,
                    friends: null,
                    ignoreds: null,
                    items: new ItemRepository(context),
                    petitions: null,
                    userOptions: null);
            }

            public IWorldUnitOfWork CreateWorld() =>
                DepartureFailureTests.StrictProxy.Create<IWorldUnitOfWork>(
                    new Dictionary<string, object>(),
                    "Dispose",
                    "Complete",
                    "Reject");

            public void Dispose()
            {
                foreach (var templateId in _addedTemplates)
                    ItemManager.Instance.ItemTemplateItemClass.Remove(templateId);
                foreach (var classId in _addedClasses)
                    EntityClassManager.Instance.LoadedEntityClasses.Remove(classId);
                SqliteConnection.ClearAllPools();
                Directory.Delete(_directory, true);
            }

            private GameAccountEntry LoadAccount(uint accountId)
            {
                using var context = Open();
                return new GameAccountRepository(context).Get(accountId);
            }

            private void PrepareStarterItems()
            {
                AddStarterItem(17131, 27120);
                AddStarterItem(28, 3147);
                AddStarterItem(13126, 15602);
                AddStarterItem(13156, 15632);
                AddStarterItem(13186, 15662);
            }

            private void AddStarterItem(uint templateId, uint classId)
            {
                if (ItemManager.Instance.ItemTemplateItemClass.TryAdd(templateId, (EntityClasses)classId))
                    _addedTemplates.Add(templateId);

                var key = (EntityClasses)classId;
                if (!EntityClassManager.Instance.LoadedEntityClasses.ContainsKey(key))
                {
                    EntityClassManager.Instance.LoadedEntityClasses.Add(
                        key,
                        new EntityClass(classId, "fixture", 0, 0, new List<AugmentationType>(), true));
                    _addedClasses.Add(key);
                }

                EntityClassManager.Instance.LoadedEntityClasses[key].ItemClassInfo =
                    new ItemClassInfo(new ItemClassEntry
                    {
                        Id = classId,
                        MaxHitPoints = 100,
                        StackSize = 50000
                    });
            }
        }
    }
}
