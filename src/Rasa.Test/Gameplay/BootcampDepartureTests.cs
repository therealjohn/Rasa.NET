extern alias RasaGame;

using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Data;
    using Game;
    using Missions;
    using Packets.MapChannel.Client;
    using Repositories.Char.Character;
    using Repositories.Char.CharacterMission;
    using Repositories.Char.CharacterQualification;
    using Repositories.Char.CharacterStartingExperience;
    using Repositories.Char.GameAccount;
    using Structures;
    using Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class BootcampDepartureTests
    {
        [TestMethod]
        public void VanHandoffNoLongerTransfersOrGrantsDepartureUntilTheExitPadIsUsed()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = harness.AddNpc(510207, 2561);

            harness.SeedMission(1, 1994, (uint)MissionState.Completed, true);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Context.Client,
                youngblood.EntityId,
                1995));
            harness.Context.Drain();
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Context.Client,
                MissionProgressEvent.Area(1995, 435)));
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Context.Client,
                survivor.EntityId,
                1995,
                10,
                1));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Context.Client,
                MissionProgressEvent.Interaction(24990)));
            harness.UtcNow += TimeSpan.FromSeconds(5);
            Assert.IsFalse(harness.Manager.TickScenarios(harness.Context.Client));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Context.Client,
                MissionProgressEvent.Interaction(24911)));
            harness.UtcNow += TimeSpan.FromSeconds(5);
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Context.Client));
            harness.UtcNow += TimeSpan.FromSeconds(2);
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Context.Client));

            var van = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId);
            Assert.IsNotNull(van);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Context.Client,
                van.EntityId,
                1995,
                4,
                1));

            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.Ingame, harness.Context.Client.State);
            Assert.IsNull(harness.Context.Client.PendingTransfer);
            using var verify = harness.Context.CreateChar();
            Assert.IsFalse(verify.CharacterQualifications.HasQualification(
                harness.Context.Client.Player.Id,
                CharacterQualificationKey.BootcampComplete));
            Assert.IsFalse(verify.GameAccounts.Get(harness.Context.Client.AccountEntry.Id).CanSkipBootcamp);
        }

        [TestMethod]
        public void ExitPadDepartureCommitsParityTransfersToAliaAndReleasesThePrivateRuntime()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(61);
            var characterId = context.SeedCharacter(
                61,
                1,
                "Departure",
                mapContextId: BootcampSelectionTestContext.BootcampMapContextId,
                x: -255.3125,
                y: 101.05078,
                z: -70.4375,
                rotation: 0,
                experience: 49250,
                level: 5);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            context.SeedMission(characterId, BootcampSelectionTestContext.MissionFinale, MissionState.Active, completeable: true);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.ExitPadWaypointId, WaypointType.Dropship);
            var client = context.CreateSelectionClient(61);

            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new Packets.Game.Client.RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = false
                });
            context.MaterializeLoadedClient(client);

            var privateMap = context.Maps.FindOwnedPrivateInstance(
                BootcampSelectionTestContext.BootcampMapContextId,
                characterId);
            Assert.IsNotNull(privateMap);

            context.Objects.SelectWaypoint(
                client,
                new SelectWaypointPacket
                {
                    WaypointId = BootcampSelectionTestContext.ExitPadWaypointId,
                    MapInstanceId = privateMap.InstanceId
                });

            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.Teleporting, client.State);
            Assert.IsNotNull(client.PendingTransfer);
            Assert.AreEqual(
                BootcampSelectionTestContext.WildernessMapContextId,
                client.PendingTransfer.DestinationMap.MapInfo.MapContextId);

            context.CompletePendingMapLinkTransfer(client);

            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.Ingame, client.State);
            Assert.AreEqual(
                BootcampSelectionTestContext.WildernessMapContextId,
                client.Player.MapContextId);
            Assert.IsNull(context.Maps.FindOwnedPrivateInstance(
                BootcampSelectionTestContext.BootcampMapContextId,
                characterId));

            using var verify = context.OpenChar();
            var durableCharacter = new CharacterRepository(verify).Get(characterId);
            Assert.AreEqual(43000U, durableCharacter.Experience);
            Assert.AreEqual((byte)5, durableCharacter.Level);
            Assert.AreEqual(
                CharacterStartingExperienceState.Completed,
                new CharacterStartingExperienceRepository(verify).Get(characterId).State);
            Assert.IsTrue(new CharacterQualificationRepository(verify).HasQualification(
                characterId,
                CharacterQualificationKey.BootcampComplete));
            Assert.IsTrue(new GameAccountRepository(verify).Get(61).CanSkipBootcamp);

            var rogers = new Creature
            {
                DbId = 100,
                Npc = new Npc { NpcPackageId = 100 }
            };
            var status = context.Missions.ClassifyNpcConversation(client.Player, rogers);
            Assert.IsTrue(status.TryGetStatus(out var conversationStatus, out var missionIds));
            Assert.AreEqual(ConversationStatus.MissionComplete, conversationStatus);
            CollectionAssert.Contains(missionIds, BootcampSelectionTestContext.MissionFinale);
            Assert.AreEqual(
                MissionState.Active,
                client.Player.Missions[BootcampSelectionTestContext.MissionFinale].State);
            Assert.IsTrue(
                client.Player.Missions[BootcampSelectionTestContext.MissionFinale].Completeable);
        }

        [TestMethod]
        public void ExitPadRejectsTravelBeforeTheFinalHandoffBecomesCompleteable()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(62);
            var characterId = context.SeedCharacter(
                62,
                1,
                "Locked",
                mapContextId: BootcampSelectionTestContext.BootcampMapContextId,
                x: -255.3125,
                y: 101.05078,
                z: -70.4375);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            context.SeedMission(characterId, BootcampSelectionTestContext.MissionFinale, MissionState.Active, completeable: false);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.ExitPadWaypointId, WaypointType.Dropship);
            var client = context.CreateSelectionClient(62);

            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new Packets.Game.Client.RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });
            context.MaterializeLoadedClient(client);

            context.Objects.SelectWaypoint(
                client,
                new SelectWaypointPacket
                {
                    WaypointId = BootcampSelectionTestContext.ExitPadWaypointId,
                    MapInstanceId = client.Player.MapChannel.InstanceId
                });

            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.Ingame, client.State);
            Assert.IsNull(client.PendingTransfer);
            using var verify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Bootcamp,
                new CharacterStartingExperienceRepository(verify).Get(characterId).State);
            Assert.IsFalse(new GameAccountRepository(verify).Get(62).CanSkipBootcamp);
            Assert.IsFalse(new CharacterQualificationRepository(verify).HasQualification(
                characterId,
                CharacterQualificationKey.BootcampComplete));
        }

        [TestMethod]
        public void FailedDepartureTransactionKeepsTheCharacterInBootcampAndPreservesTheRuntime()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(63);
            var characterId = context.SeedCharacter(
                63,
                1,
                "DepartureRollback",
                mapContextId: BootcampSelectionTestContext.BootcampMapContextId,
                x: -255.3125,
                y: 101.05078,
                z: -70.4375,
                level: 5,
                experience: 49250);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            context.SeedMission(characterId, BootcampSelectionTestContext.MissionRetryFinale, MissionState.Active, completeable: true);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.ExitPadWaypointId, WaypointType.Dropship);
            var client = context.CreateSelectionClient(63);

            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new Packets.Game.Client.RequestSwitchToCharacterInSlotPacket { SlotNum = 1 });
            context.MaterializeLoadedClient(client);
            var privateMap = context.Maps.FindOwnedPrivateInstance(
                BootcampSelectionTestContext.BootcampMapContextId,
                characterId);
            Assert.IsNotNull(privateMap);

            context.BeforeSave = db =>
            {
                if (db.CharacterStartingExperienceEntries.Any(entry =>
                        entry.CharacterId == characterId &&
                        entry.State == CharacterStartingExperienceState.Completed))
                    throw new DbUpdateException("boom", new Exception("boom"));
            };

            context.Objects.SelectWaypoint(
                client,
                new SelectWaypointPacket
                {
                    WaypointId = BootcampSelectionTestContext.ExitPadWaypointId,
                    MapInstanceId = privateMap.InstanceId
                });

            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.Ingame, client.State);
            Assert.IsNull(client.PendingTransfer);
            Assert.AreSame(privateMap, client.Player.MapChannel);
            Assert.IsNotNull(context.Maps.FindOwnedPrivateInstance(
                BootcampSelectionTestContext.BootcampMapContextId,
                characterId));
            using var verify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Bootcamp,
                new CharacterStartingExperienceRepository(verify).Get(characterId).State);
            Assert.IsFalse(new GameAccountRepository(verify).Get(63).CanSkipBootcamp);
            Assert.IsFalse(new CharacterQualificationRepository(verify).HasQualification(
                characterId,
                CharacterQualificationKey.BootcampComplete));
            Assert.AreEqual(49250U, new CharacterRepository(verify).Get(characterId).Experience);
            Assert.AreEqual(
                (uint)MissionState.Active,
                new CharacterMissionRepository(verify)
                    .GetByCharacterAndMission(characterId, BootcampSelectionTestContext.MissionRetryFinale)
                    .MissionState);
        }
    }
}
