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
    using Rasa.Repositories.Char.CharacterAbilityDrawer;
    using Rasa.Repositories.Char.CharacterAppearance;
    using Rasa.Repositories.Char.CharacterInventory;
    using Rasa.Repositories.Char.CharacterLockbox;
    using Rasa.Repositories.Char.CharacterLogos;
    using Rasa.Repositories.Char.CharacterMission;
    using Rasa.Repositories.Char.CharacterMissionDeadline;
    using Rasa.Repositories.Char.CharacterMissionProgress;
    using Rasa.Repositories.Char.CharacterMissionScenario;
    using Rasa.Repositories.Char.CharacterSkills;
    using Rasa.Repositories.Char.CharacterStartingExperience;
    using Rasa.Repositories.Char.CharacterTeleporter;
    using Rasa.Repositories.Char.CharacterTitle;
    using Rasa.Repositories.Char.Clan;
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
        public void CreatingCharacterDoesNotGrantNormalStartInventoryItems()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(181);
            var client = context.CreateClient(181);

            new CharacterManager(context).RequestCreateCharacterInSlot(
                client,
                CreatePacket(slot: 1, familyName: "Fixture", characterName: "NoGear"));

            using var verify = context.Open();
            var character = new GameAccountRepository(verify).Get(181).GetCharacterBySlot(1);
            Assert.IsNotNull(character);
            Assert.AreEqual(
                0,
                verify.CharacterInventoryEntries.Count(entry =>
                    entry.CharacterId == character.Id &&
                    entry.InventoryType == (uint)InventoryType.Personal));
            Assert.IsNotNull(new CharacterLockboxRepository(verify).Get(181));
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

        [TestMethod]
        public void SwitchingToBootcampCharacterCreatesOwnedPrivate1985Instance()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(21);
            var characterId = context.SeedCharacter(21, 1, "Bootcamp", mapContextId: 1985);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            var client = context.CreateClient(21);
            var maps = new MapChannelManager(context, privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(1985, CreatePublicMap(1985));
            using var scope = new MapChannelManagerScope(maps);

            new CharacterManager(context).RequestSwitchToCharacterInSlot(
                client,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });

            Assert.IsNotNull(client.Player.MapChannel);
            Assert.IsTrue(client.Player.MapChannel.IsPrivateInstance);
            Assert.AreEqual(1985U, client.Player.MapChannel.MapInfo.MapContextId);
            Assert.AreEqual(characterId, client.Player.MapChannel.OwnerCharacterId);
            Assert.AreSame(client.Player.MapChannel,
                maps.FindOwnedPrivateInstance(1985, characterId));
            Assert.AreEqual(ClientState.Loading, client.State);
        }

        [TestMethod]
        public void BootcampReconnectRecreatesEquivalentOwnedPrivate1985RuntimeAfterRelease()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(22);
            var characterId = context.SeedCharacter(22, 1, "Reconnect", mapContextId: 1985);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            var maps = new MapChannelManager(context, privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(1985, CreatePublicMap(1985));
            using var scope = new MapChannelManagerScope(maps);

            var firstClient = context.CreateClient(22);
            new CharacterManager(context).RequestSwitchToCharacterInSlot(
                firstClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });
            var firstRuntime = firstClient.Player.MapChannel;

            maps.ReleaseOwnedPrivateInstances(characterId);

            var secondClient = context.CreateClient(22);
            new CharacterManager(context).RequestSwitchToCharacterInSlot(
                secondClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });

            Assert.AreNotSame(firstRuntime, secondClient.Player.MapChannel);
            Assert.IsTrue(secondClient.Player.MapChannel.IsPrivateInstance);
            Assert.AreEqual(1985U, secondClient.Player.MapChannel.MapInfo.MapContextId);
            Assert.AreEqual(characterId, secondClient.Player.MapChannel.OwnerCharacterId);
            Assert.AreSame(secondClient.Player.MapChannel,
                maps.FindOwnedPrivateInstance(1985, characterId));
        }

        [TestMethod]
        public void DisconnectingBootcampCharacterReleasesOwnedPrivateRuntimeBeforeReconnect()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(24);
            var characterId = context.SeedCharacter(24, 1, "Disconnect", mapContextId: 1985);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            var maps = new MapChannelManager(context, privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(1985, CreatePublicMap(1985));
            using var scope = new MapChannelManagerScope(maps);

            var firstClient = context.CreateClient(24);
            new CharacterManager(context).RequestSwitchToCharacterInSlot(
                firstClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });
            var firstRuntime = firstClient.Player.MapChannel;

            maps.CleanupDisconnected(firstClient);

            Assert.IsNull(maps.FindOwnedPrivateInstance(1985, characterId));

            var secondClient = context.CreateClient(24);
            new CharacterManager(context).RequestSwitchToCharacterInSlot(
                secondClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });

            Assert.AreNotSame(firstRuntime, secondClient.Player.MapChannel);
            Assert.IsTrue(secondClient.Player.MapChannel.IsPrivateInstance);
            Assert.AreEqual(characterId, secondClient.Player.MapChannel.OwnerCharacterId);
        }

        [TestMethod]
        public void DeletingCharacterReleasesOwnedPrivateInstances()
        {
            using var context = new CharacterCreationContext();
            context.SeedAccount(23);
            var characterId = context.SeedCharacter(23, 1, "DeleteMe", mapContextId: 1985);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            var maps = new MapChannelManager(context, privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(1985, CreatePublicMap(1985));
            var owned = maps.GetOrCreatePrivateInstance(1985, characterId);
            owned.QueuedClients.Enqueue(new Client(context, new ClientPacketHandler())
            {
                State = ClientState.Loading
            });
            owned.QueuedMissiles.Add(new Missile());
            using var scope = new MapChannelManagerScope(maps);
            var client = context.CreateClient(23);

            new CharacterManager(context).RequestDeleteCharacterInSlot(
                client,
                new RequestDeleteCharacterInSlotPacket { Slot = 1 });

            Assert.IsNull(maps.FindOwnedPrivateInstance(1985, characterId));
            Assert.AreEqual(0, owned.QueuedClients.Count);
            Assert.AreEqual(0, owned.QueuedMissiles.Count);
            using var verify = context.Open();
            Assert.IsNull(new GameAccountRepository(verify).Get(23).GetCharacterBySlot(1));
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

        private static MapChannel CreatePublicMap(uint contextId) => new()
        {
            MapInfo = new MapInfo(contextId, "bootcamp_fixture", 1, 0),
            ClientList = new List<Client>(),
            PlayerLimit = 128
        };

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
                uint cloneCredits = 0,
                uint mapContextId = 1220)
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
                    MapContextId = mapContextId,
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

            internal void SeedStartingExperience(
                uint characterId,
                CharacterStartingExperienceState state,
                string revision = "deployment_11")
            {
                using var context = Open();
                context.CharacterStartingExperienceEntries.Add(
                    new CharacterStartingExperienceEntry(characterId, revision, state));
                context.SaveChanges();
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
                    characterAbilityDrawers: new CharacterAbilityDrawerRepository(context),
                    characterAppearances: new CharacterAppearanceRepository(context),
                    characterInventories: new CharacterInventoryRepository(context),
                    characterLockboxes: new CharacterLockboxRepository(context),
                    characterLogoses: new CharacterLogosRepository(context),
                    characterMissions: new CharacterMissionRepository(context),
                    characterMissionDeadlines: new CharacterMissionDeadlineRepository(context),
                    characterMissionProgress: new CharacterMissionProgressRepository(context),
                    characterMissionScenario: new CharacterMissionScenarioRepository(context),
                    characterOptions: null,
                    characterQualifications: null,
                    characterSkills: new CharacterSkillsRepository(context),
                    characterStartingExperience: new CharacterStartingExperienceRepository(context),
                    characterTeleporters: new CharacterTeleporterRepository(context),
                    characterTitles: new CharacterTitleRepository(context),
                    auctions: null,
                    clans: new ClanRepository(context),
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

        private sealed class MapChannelManagerScope : IDisposable
        {
            private readonly FieldInfo _singleton = typeof(MapChannelManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly object _previous;

            internal MapChannelManagerScope(MapChannelManager current)
            {
                _previous = _singleton.GetValue(null);
                _singleton.SetValue(null, current);
            }

            public void Dispose()
            {
                _singleton.SetValue(null, _previous);
            }
        }
    }
}
