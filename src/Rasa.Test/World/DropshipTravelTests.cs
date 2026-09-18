extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.World
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class DropshipTravelTests
    {
        [TestMethod]
        public void BoardingUsesDestinationMetadataAndDepartsFromTheSourceMap()
        {
            using var world = new WorldTestContext();
            world.AddClass(EntityClasses.UsableCrSpawnerHumDropshipV01);
            var client = world.CreateClient();
            CellManager.Instance.AddToWorld(client);
            var destination = CreateDestination();
            var maps = new MapChannelManager(null);
            maps.MapChannelArray.Add(1220, world.Map);
            maps.MapChannelArray.Add(1300, destination);
            var saved = 0;
            var manager = new DynamicObjectManager(null, maps, () => 1000, (_, update, _) =>
            {
                if (update == CharacterUpdate.Position)
                    saved++;
            });
            WaypointTravelTests.AddWaypoint(manager, world.Map, 10, Vector3.Zero, type: WaypointType.Dropship);
            WaypointTravelTests.AddWaypoint(manager, destination, 20, new Vector3(400, 5, 0), type: WaypointType.Dropship);
            client.Player.GainedWaypoints.Add(new CharacterTeleporterEntry(client.Player.Id, 20, (byte)WaypointType.Dropship));

            manager.SelectWaypoint(client, new SelectWaypointPacket { MapInstanceId = 1, WaypointId = 20 });

            Assert.IsNotNull(client.PendingTransfer);
            Assert.IsTrue(client.PendingTransfer.IsDropship);
            Assert.AreEqual(1300U, client.LoadingMap);
            Assert.IsFalse(client.HandleMovement(new Rasa.Models.Movement(Vector3.One, Vector2.Zero)));
            var ship = manager.Dropships[client.PendingTransfer.DropshipId];
            try
            {
                for (var step = 0; step < 6; step++)
                    manager.DropshipsWorker(world.Map, Math.Max(0, ship.PhaseTimeleft));

                Assert.IsTrue(client.PendingTransfer.HasDeparted);
                Assert.AreSame(destination, client.Player.MapChannel);
                Assert.AreEqual(1300U, client.Player.MapContextId);
                Assert.AreEqual(new Vector3(400, 5, 0), client.Player.Position);
                Assert.AreEqual(ClientState.Teleporting, client.State);
                Assert.IsFalse(world.Map.ClientList.Contains(client));
                Assert.IsFalse(manager.Dropships.ContainsKey(ship.EntityId));
                Assert.IsTrue(manager.IsExpectedMapLoad(client));
                Assert.AreEqual(0, saved);

                destination.ClientList.Add(client);
                CellManager.Instance.AddToWorld(client);
                manager.CompleteMapLoadTransfer(client);
                Assert.IsNull(client.PendingTransfer);
                Assert.AreEqual(1, saved);
                var arrival = new Dropship(Factions.AFS, DropshipType.Teleporter, client);
                CellManager.Instance.AddToWorld(destination, arrival);
                manager.Dropships.Add(arrival.EntityId, arrival);
                for (var step = 0; step < 6; step++)
                    manager.DropshipsWorker(destination, Math.Max(0, arrival.PhaseTimeleft));
                Assert.AreEqual(ClientState.Ingame, client.State);
                Assert.AreEqual(1, destination.ClientList.Count);
                Assert.IsFalse(manager.IsExpectedMapLoad(client));
            }
            finally
            {
                foreach (var remaining in new List<Dropship>(manager.Dropships.Values))
                {
                    var map = remaining.MapContextId == 1220 ? world.Map : destination;
                    CellManager.Instance.RemoveFromWorld(map, remaining);
                    manager.Dropships.Remove(remaining.EntityId);
                }
                destination.ClientList.Clear();
                destination.MapCellInfo.Cells.Clear();
            }
        }

        [TestMethod]
        public void AMapTickDoesNotAdvanceAnotherMapsDropship()
        {
            using var world = new WorldTestContext();
            var client = world.CreateClient();
            var ship = new Dropship(Factions.AFS, DropshipType.Teleporter, client);
            var manager = new DynamicObjectManager(null);
            manager.Dropships.Add(ship.EntityId, ship);
            try
            {
                manager.DropshipsWorker(CreateDestination(), 1000);

                Assert.AreEqual(5000L, ship.PhaseTimeleft);
                Assert.AreEqual((byte)0, ship.Phase);
            }
            finally
            {
                manager.Dropships.Remove(ship.EntityId);
                EntityManager.Instance.FreeEntity(ship.EntityId);
            }
        }

        internal static MapChannel CreateDestination()
        {
            return new MapChannel
            {
                MapInfo = new MapInfo(1300, "fixture-destination", 1, 0),
                ClientList = new List<Rasa.Game.Client>(),
                PlayerLimit = 128
            };
        }
    }
}
