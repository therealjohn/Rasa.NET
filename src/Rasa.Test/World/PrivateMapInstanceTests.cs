extern alias RasaGame;

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.World
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Game.Handlers;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Structures;

    [TestClass]
    [DoNotParallelize]
    public class PrivateMapInstanceTests
    {
        [TestMethod]
        public void GetOrCreateKeepsPublicLookupCompatibleAndStaysStablePerOwner()
        {
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, privateInstances: service);
            var publicMap = CreateMap();
            maps.MapChannelArray.Add(publicMap.MapInfo.MapContextId, publicMap);

            var ownerOne = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);
            var ownerOneAgain = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);
            var ownerTwo = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 8);

            Assert.AreSame(ownerOne, ownerOneAgain);
            Assert.AreSame(publicMap, maps.FindByContextId(publicMap.MapInfo.MapContextId));
            Assert.AreEqual(1U, publicMap.InstanceId);
            Assert.AreNotSame(ownerOne, ownerTwo);
            Assert.IsTrue(ownerOne.IsPrivateInstance);
            Assert.AreEqual(7U, ownerOne.OwnerCharacterId);
            Assert.AreSame(ownerOne,
                maps.FindByContextAndInstance(publicMap.MapInfo.MapContextId, ownerOne.InstanceId));
            Assert.AreSame(ownerTwo,
                maps.FindOwnedPrivateInstance(publicMap.MapInfo.MapContextId, 8));
        }

        [TestMethod]
        public void MapChannelWorkerTicksEveryPrivateInstanceExactlyOnce()
        {
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, privateInstances: service);
            var publicMap = CreateMap();
            maps.MapChannelArray.Add(publicMap.MapInfo.MapContextId, publicMap);
            var first = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);
            var second = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 8);

            maps.MapChannelWorker(100);

            Assert.AreEqual(100L, publicMap.MapChannelElapsed);
            Assert.AreEqual(100L, first.MapChannelElapsed);
            Assert.AreEqual(100L, second.MapChannelElapsed);
        }

        [TestMethod]
        public void MapLoadedCommitsTransferToTheExactPrivateDestinationInstance()
        {
            using var world = new WorldTestContext();
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, updateCharacter: (_, _, _) => { },
                disconnect: _ => Assert.Fail("Transfer should not disconnect."),
                refreshStats: (_, _) => { }, assignPlayer: _ => { }, enterMapChannels: _ => { },
                privateInstances: service);
            maps.MapChannelArray.Add(world.Map.MapInfo.MapContextId, world.Map);
            var client = world.CreateClient();
            var destination = maps.GetOrCreatePrivateInstance(world.Map.MapInfo.MapContextId, client.Player.Id);
            var objects = new DynamicObjectManager(null, maps, updateCharacter: (_, _, _) => { },
                disconnect: current => current.State = ClientState.Disconnected);
            using var singletons = new ManagerInstances(objects);

            client.PendingTransfer = new PlayerTransfer
            {
                OriginMap = world.Map,
                OriginPosition = Vector3.Zero,
                OriginRotation = 0,
                DestinationMap = destination,
                DestinationPosition = new Vector3(250, 5, 0),
                DestinationRotation = 1,
                Deadline = long.MaxValue,
                IsDropship = true,
                HasDeparted = true
            };
            client.State = ClientState.Teleporting;
            client.Player.MapChannel = destination;
            client.Player.MapContextId = destination.MapInfo.MapContextId;
            client.SetWorldPosition(new Vector3(250, 5, 0), 1);
            client.LoadingMap = destination.MapInfo.MapContextId;
            client.AwaitingMapLoaded = true;
            world.Map.ClientList.Remove(client);

            maps.MapLoaded(client);

            Assert.AreSame(destination, client.Player.MapChannel);
            Assert.IsTrue(destination.ClientList.Contains(client));
            Assert.IsFalse(world.Map.ClientList.Contains(client));
            Assert.IsNull(client.PendingTransfer);
            Assert.AreEqual(ClientState.Teleporting, client.State);
        }

        [TestMethod]
        public void CrossInstanceBroadcastsUseExactMapIdentity()
        {
            using var world = new WorldTestContext();
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, privateInstances: service);
            maps.MapChannelArray.Add(world.Map.MapInfo.MapContextId, world.Map);
            var first = maps.GetOrCreatePrivateInstance(world.Map.MapInfo.MapContextId, 7);
            var second = maps.GetOrCreatePrivateInstance(world.Map.MapInfo.MapContextId, 8);
            var firstClient = CreateClient(first, 7);
            var secondClient = CreateClient(second, 8);
            CellManager.Instance.AddToWorld(firstClient);
            CellManager.Instance.AddToWorld(secondClient);
            var creature = new Creature
            {
                EntityClass = EntityClasses.HumanBaseMale,
                MapContextId = first.MapInfo.MapContextId,
                Position = Vector3.Zero,
                State = CharacterState.Idle,
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                Attributes = new Dictionary<Attributes, ActorAttributes>
                {
                    [Attributes.Health] = new(Attributes.Health, 100, 100, 50, 0, 0)
                }
            };
            CellManager.Instance.AddToWorld(first, creature);
            WorldTestContext.Drain(firstClient);
            WorldTestContext.Drain(secondClient);

            ActorManager.Instance.Heal(creature, 10);

            Assert.IsTrue(WorldTestContext.Drain(firstClient).Select(packet => packet.Message)
                .OfType<CallMethodMessage>()
                .Any(message => message.Packet is UpdateHealthPacket));
            Assert.AreEqual(0, WorldTestContext.Drain(secondClient).Count);

            CellManager.Instance.RemoveCreatureFromWorld(first, creature);
            CleanupClient(firstClient);
            CleanupClient(secondClient);
        }

        [TestMethod]
        public void CrossInstanceUseAndRecoveryRequireExactPrivateMapIdentity()
        {
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, privateInstances: service);
            var publicMap = CreateMap(1985);
            maps.MapChannelArray.Add(publicMap.MapInfo.MapContextId, publicMap);
            var first = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);
            var second = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 8);
            var firstClient = CreateClient(first, 7);
            var secondClient = CreateClient(second, 8);
            var controlPoint = new DynamicObject
            {
                MapContextId = second.MapInfo.MapContextId,
                Position = Vector3.Zero,
                DynamicObjectType = DynamicObjectType.ControlPoint,
                Faction = Factions.AFS,
                StateId = UseObjectState.CpointStateFactionAOwned
            };
            second.ControlPoints.Add(1, controlPoint);
            CellManager.Instance.AddToWorld(second, controlPoint);
            controlPoint.IsInWorld = true;
            var objects = new DynamicObjectManager(null, maps, updateCharacter: (_, _, _) => { },
                disconnect: _ => Assert.Fail("Cross-instance use should be ignored."));
            using var singletons = new ManagerInstances(objects);

            objects.RequestUseObjectPacket(firstClient, new RequestUseObjectPacket
            {
                ActionId = ActionId.UseObject,
                ActionArgId = DynamicObjectManager.ControlPointUseArgId,
                EntityId = controlPoint.EntityId
            });

            Assert.AreEqual(0, controlPoint.TriggeredByPlayers.Count);
            Assert.AreEqual(0, WorldTestContext.Drain(firstClient).Count);

            controlPoint.TriggeredByPlayers.Add(firstClient);
            objects.CaptureControlPointRecovery(second, new ActionData(
                firstClient.Player,
                ActionId.UseObject,
                DynamicObjectManager.ControlPointUseArgId,
                0));

            Assert.AreEqual(Factions.AFS, controlPoint.Faction);
            Assert.AreEqual(UseObjectState.CpointStateFactionAOwned, controlPoint.StateId);
            Assert.AreEqual(0, controlPoint.TriggeredByPlayers.Count);

            CellManager.Instance.RemoveFromWorld(second, controlPoint);
            CleanupClient(firstClient);
            CleanupClient(secondClient);
        }

        [TestMethod]
        public void CrossInstanceDamageLootAndDespawnCannotReachOtherPrivateMaps()
        {
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, privateInstances: service);
            var publicMap = CreateMap(1985);
            maps.MapChannelArray.Add(publicMap.MapInfo.MapContextId, publicMap);
            var first = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);
            var second = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 8);
            var firstClient = CreateClient(first, 7);
            var target = new Creature
            {
                EntityClass = EntityClasses.HumanBaseMale,
                MapContextId = second.MapInfo.MapContextId,
                Position = Vector3.Zero,
                State = CharacterState.Normal,
                Faction = Factions.Bane,
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                Attributes = new Dictionary<Attributes, ActorAttributes>
                {
                    [Attributes.Health] = new(Attributes.Health, 100, 100, 100, 0, 0),
                    [Attributes.Armor] = new(Attributes.Armor, 0, 0, 0, 0, 0)
                }
            };
            CellManager.Instance.AddToWorld(second, target);

            MissileManager.Instance.MissileLaunch(first, new ActionData(
                firstClient.Player,
                ActionId.WeaponAttack,
                133,
                target.EntityId,
                0), 55);

            Assert.AreEqual(0, first.QueuedMissiles.Count);
            Assert.AreEqual(100, target.Attributes[Attributes.Health].Current);

            var loot = new LootDispenser
            {
                Owner = firstClient.Player.EntityId,
                OwnerClient = firstClient,
                Player = firstClient.Player,
                Map = second,
                Corpse = target,
                CharacterId = firstClient.Player.Id,
                IsLootable = true
            };
            target.State = CharacterState.Dead;
            target.Attributes[Attributes.Health].Current = 0;
            target.CorpseLootEntityId = loot.EntityId;
            second.LootDispensers.Add(loot.EntityId, loot);
            var looting = new LootDispenserManager(null, _ => 20);

            looting.RequestCorpseLooting(firstClient,
                new RequestCorpseLootingPacket { EntityId = loot.EntityId });

            Assert.AreEqual(0UL, loot.CurrentLooter);
            Assert.AreEqual(0, WorldTestContext.Drain(firstClient).Count);

            Assert.IsFalse(CellManager.Instance.RemoveCreatureFromWorld(first, target));
            Assert.IsTrue(EntityManager.Instance.Creatures.ContainsKey(target.EntityId));
            Assert.AreSame(second, target.RuntimeMapChannel);

            second.LootDispensers.Remove(loot.EntityId);
            target.CorpseLootEntityId = 0;
            CellManager.Instance.RemoveCreatureFromWorld(second, target);
            CleanupClient(firstClient);
        }

        [TestMethod]
        public void ReleasingOwnedInstanceUnregistersOwnedEntitiesQueuesAndMissilesAndReconnectCreatesANewRuntime()
        {
            var service = new PrivateMapInstanceService();
            var maps = new MapChannelManager(null, privateInstances: service);
            var publicMap = CreateMap(1985);
            maps.MapChannelArray.Add(publicMap.MapInfo.MapContextId, publicMap);
            var owned = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);
            var loadingClient = CreateClient(owned, 7);
            loadingClient.State = ClientState.Loading;
            owned.ClientList.Add(loadingClient);
            owned.QueuedClients.Enqueue(loadingClient);
            owned.PerformRecovery.Add(new ActionData(loadingClient.Player, ActionId.UseObject, 0, 0));
            owned.QueuedMissiles.Add(new Missile());
            var creature = new Creature
            {
                EntityClass = EntityClasses.HumanBaseMale,
                MapContextId = owned.MapInfo.MapContextId,
                Position = Vector3.Zero,
                State = CharacterState.Idle,
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                Attributes = new Dictionary<Attributes, ActorAttributes>
                {
                    [Attributes.Health] = new(Attributes.Health, 100, 100, 100, 0, 0)
                }
            };
            var dynamicObject = new DynamicObject
            {
                MapContextId = owned.MapInfo.MapContextId,
                Position = Vector3.Zero
            };
            CellManager.Instance.AddToWorld(owned, creature);
            CellManager.Instance.AddToWorld(owned, dynamicObject);

            maps.ReleaseOwnedPrivateInstances(7);

            Assert.IsNull(maps.FindOwnedPrivateInstance(publicMap.MapInfo.MapContextId, 7));
            Assert.IsNull(maps.FindByContextAndInstance(publicMap.MapInfo.MapContextId, owned.InstanceId));
            Assert.AreEqual(0, owned.ClientList.Count);
            Assert.AreEqual(0, owned.QueuedClients.Count);
            Assert.AreEqual(0, owned.PerformRecovery.Count);
            Assert.AreEqual(0, owned.QueuedMissiles.Count);
            Assert.AreEqual(0, owned.MapCellInfo.Cells.Count);
            Assert.IsFalse(EntityManager.Instance.RegisteredEntities.ContainsKey(creature.EntityId));
            Assert.IsFalse(EntityManager.Instance.Creatures.ContainsKey(creature.EntityId));
            Assert.IsFalse(EntityManager.Instance.RegisteredEntities.ContainsKey(dynamicObject.EntityId));
            Assert.IsFalse(EntityManager.Instance.DynamicObjects.ContainsKey(dynamicObject.EntityId));

            var recreated = maps.GetOrCreatePrivateInstance(publicMap.MapInfo.MapContextId, 7);

            Assert.AreNotSame(owned, recreated);
            Assert.AreEqual(publicMap.MapInfo.MapContextId, recreated.MapInfo.MapContextId);
            Assert.AreEqual(7U, recreated.OwnerCharacterId);
            CleanupClient(loadingClient);
        }

        private static MapChannel CreateMap(uint contextId = 1985) => new()
        {
            MapInfo = new MapInfo(contextId, "fixture", 1556, 0),
            ClientList = new List<Client>(),
            PlayerLimit = 128
        };

        private static Client CreateClient(MapChannel map, uint characterId)
        {
            var client = new Client(null, new ClientPacketHandler())
            {
                State = ClientState.Ingame
            };
            client.Player.Id = characterId;
            client.Player.EntityId = EntityManager.Instance.AllocateUnrecycledEntityId();
            client.Player.Name = $"Player{characterId}";
            client.Player.FamilyName = "Fixture";
            client.Player.Level = 1;
            client.Player.EntityClass = EntityClasses.HumanBaseMale;
            client.Player.AppearanceData = new Dictionary<EquipmentData, AppearanceData>();
            client.Player.Inventory.EquippedInventory = Enumerable.Repeat(0UL, 17).ToList();
            client.Player.MapChannel = map;
            client.Player.MapContextId = map.MapInfo.MapContextId;
            client.Player.Position = Vector3.Zero;
            EntityManager.Instance.RegisterEntity(client.Player.EntityId, EntityType.Character);
            EntityManager.Instance.RegisterPlayer(client.Player.EntityId, client.Player);
            EntityManager.Instance.RegisterActor(client.Player.EntityId, client.Player);
            return client;
        }

        private static void CleanupClient(Client client)
        {
            if (EntityManager.Instance.RegisteredEntities.ContainsKey(client.Player.EntityId))
                EntityManager.Instance.UnregisterEntity(client.Player.EntityId);
            if (EntityManager.Instance.Players.ContainsKey(client.Player.EntityId))
                EntityManager.Instance.UnregisterPlayer(client.Player.EntityId);
            if (EntityManager.Instance.Actors.ContainsKey(client.Player.EntityId))
                EntityManager.Instance.UnregisterActor(client.Player.EntityId);
            EntityManager.Instance.FreeEntity(client.Player.EntityId);
        }

        private sealed class ManagerInstances : System.IDisposable
        {
            private readonly FieldInfo _objectsField = typeof(DynamicObjectManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object _previousObjects;

            internal ManagerInstances(DynamicObjectManager objects)
            {
                _previousObjects = _objectsField.GetValue(null);
                _objectsField.SetValue(null, objects);
            }

            public void Dispose()
            {
                _objectsField.SetValue(null, _previousObjects);
            }
        }
    }
}
