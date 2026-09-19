using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Packets.Inventory.Client;
    using Rasa.Packets.Mission.Server;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Structures;

    [TestClass]
    [DoNotParallelize]
    public class BootcampGearingUpTests
    {
        [TestMethod]
        public void GearingUpPreservesObjectiveOrderAcrossReconnectsAndRejectsWrongNpcOrItem()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var mcAllister = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            var delessio = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainDelessioCreatureId,
                BootcampRuntimeTestHarness.CaptainDelessioPackageId);
            var hartmann = harness.AddNpc(
                BootcampRuntimeTestHarness.CorporalHartmannCreatureId,
                BootcampRuntimeTestHarness.CorporalHartmannPackageId);

            harness.SeedMission(harness.Client.Player.Id, BootcampRuntimeTestHarness.MissionInitiation, (uint)MissionState.Completed, false);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp));
            AssertMissionOrder(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionGearingUp],
                (4U, MissionObjectiveState.Incomplete),
                (1U, MissionObjectiveState.Inactive),
                (2U, MissionObjectiveState.Inactive),
                (5U, MissionObjectiveState.Inactive),
                (6U, MissionObjectiveState.Inactive),
                (3U, MissionObjectiveState.Inactive),
                (9U, MissionObjectiveState.Inactive),
                (8U, MissionObjectiveState.Inactive),
                (7U, MissionObjectiveState.Inactive));

            Assert.IsFalse(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                4,
                1));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                4,
                1));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-equipment-crate"));

            harness.Reconnect();
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-equipment-crate"));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(7862)));
            var afterGrant = harness.Context.ReadRewardTotals();
            AssertItemTemplatesPresent(harness, 13066, 13096, 13156, 13186, 13713);

            Assert.IsFalse(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(7862)));
            var afterDuplicate = harness.Context.ReadRewardTotals();
            Assert.AreEqual(afterGrant.ItemCount, afterDuplicate.ItemCount);

            var inventory = new InventoryManager(harness.Context, harness.Manager);
            PrepareEquipping(harness);
            var unrelated = CreateUnrelatedArmorItem(harness, inventory);
            Assert.IsFalse(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.ItemEquipped(
                    (uint)unrelated.ItemTemplate.Class,
                    unrelated.ItemTemplate.ItemTemplateId)));
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionGearingUp].Objectives[2].State);

            Assert.IsTrue(RecordTemplateEquipProgress(harness, 13066));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionGearingUp].Objectives[2].State);

            harness.Reconnect();
            AssertMissionOrder(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionGearingUp],
                (4U, MissionObjectiveState.Completed),
                (1U, MissionObjectiveState.Completed),
                (2U, MissionObjectiveState.Completed),
                (5U, MissionObjectiveState.Incomplete),
                (6U, MissionObjectiveState.Inactive),
                (3U, MissionObjectiveState.Inactive),
                (9U, MissionObjectiveState.Inactive),
                (8U, MissionObjectiveState.Inactive),
                (7U, MissionObjectiveState.Inactive));

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                BootcampRuntimeTestHarness.FindNpcByPackage(
                    harness.BootcampMap,
                    BootcampRuntimeTestHarness.CaptainDelessioPackageId).EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                5,
                1));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                BootcampRuntimeTestHarness.FindNpcByPackage(
                    harness.BootcampMap,
                    BootcampRuntimeTestHarness.CorporalHartmannPackageId).EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                6,
                1));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.PracticeDummyCreatureId));
            Assert.IsNull(BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.LightningDummyCreatureId));

            harness.Reconnect();
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.PracticeDummyCreatureId));
            Assert.IsNull(BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.LightningDummyCreatureId));
        }

        [TestMethod]
        public void GearingUpCombatStagesGrantLightningOnceAndRespawnNonLootableDummies()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var mcAllister = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            var delessio = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainDelessioCreatureId,
                BootcampRuntimeTestHarness.CaptainDelessioPackageId);
            var hartmann = harness.AddNpc(
                BootcampRuntimeTestHarness.CorporalHartmannCreatureId,
                BootcampRuntimeTestHarness.CorporalHartmannPackageId);
            var deSimone = harness.AddNpc(
                BootcampRuntimeTestHarness.CorporalDeSimoneCreatureId,
                BootcampRuntimeTestHarness.CorporalDeSimonePackageId);

            harness.SeedMission(harness.Client.Player.Id, BootcampRuntimeTestHarness.MissionInitiation, (uint)MissionState.Completed, false);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                4,
                1));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(7862)));
            var inventory = new InventoryManager(harness.Context, harness.Manager);
            PrepareEquipping(harness);
            Assert.IsTrue(RecordTemplateEquipProgress(harness, 13066));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                5,
                1));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                6,
                1));

            Assert.IsFalse(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Creature(BootcampRuntimeTestHarness.LightningDummyCreatureId)));
            var practiceDummy = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.PracticeDummyCreatureId);
            Assert.IsNotNull(practiceDummy);

            new CreatureManager(null, new ManifestationManager(harness.Context), harness.Manager)
                .HandleCreatureKill(harness.BootcampMap, practiceDummy, harness.Client.Player);

            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionGearingUp].Objectives[3].State);
            Assert.AreEqual(0, harness.BootcampMap.LootDispensers.Count);
            Assert.AreEqual(0, practiceDummy.HarvestAttemptsLeft);

            BootcampRuntimeTestHarness.AdvanceScenarioCorpseAndRespawn(
                harness,
                practiceDummy,
                corpseMilliseconds: 1000,
                respawnMilliseconds: 1000);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.PracticeDummyCreatureId));

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                9,
                1));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.LightningDummyCreatureId));
            using (var unit = harness.Context.CreateChar())
            {
                Assert.AreEqual(
                    1,
                    unit.CharacterSkills.GetCharacterSkills(harness.Client.Player.Id)
                        .Count(entry => entry.SkillId == (uint)SkillId.Lightning && entry.AbilityId == (int)ActionId.AaRecruitLightning && entry.SkillLevel == 1));
                Assert.AreEqual(
                    1,
                    unit.CharacterAbilityDrawers.GetCharacterAbilities(harness.Client.Player.Id)
                        .Count(entry => entry.AbilityId == (int)ActionId.AaRecruitLightning && entry.AbilityLevel == 1));
            }

            harness.Reconnect();
            using (var unit = harness.Context.CreateChar())
            {
                Assert.AreEqual(
                    1,
                    unit.CharacterSkills.GetCharacterSkills(harness.Client.Player.Id)
                        .Count(entry => entry.SkillId == (uint)SkillId.Lightning && entry.AbilityId == (int)ActionId.AaRecruitLightning && entry.SkillLevel == 1));
                Assert.AreEqual(
                    1,
                    unit.CharacterAbilityDrawers.GetCharacterAbilities(harness.Client.Player.Id)
                        .Count(entry => entry.AbilityId == (int)ActionId.AaRecruitLightning && entry.AbilityLevel == 1));
            }

            Assert.IsFalse(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.AbilityHit(
                    (uint)ActionId.AaRecruitLightning,
                    BootcampRuntimeTestHarness.PracticeDummyCreatureId)));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.AbilityHit(
                    (uint)ActionId.AaRecruitLightning,
                    BootcampRuntimeTestHarness.LightningDummyCreatureId)));

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                BootcampRuntimeTestHarness.FindNpcByPackage(
                    harness.BootcampMap,
                    BootcampRuntimeTestHarness.CorporalHartmannPackageId).EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                7,
                1));

            var beforeReward = harness.Context.ReadRewardTotals();
            var currentDeSimone = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalDeSimonePackageId);
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client,
                currentDeSimone.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                selectionIndex: null,
                rating: null));
            Assert.IsTrue(harness.Manager.TryRewardNpcMission(
                harness.Client,
                currentDeSimone.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                selectionIndex: null,
                rating: null));
            var afterReward = harness.Context.ReadRewardTotals();
            Assert.AreEqual(beforeReward.Experience + 1250U, afterReward.Experience);
            Assert.AreEqual(beforeReward.Credits + 200, afterReward.Credits);

            Assert.IsFalse(harness.Manager.TryRewardNpcMission(
                harness.Client,
                currentDeSimone.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                selectionIndex: null,
                rating: null));
            var finalReward = harness.Context.ReadRewardTotals();
            Assert.AreEqual(afterReward.Experience, finalReward.Experience);
            Assert.AreEqual(afterReward.Credits, finalReward.Credits);
        }

        private static void AssertMissionOrder(
            MissionLog mission,
            params (uint ObjectiveId, MissionObjectiveState State)[] expected) =>
            BootcampRuntimeTestHarness.AssertObjectiveStates(mission, expected);

        private static void PrepareEquipping(BootcampRuntimeTestHarness.Harness harness)
        {
            harness.Client.Player.AppearanceData ??= new Dictionary<EquipmentData, AppearanceData>();
            typeof(ManifestationManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new ManifestationManager(harness.Context));
        }

        private static Item CreateUnrelatedArmorItem(
            BootcampRuntimeTestHarness.Harness harness,
            InventoryManager inventory)
        {
            const uint templateId = 2800;
            const uint classId = 7000;
            harness.Context.AddRewardTemplate(templateId, classId);
            var classInfo = EntityClassManager.Instance.LoadedEntityClasses[(EntityClasses)classId];
            classInfo.EquipableClassInfo = new EquipableClassInfo((EquipmentData)0);
            var item = harness.Context.CreateInventoryItem(templateId, classId, 1);
            item.OwnerSlotId = 10;
            inventory.AddItemBySlot(
                harness.Client,
                InventoryType.Personal,
                item.EntityId,
                10,
                true,
                true);
            return item;
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

            Assert.Fail($"Could not find personal inventory item template {templateId}.");
            return 0;
        }

        private static void AssertItemTemplatesPresent(
            BootcampRuntimeTestHarness.Harness harness,
            params uint[] expectedTemplateIds)
        {
            var actual = harness.Client.Player.Inventory.PersonalInventory
                .Where(entityId => entityId != 0)
                .Select(entityId => EntityManager.Instance.GetItem(entityId)?.ItemTemplate?.ItemTemplateId ?? 0)
                .Where(templateId => templateId != 0)
                .ToList();

            foreach (var templateId in expectedTemplateIds)
                CollectionAssert.Contains(actual, templateId);
        }
    }
}
