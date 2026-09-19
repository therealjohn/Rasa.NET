extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Repositories.Char.CharacterQualification;
    using Rasa.Repositories.Char.CharacterStartingExperience;
    using Rasa.Repositories.Char.GameAccount;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Test.Gameplay;

    [TestClass]
    [DoNotParallelize]
    public class BootcampEndToEndTests
    {
        private const uint MissionCaptureTheFlag = 1994;
        private const uint MissionCallingForReinforcements = 1995;
        private const uint MissionBombRetry = 2005;
        private const uint InitiationFirstAreaId = 430;
        private const uint InitiationSecondAreaId = 431;
        private const uint CaveInAreaId = 439;
        private const uint MissingScoutAreaId = 435;
        private static readonly TimeSpan BombDeadline = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan FuseDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ArrivalDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan YoungbloodDelay = TimeSpan.FromSeconds(7);

        [TestMethod]
        public void NewCharacterMissionChainAdvancesFrom1990Through1995WithoutGrantingReadyStateEarly()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var actors = SeedBootcampActors(harness);
            harness.Context.Drain();

            CompleteInitiation(harness, actors.McAllister);
            CompleteGearingUp(harness, actors);
            var youngblood = CompleteCaptureTheFlag(harness, actors);
            CompleteCallingForReinforcements(harness, youngblood);

            Assert.AreEqual(MissionState.Success,
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation].State);
            Assert.AreEqual(MissionState.Completed,
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionGearingUp].State);
            Assert.AreEqual(MissionState.Completed,
                harness.Client.Player.Missions[MissionCaptureTheFlag].State);
            Assert.AreEqual(MissionState.Active,
                harness.Client.Player.Missions[MissionCallingForReinforcements].State);
            Assert.IsTrue(harness.Client.Player.Missions[MissionCallingForReinforcements].Completeable);
            Assert.IsNull(harness.Client.PendingTransfer);

            using var verify = harness.Context.CreateChar();
            Assert.IsFalse(verify.CharacterQualifications.HasQualification(
                harness.Client.Player.Id,
                CharacterQualificationKey.BootcampComplete));
            Assert.IsFalse(verify.GameAccounts.Get(harness.Client.AccountEntry.Id).CanSkipBootcamp);
        }

        [TestMethod]
        public void TimeoutResetsMission1995AndMission2005FinishesTheRetryPath()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            harness.UtcNow += BombDeadline + TimeSpan.FromSeconds(1);
            Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));
            Assert.AreEqual(
                MissionState.Failed,
                harness.Client.Player.Missions[MissionCallingForReinforcements].State);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionBombRetry));

            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-dropship-debris"));
            harness.UtcNow += FuseDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));

            var van = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId);
            Assert.IsNotNull(van);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                van.EntityId,
                MissionBombRetry,
                4,
                1));

            Assert.AreEqual(MissionState.Active, harness.Client.Player.Missions[MissionBombRetry].State);
            Assert.IsTrue(harness.Client.Player.Missions[MissionBombRetry].Completeable);
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-dropship-debris"));
        }

        [TestMethod]
        public void BootcampCompletionUnlocksAccountSkipAndSecondPendingCharacterUsesSkipParity()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(713);
            var firstCharacterId = context.SeedCharacter(
                713,
                1,
                "Graduate",
                mapContextId: BootcampSelectionTestContext.BootcampMapContextId,
                x: -255.3125,
                y: 101.05078,
                z: -70.4375,
                experience: 49250,
                level: 5);
            var secondCharacterId = context.SeedCharacter(713, 2, "SecondPass");
            context.SeedStartingExperience(firstCharacterId, CharacterStartingExperienceState.Bootcamp);
            context.SeedStartingExperience(secondCharacterId, CharacterStartingExperienceState.Pending);
            context.SeedMission(
                firstCharacterId,
                BootcampSelectionTestContext.MissionFinale,
                MissionState.Active,
                completeable: true);
            context.SeedWaypoint(
                firstCharacterId,
                BootcampSelectionTestContext.ExitPadWaypointId,
                WaypointType.Dropship);
            SeedDepartureParityState(context, 713, firstCharacterId);

            var departingClient = context.CreateSelectionClient(713);
            context.Characters.RequestSwitchToCharacterInSlot(
                departingClient,
                new Rasa.Packets.Game.Client.RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = false
                });
            context.MaterializeLoadedClient(departingClient);

            var privateMap = context.Maps.FindOwnedPrivateInstance(
                BootcampSelectionTestContext.BootcampMapContextId,
                firstCharacterId);
            Assert.IsNotNull(privateMap);

            context.Objects.SelectWaypoint(
                departingClient,
                new Rasa.Packets.MapChannel.Client.SelectWaypointPacket
                {
                    WaypointId = BootcampSelectionTestContext.ExitPadWaypointId,
                    MapInstanceId = privateMap.InstanceId
                });
            context.CompletePendingMapLinkTransfer(departingClient);

            using (var verify = context.OpenChar())
            {
                Assert.AreEqual(
                    CharacterStartingExperienceState.Completed,
                    new CharacterStartingExperienceRepository(verify).Get(firstCharacterId).State);
                Assert.IsTrue(new GameAccountRepository(verify).Get(713).CanSkipBootcamp);
            }

            var secondClient = context.CreateSelectionClient(713);
            context.Characters.RequestSwitchToCharacterInSlot(
                secondClient,
                new Rasa.Packets.Game.Client.RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 2,
                    SkipBootcamp = true
                });

            Assert.AreEqual(
                BootcampSelectionTestContext.WildernessMapContextId,
                secondClient.Player.MapContextId);
            using var secondVerify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Skipped,
                new CharacterStartingExperienceRepository(secondVerify).Get(secondCharacterId).State);
            Assert.IsTrue(new GameAccountRepository(secondVerify).Get(713).CanSkipBootcamp);
        }

        [TestMethod]
        public void TwoSimultaneousBootcampCharactersReceiveDistinctPrivateInstancesAndDurableMissionState()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(714);
            context.SeedAccount(715);
            var firstCharacterId = context.SeedCharacter(714, 1, "FirstPending");
            var secondCharacterId = context.SeedCharacter(715, 1, "SecondPending");
            context.SeedStartingExperience(firstCharacterId, CharacterStartingExperienceState.Pending);
            context.SeedStartingExperience(secondCharacterId, CharacterStartingExperienceState.Pending);

            var firstClient = context.CreateSelectionClient(714);
            var secondClient = context.CreateSelectionClient(715);

            context.Characters.RequestSwitchToCharacterInSlot(
                firstClient,
                new Rasa.Packets.Game.Client.RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = false
                });
            context.Characters.RequestSwitchToCharacterInSlot(
                secondClient,
                new Rasa.Packets.Game.Client.RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = false
                });

            Assert.AreNotSame(firstClient.Player.MapChannel, secondClient.Player.MapChannel);
            Assert.IsTrue(firstClient.Player.MapChannel.IsPrivateInstance);
            Assert.IsTrue(secondClient.Player.MapChannel.IsPrivateInstance);
            Assert.AreEqual(firstCharacterId, firstClient.Player.MapChannel.OwnerCharacterId);
            Assert.AreEqual(secondCharacterId, secondClient.Player.MapChannel.OwnerCharacterId);
            using var verify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Bootcamp,
                new CharacterStartingExperienceRepository(verify).Get(firstCharacterId).State);
            Assert.AreEqual(
                CharacterStartingExperienceState.Bootcamp,
                new CharacterStartingExperienceRepository(verify).Get(secondCharacterId).State);
        }

        private static BootcampActors SeedBootcampActors(BootcampRuntimeTestHarness.Harness harness) =>
            new(
                harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId),
                harness.AddNpc(
                    BootcampRuntimeTestHarness.CaptainDelessioCreatureId,
                    BootcampRuntimeTestHarness.CaptainDelessioPackageId),
                harness.AddNpc(
                    BootcampRuntimeTestHarness.CorporalHartmannCreatureId,
                    BootcampRuntimeTestHarness.CorporalHartmannPackageId),
                harness.AddNpc(
                    BootcampRuntimeTestHarness.CorporalDeSimoneCreatureId,
                    BootcampRuntimeTestHarness.CorporalDeSimonePackageId));

        private static void CompleteInitiation(
            BootcampRuntimeTestHarness.Harness harness,
            Creature mcAllister)
        {
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionInitiation));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(BootcampRuntimeTestHarness.MissionInitiation, InitiationFirstAreaId)));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(BootcampRuntimeTestHarness.MissionInitiation, InitiationSecondAreaId)));
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client,
                mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionInitiation,
                selectionIndex: null,
                rating: null));
        }

        private static void CompleteGearingUp(
            BootcampRuntimeTestHarness.Harness harness,
            BootcampActors actors)
        {
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                actors.McAllister.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp));
            harness.Context.Drain();

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                4,
                1));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(7862)));

            PrepareEquipping(harness);
            Assert.IsTrue(RecordTemplateEquipProgress(harness, 13066));

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                5,
                1));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                6,
                1));

            var practiceDummy = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.PracticeDummyCreatureId);
            Assert.IsNotNull(practiceDummy);
            new CreatureManager(null, new ManifestationManager(harness.Context), harness.Manager)
                .HandleCreatureKill(harness.BootcampMap, practiceDummy, harness.Client.Player);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                9,
                1));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.AbilityHit(
                    (uint)ActionId.AaRecruitLightning,
                    BootcampRuntimeTestHarness.LightningDummyCreatureId)));

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                7,
                1));
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client,
                actors.DeSimone.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                selectionIndex: null,
                rating: null));
            Assert.IsTrue(harness.Manager.TryRewardNpcMission(
                harness.Client,
                actors.DeSimone.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                selectionIndex: null,
                rating: null));
        }

        private static Creature CompleteCaptureTheFlag(
            BootcampRuntimeTestHarness.Harness harness,
            BootcampActors actors)
        {
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                actors.DeSimone.EntityId,
                MissionCaptureTheFlag));
            harness.Context.Drain();

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.DeSimone.EntityId,
                MissionCaptureTheFlag,
                4,
                1));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(MissionCaptureTheFlag, CaveInAreaId)));

            var tizzik = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.TizzikGiCreatureId);
            Assert.IsNotNull(tizzik);
            new CreatureManager(null, new ManifestationManager(harness.Context), harness.Manager)
                .HandleCreatureKill(harness.BootcampMap, tizzik, harness.Client.Player);

            harness.UtcNow += YoungbloodDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));

            var youngblood = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId);
            Assert.IsNotNull(youngblood);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                youngblood.EntityId,
                MissionCaptureTheFlag,
                3,
                1));
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCaptureTheFlag,
                selectionIndex: null,
                rating: null));
            Assert.IsTrue(harness.Manager.TryRewardNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCaptureTheFlag,
                selectionIndex: null,
                rating: null));
            return youngblood;
        }

        private static void CompleteCallingForReinforcements(
            BootcampRuntimeTestHarness.Harness harness,
            Creature youngblood)
        {
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCallingForReinforcements));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(MissionCallingForReinforcements, MissingScoutAreaId)));

            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                MissionCallingForReinforcements,
                10,
                1));

            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-dropship-debris"));
            harness.UtcNow += FuseDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));

            var van = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId);
            Assert.IsNotNull(van);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                van.EntityId,
                MissionCallingForReinforcements,
                4,
                1));
        }

        private static Creature StartTimedFinale(BootcampRuntimeTestHarness.Harness harness)
        {
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                BootcampRuntimeTestHarness.CaptainYoungbloodPackageId);
            harness.SeedMission(1, MissionCaptureTheFlag, (uint)MissionState.Completed, completeable: true);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCallingForReinforcements));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(MissionCallingForReinforcements, MissingScoutAreaId)));

            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                MissionCallingForReinforcements,
                10,
                1));
            return youngblood;
        }

        private static DynamicObject FindScenarioObject(
            BootcampRuntimeTestHarness.Harness harness,
            string key) =>
            BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, key)
            ?? throw new AssertFailedException($"Missing scenario object {key}.");

        private static void PrepareEquipping(BootcampRuntimeTestHarness.Harness harness)
        {
            harness.Client.Player.AppearanceData ??= new Dictionary<EquipmentData, AppearanceData>();
            typeof(ManifestationManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new ManifestationManager(harness.Context));
        }

        private static bool RecordTemplateEquipProgress(
            BootcampRuntimeTestHarness.Harness harness,
            uint templateId)
        {
            var slot = FindPersonalSlotByTemplate(harness.Client, templateId);
            var item = EntityManager.Instance.GetItem(harness.Client.Player.Inventory.PersonalInventory[(int)slot]);
            return item?.ItemTemplate != null &&
                harness.Manager.RecordProgress(
                    harness.Client,
                    MissionProgressEvent.ItemEquipped(
                        (uint)item.ItemTemplate.Class,
                        item.ItemTemplate.ItemTemplateId));
        }

        private static uint FindPersonalSlotByTemplate(Client client, uint templateId)
        {
            for (uint slot = 0; slot < client.Player.Inventory.PersonalInventory.Count; slot++)
            {
                var entityId = client.Player.Inventory.PersonalInventory[(int)slot];
                if (entityId == 0)
                    continue;

                var item = EntityManager.Instance.GetItem(entityId);
                if (item?.ItemTemplate?.ItemTemplateId == templateId)
                    return slot;
            }

            throw new AssertFailedException($"Could not find personal inventory item template {templateId}.");
        }

        private static void SeedDepartureParityState(
            BootcampSelectionTestContext context,
            uint accountId,
            uint characterId)
        {
            context.SeedLightningGrant(characterId);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.AliaDasWaypointId, WaypointType.Waypoint);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.AliaDasHospitalId, WaypointType.Hospital);
            context.SeedPersonalInventory(accountId, characterId, 13066, 13096, 13156, 13186, 13713, 28);
        }

        private sealed record BootcampActors(
            Creature McAllister,
            Creature Delessio,
            Creature Hartmann,
            Creature DeSimone);
    }
}
