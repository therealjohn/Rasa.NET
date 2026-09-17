extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Game.Handlers;
    using Rasa.Managers;
    using Rasa.Packets;
    using Rasa.Packets.Communicator.Both;
    using Rasa.Packets.Inventory.Client;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.Protocol;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.Char.CharacterAppearance;
    using Rasa.Repositories.Char.CharacterInventory;
    using Rasa.Repositories.Char.CharacterOption;
    using Rasa.Repositories.Char.Clan;
    using Rasa.Repositories.Char.Friend;
    using Rasa.Repositories.Char.Ignored;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Structures.World;
    using Rasa.Test.World;
    using StrictProxy = Rasa.Test.World.DepartureFailureTests.StrictProxy;

    [TestClass]
    [DoNotParallelize]
    public class ProgressionBoundaryTests
    {
        [TestMethod]
        [DataRow((byte)0, false)]
        [DataRow((byte)51, false)]
        [DataRow(byte.MaxValue, false)]
        [DataRow((byte)0, true)]
        [DataRow((byte)51, true)]
        [DataRow(byte.MaxValue, true)]
        public void MapLoadedRejectsInvalidLevelBeforeAdmissionAndThenLoadsHealthyClient(byte level, bool routed)
        {
            using var fixture = new BoundaryContext();
            var invalid = fixture.CreateLoadingClient(level);
            var healthy = fixture.CreateLoadingClient(1);
            var originalOutput = Console.Out;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                if (routed)
                    fixture.Route(invalid, new MapLoadedPacket());
                else
                    fixture.Maps.MapLoaded(invalid);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            StringAssert.Contains(output.ToString(), $"Character {invalid.Player.Id} has invalid persisted level {level}");
            fixture.AssertRejected(invalid);
            Assert.AreEqual(level, invalid.Player.Level);
            Assert.IsTrue(invalid.Player.Attributes.Values.All(attribute => attribute.CurrentMax == 0));
            Assert.AreEqual(0, fixture.InventoryReads);
            Assert.AreEqual(0, WorldTestContext.Drain(invalid).Count);
            Assert.AreEqual(0, WorldTestContext.Drain(healthy).Count);

            fixture.Route(invalid, new MapLoadedPacket());
            fixture.Route(healthy, new MapLoadedPacket());

            Assert.AreEqual(ClientState.Ingame, healthy.State);
            Assert.IsTrue(CellManager.Instance.IsInWorld(healthy));
            Assert.IsTrue(EntityManager.Instance.Players.ContainsKey(healthy.Player.EntityId));
            Assert.AreEqual(1, fixture.InventoryReads);
            Assert.AreEqual(286, healthy.Player.Attributes[Attributes.Health].Current);
            var packets = WorldTestContext.Drain(healthy).Select(packet => packet.Message).OfType<CallMethodMessage>().ToList();
            Assert.IsTrue(packets.Any(packet => packet.MethodId == GameOpcode.LoginOk));
            Assert.IsTrue(packets.Any(packet => packet.MethodId == GameOpcode.SetControlledActorId));
            Assert.IsFalse(fixture.Maps.MapChannelArray.Values.Any(map => map.QueuedClients.Contains(healthy)));
        }

        [TestMethod]
        [DataRow((byte)0)]
        [DataRow((byte)51)]
        [DataRow(byte.MaxValue)]
        public void DropshipMapLoadedRejectsInvalidLevelBeforeArrivalOrLoginPublication(byte level)
        {
            using var fixture = new BoundaryContext();
            var invalid = fixture.CreateLoadingClient(level);
            var healthy = fixture.CreateLoadingClient(1);
            var origin = fixture.Progression.World.Map;
            var destination = DropshipTravelTests.CreateDestination();
            fixture.Maps.MapChannelArray.Add(destination.MapInfo.MapContextId, destination);
            invalid.PendingTransfer = new PlayerTransfer
            {
                OriginMap = origin,
                DestinationMap = destination,
                HasDeparted = true,
                IsDropship = true,
                Deadline = long.MaxValue
            };
            invalid.Player.MapChannel = destination;
            invalid.Player.MapContextId = destination.MapInfo.MapContextId;
            invalid.LoadingMap = destination.MapInfo.MapContextId;
            invalid.State = ClientState.Teleporting;
            var ships = DynamicObjectManager.Instance.Dropships.Count;
            Assert.IsTrue(DynamicObjectManager.Instance.IsExpectedMapLoad(invalid));

            fixture.Route(invalid, new MapLoadedPacket());

            fixture.AssertRejected(invalid);
            Assert.IsNull(invalid.PendingTransfer);
            Assert.AreEqual(ships, DynamicObjectManager.Instance.Dropships.Count);
            Assert.AreEqual(0, destination.MapCellInfo.Cells.Count);
            Assert.AreEqual(0, WorldTestContext.Drain(invalid).Count);
            Assert.AreEqual(0, WorldTestContext.Drain(healthy).Count);
            fixture.Route(healthy, new MapLoadedPacket());
            Assert.IsTrue(CellManager.Instance.IsInWorld(healthy));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MapLoadedKeepsUnrelatedApplicationErrorsObservable(bool routed)
        {
            var expected = new InvalidOperationException("Fixture inventory application failure.");
            using var fixture = new BoundaryContext(expected);
            var client = fixture.CreateLoadingClient(1);

            if (routed)
            {
                var wrapped = Assert.ThrowsExactly<TargetInvocationException>(() => fixture.Route(client, new MapLoadedPacket()));
                Assert.AreSame(expected, wrapped.InnerException);
            }
            else
            {
                Assert.AreSame(expected, Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Maps.MapLoaded(client)));
            }
            Assert.AreNotEqual(ClientState.Disconnected, client.State);
            Assert.IsFalse(client.Player.Disconected);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ProgressionPacketsKeepUnrelatedApplicationErrorsObservable(bool experience)
        {
            using var fixture = new BoundaryContext();
            using var database = new ProgressionDatabase();
            var client = fixture.CreateActiveClient(2);
            database.Seed(client);
            fixture.Install(new ManifestationManager(database, BoundaryContext.Disconnect));
            fixture.RegisterChatCommands();
            var expected = new InvalidOperationException("Fixture progression application failure.");
            database.BeforeSave = _ => throw expected;
            WorldTestContext.Drain(client);
            IOpcodedPacket<GameOpcode> packet = experience
                ? new RadialChatPacket { TextMsg = ".givexp 10500" }
                : new AllocateAttributePointsPacket { Body = 1 };

            var wrapped = Assert.ThrowsExactly<TargetInvocationException>(() => fixture.Route(client, packet));

            Assert.AreSame(expected, wrapped.InnerException);
            Assert.AreEqual(ClientState.Ingame, client.State);
            Assert.IsTrue(CellManager.Instance.IsInWorld(client));
            Assert.IsFalse(client.Player.Disconected);
            Assert.AreEqual(0, client.Player.SpentBody);
            Assert.AreEqual(0U, client.Player.Experience);
            Assert.IsFalse(WorldTestContext.Drain(client).Any(IsProgressionSuccess));
        }

        [TestMethod]
        public void ProgressionRejectionDoesNotSwallowDisconnectApplicationFailures()
        {
            using var fixture = new BoundaryContext();
            var client = fixture.CreateLoadingClient(51);
            var expected = new InvalidOperationException("Fixture disconnect application failure.");
            fixture.Install(new ManifestationManager(fixture.Factory, _ => throw expected));

            Assert.AreSame(expected, Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Maps.MapLoaded(client)));
            Assert.IsFalse(CellManager.Instance.IsInWorld(client));
            Assert.AreEqual(0, fixture.InventoryReads);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
        }

        [TestMethod]
        [DataRow((byte)0, false)]
        [DataRow((byte)51, false)]
        [DataRow(byte.MaxValue, false)]
        [DataRow((byte)0, true)]
        [DataRow((byte)51, true)]
        [DataRow(byte.MaxValue, true)]
        public void ProgressionPacketsRejectInvalidLevelAndThenHandleHealthyClient(byte level, bool experience)
        {
            using var fixture = new BoundaryContext();
            using var database = new ProgressionDatabase();
            var invalid = fixture.CreateActiveClient(level);
            var healthy = fixture.CreateActiveClient(experience ? (byte)1 : (byte)2);
            database.Seed(invalid);
            database.Seed(healthy);
            // Inserts use the schema's default for zero; explicitly persist corrupt level zero.
            if (level == 0)
            {
                using var unit = database.CreateChar();
                unit.Characters.UpdateCharacterProgression(invalid.Player.Id, 0, level);
            }
            var opens = database.OpenAttempts;
            var saves = database.SaveAttempts;
            fixture.Install(new ManifestationManager(database, BoundaryContext.Disconnect));
            fixture.RegisterChatCommands();
            WorldTestContext.Drain(invalid);
            WorldTestContext.Drain(healthy);

            IOpcodedPacket<GameOpcode> packet = experience
                ? new RadialChatPacket { TextMsg = ".givexp 3000" }
                : new AllocateAttributePointsPacket { Body = 1 };
            fixture.Route(invalid, packet);

            fixture.AssertRejected(invalid);
            Assert.AreEqual(level, invalid.Player.Level);
            Assert.AreEqual(0, invalid.Player.SpentBody);
            Assert.AreEqual(0U, invalid.Player.Experience);
            Assert.IsTrue(invalid.Player.Attributes.Values.All(attribute => attribute.CurrentMax == 0));
            Assert.AreEqual(opens, database.OpenAttempts);
            Assert.AreEqual(saves, database.SaveAttempts);
            Assert.AreEqual(level, database.Read(invalid.Player.Id).Level);
            Assert.IsFalse(WorldTestContext.Drain(invalid).Any(IsProgressionSuccess));
            Assert.IsFalse(WorldTestContext.Drain(healthy).Any(IsProgressionSuccess));

            fixture.Route(healthy, packet);

            Assert.AreEqual(ClientState.Ingame, healthy.State);
            Assert.IsTrue(CellManager.Instance.IsInWorld(healthy));
            Assert.AreEqual(saves + 1, database.SaveAttempts);
            var saved = database.Read(healthy.Player.Id);
            Assert.AreEqual(experience ? 3000U : 0U, saved.Experience);
            Assert.AreEqual(experience ? 0 : 1, saved.Body);
            Assert.IsTrue(WorldTestContext.Drain(healthy).Any(IsProgressionSuccess));
        }

        private static bool IsProgressionSuccess(ProtocolPacket packet)
        {
            return packet.Message is CallMethodMessage message &&
                (message.MethodId == GameOpcode.AttributeInfo ||
                 message.MethodId == GameOpcode.AvailableAllocationPoints ||
                 message.MethodId == GameOpcode.ExperienceChanged ||
                 message.MethodId == GameOpcode.LevelUp);
        }

        [TestMethod]
        [DataRow((byte)0)]
        [DataRow((byte)51)]
        [DataRow(byte.MaxValue)]
        public void ArmorPacketRejectsInvalidLevelBeforeInventoryOrAppearanceChanges(byte level)
        {
            using var fixture = new BoundaryContext();
            var invalid = fixture.CreateActiveClient(level);
            var healthy = fixture.CreateActiveClient(1);
            var armor = fixture.EquipArmor(invalid);
            fixture.EquipArmor(healthy);
            var inventory = invalid.Player.Inventory;
            var appearance = invalid.Player.AppearanceData[(EquipmentData)1].Class;
            WorldTestContext.Drain(invalid);
            WorldTestContext.Drain(healthy);
            var request = new RequestEquipArmorPacket { SrcInventory = InventoryType.Personal, SrcSlot = 0, DestSlot = 1 };

            fixture.Route(invalid, request);

            fixture.AssertRejected(invalid);
            Assert.AreEqual(armor.EntityId, inventory.EquippedInventory[1]);
            Assert.AreEqual(0UL, inventory.PersonalInventory[0]);
            Assert.AreEqual(appearance, invalid.Player.AppearanceData[(EquipmentData)1].Class);
            Assert.AreEqual(0, fixture.InventoryWrites);
            Assert.AreEqual(0, fixture.AppearanceWrites);
            Assert.IsFalse(WorldTestContext.Drain(invalid).Any(packet =>
                packet.Message is CallMethodMessage message &&
                message.MethodId != GameOpcode.DestroyPhysicalEntity));

            fixture.Route(healthy, request);

            Assert.AreEqual(ClientState.Ingame, healthy.State);
            Assert.AreEqual(0UL, healthy.Player.Inventory.EquippedInventory[1]);
            Assert.AreEqual(1, fixture.InventoryWrites);
            Assert.AreEqual(1, fixture.AppearanceWrites);
            Assert.IsTrue(WorldTestContext.Drain(healthy).Any(IsProgressionSuccess));
        }

        [TestMethod]
        [DataRow((byte)0)]
        [DataRow((byte)51)]
        [DataRow(byte.MaxValue)]
        public void CreatureRewardStopsAfterRejectingInvalidKillerAndNextKillStillWorks(byte level)
        {
            using var fixture = new BoundaryContext();
            using var database = new ProgressionDatabase();
            var invalid = fixture.CreateActiveClient(level);
            var healthy = fixture.CreateActiveClient(1);
            database.Seed(healthy);
            var progression = new ManifestationManager(database, BoundaryContext.Disconnect);
            var creatures = new CreatureManager(null, progression);
            var first = fixture.AddCreature();
            var second = fixture.AddCreature();
            WorldTestContext.Drain(invalid);
            WorldTestContext.Drain(healthy);

            creatures.HandleCreatureKill(fixture.Progression.World.Map, first, invalid.Player);
            creatures.HandleCreatureKill(fixture.Progression.World.Map, first, invalid.Player);

            fixture.AssertRejected(invalid);
            Assert.AreEqual(CharacterState.Dead, first.State);
            Assert.AreEqual(0, first.SpawnPool.AliveCreatures);
            Assert.AreEqual(1, first.SpawnPool.DeadCreatures);
            Assert.AreEqual(0, fixture.Progression.World.Map.LootDispensers.Count);
            Assert.AreEqual(0, database.SaveAttempts);
            Assert.IsFalse(WorldTestContext.Drain(invalid).Any(IsProgressionSuccess));

            creatures.HandleCreatureKill(fixture.Progression.World.Map, second, healthy.Player);

            Assert.AreEqual(1, database.SaveAttempts);
            Assert.AreEqual(1, fixture.Progression.World.Map.LootDispensers.Count);
            Assert.IsTrue(healthy.Player.Experience >= 90 && healthy.Player.Experience <= 110);
            Assert.AreEqual(healthy.Player.Experience, database.Read(healthy.Player.Id).Experience);
        }

        private sealed class BoundaryContext : IDisposable
        {
            private readonly List<(FieldInfo Field, object Previous)> _singletons = new();
            private readonly HashSet<int> _channelSeeds = CommunicatorManager.ChannelsBySeed.Keys.ToHashSet();
            private readonly PacketRouter<ClientPacketHandler, GameOpcode> _router = new();
            private readonly ICharacterInventoryRepository _inventory;
            private readonly ICharacterAppearanceRepository _appearance;
            private readonly List<Item> _items = new();
            private readonly List<Creature> _creatures = new();
            private Dictionary<string, Action<string[]>> _commands;
            private KeyValuePair<string, Action<string[]>>[] _previousCommands;
            internal ProgressionTestContext Progression { get; } = new();
            internal MapChannelManager Maps { get; }
            internal IGameUnitOfWorkFactory Factory { get; }
            internal int InventoryReads => ((StrictProxy)(object)_inventory).Calls.Count(call => call == "GetItems");
            internal int InventoryWrites => ((StrictProxy)(object)_inventory).Calls.Count(call => call == "MoveInvItem");
            internal int AppearanceWrites => ((StrictProxy)(object)_appearance).Calls.Count(call => call == "AddOrUpdate");

            internal BoundaryContext(Exception inventoryFailure = null)
            {
                _inventory = StrictProxy.Create<ICharacterInventoryRepository>(new Dictionary<string, object>
                {
                    ["GetItems"] = (object)inventoryFailure ?? new List<CharacterInventoryEntry>()
                }, "MoveInvItem");
                _appearance = StrictProxy.Create<ICharacterAppearanceRepository>(new Dictionary<string, object>(), "AddOrUpdate");
                var options = StrictProxy.Create<ICharacterOptionRepository>(new Dictionary<string, object>
                {
                    ["Get"] = new List<CharacterOptionEntry>()
                });
                var friends = StrictProxy.Create<IFriendRepository>(new Dictionary<string, object>
                {
                    ["GetFriends"] = new List<uint>()
                });
                var ignored = StrictProxy.Create<IIgnoredRepository>(new Dictionary<string, object>
                {
                    ["GetIgnored"] = new List<uint>()
                });
                var clans = StrictProxy.Create<IClanRepository>(new Dictionary<string, object>
                {
                    ["GetClanByCharacterId"] = null
                });
                var unit = StrictProxy.Create<ICharUnitOfWork>(new Dictionary<string, object>
                {
                    ["get_CharacterInventories"] = _inventory,
                    ["get_CharacterAppearances"] = _appearance,
                    ["get_CharacterOptions"] = options,
                    ["get_Friends"] = friends,
                    ["get_Ignoreds"] = ignored,
                    ["get_Clans"] = clans
                }, "Dispose", "Complete");
                Factory = StrictProxy.Create<IGameUnitOfWorkFactory>(new Dictionary<string, object>
                {
                    ["CreateChar"] = unit
                });
                Maps = new MapChannelManager(Factory);
                Maps.MapChannelArray.Add(Progression.World.Map.MapInfo.MapContextId, Progression.World.Map);
                Install(Maps);
                Install(new ManifestationManager(Factory, Disconnect));
                InstallManager<InventoryManager>();
                InstallManager<SocialManager>();
                InstallManager<ClanManager>();
            }

            internal Client CreateLoadingClient(byte level)
            {
                var client = CreateClient(level);
                client.State = ClientState.Loading;
                client.LoadingMap = Progression.World.Map.MapInfo.MapContextId;
                Progression.World.Map.ClientList.Remove(client);
                Progression.World.Map.QueuedClients.Enqueue(client);
                Progression.World.Map.QueuedClients.Enqueue(client);
                EntityManager.Instance.UnregisterEntity(client.Player.EntityId);
                EntityManager.Instance.UnregisterPlayer(client.Player.EntityId);
                EntityManager.Instance.UnregisterActor(client.Player.EntityId);
                return client;
            }

            internal Client CreateActiveClient(byte level)
            {
                var client = CreateClient(level);
                CellManager.Instance.AddToWorld(client);
                return client;
            }

            private Client CreateClient(byte level)
            {
                var client = Progression.CreateClient(level);
                client.Player.State = CharacterState.Normal;
                typeof(Client).GetProperty(nameof(Client.AccountEntry)).SetValue(client, new GameAccountEntry
                {
                    Id = client.Player.Id,
                    FamilyName = client.Player.FamilyName,
                    Level = 1
                });
                return client;
            }

            internal void RegisterChatCommands()
            {
                _commands = (Dictionary<string, Action<string[]>>)typeof(ChatCommandsManager)
                    .GetField("Commands", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                _previousCommands = _commands.ToArray();
                _commands.Clear();
                ChatCommandsManager.Instance.RegisterChatCommands();
            }

            internal Item EquipArmor(Client client)
            {
                var entityClass = (EntityClasses)900051;
                Progression.World.AddClass(entityClass);
                EntityClassManager.Instance.LoadedEntityClasses[entityClass].EquipableClassInfo =
                    new EquipableClassInfo((EquipmentData)1);
                var item = new Item
                {
                    OwnerId = client.Player.Id,
                    ItemTemplate = new ItemTemplate(new ItemTemplateItemClassEntry { ItemClass = (uint)entityClass })
                };
                _items.Add(item);
                EntityManager.Instance.RegisterEntity(item.EntityId, EntityType.Item);
                EntityManager.Instance.RegisterItem(item.EntityId, item);
                client.Player.Inventory.PersonalInventory = Enumerable.Repeat(0UL, 50).ToList();
                client.Player.Inventory.EquippedInventory[1] = item.EntityId;
                client.Player.AppearanceData[(EquipmentData)1] = new AppearanceData
                {
                    SlotId = (EquipmentData)1,
                    Class = (uint)entityClass
                };
                return item;
            }

            internal Creature AddCreature()
            {
                var creature = new Creature
                {
                    EntityClass = EntityClasses.HumanBaseMale,
                    MapContextId = Progression.World.Map.MapInfo.MapContextId,
                    Level = 1,
                    State = CharacterState.Idle,
                    AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                    SpawnPool = new SpawnPool { AliveCreatures = 1 }
                };
                _creatures.Add(creature);
                CellManager.Instance.AddToWorld(Progression.World.Map, creature);
                return creature;
            }

            internal void Route(Client client, IOpcodedPacket<GameOpcode> packet)
            {
                var handler = new ClientPacketHandler();
                handler.RegisterClient(client);
                _router.RoutePacket(handler, packet);
            }

            internal void AssertRejected(Client client)
            {
                Assert.AreEqual(ClientState.Disconnected, client.State);
                Assert.IsTrue(client.Player.Disconected);
                Assert.IsFalse(CellManager.Instance.IsInWorld(client));
                Assert.IsNull(client.Player.MapChannel);
                Assert.IsFalse(EntityManager.Instance.Players.ContainsKey(client.Player.EntityId));
                Assert.IsFalse(EntityManager.Instance.Actors.ContainsKey(client.Player.EntityId));
                Assert.IsFalse(EntityManager.Instance.RegisteredEntities.ContainsKey(client.Player.EntityId));
                foreach (var map in Maps.MapChannelArray.Values)
                {
                    Assert.IsFalse(map.ClientList.Contains(client));
                    Assert.IsFalse(map.QueuedClients.Contains(client));
                    Assert.IsFalse(map.MapCellInfo.Cells.Values.Any(cell => cell.ClientList.Contains(client)));
                }
            }

            internal static void Disconnect(Client client)
            {
                client.State = ClientState.Disconnected;
                client.RestoreTransferOrigin();
                client.Player.Disconected = true;
                client.Player.RemoveFromMap = true;
            }

            private void InstallManager<T>() where T : class
            {
                Install((T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { Factory }, null));
            }

            internal void Install<T>(T manager)
            {
                var field = typeof(T).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
                _singletons.Add((field, field.GetValue(null)));
                field.SetValue(null, manager);
            }

            public void Dispose()
            {
                if (_commands != null)
                {
                    _commands.Clear();
                    foreach (var command in _previousCommands)
                        _commands.Add(command.Key, command.Value);
                }
                foreach (var ship in DynamicObjectManager.Instance.Dropships.Values
                    .Where(ship => Maps.MapChannelArray.ContainsKey(ship.MapContextId)).ToArray())
                {
                    CellManager.Instance.RemoveFromWorld(Maps.MapChannelArray[ship.MapContextId], ship);
                    DynamicObjectManager.Instance.Dropships.Remove(ship.EntityId);
                }
                foreach (var seed in CommunicatorManager.ChannelsBySeed.Keys.Except(_channelSeeds).ToArray())
                    CommunicatorManager.ChannelsBySeed.Remove(seed);
                foreach (var creature in _creatures)
                    CellManager.Instance.RemoveCreatureFromWorld(Progression.World.Map, creature);
                foreach (var item in _items)
                {
                    EntityManager.Instance.UnregisterItem(item.EntityId);
                    EntityManager.Instance.UnregisterEntity(item.EntityId);
                    EntityManager.Instance.FreeEntity(item.EntityId);
                }
                Progression.Dispose();
                for (var i = _singletons.Count - 1; i >= 0; i--)
                    _singletons[i].Field.SetValue(null, _singletons[i].Previous);
            }
        }
    }
}
