extern alias RasaGame;

using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Data;
    using Game;
    using Packets.Game.Client;
    using Repositories.Char.Character;
    using Repositories.Char.CharacterAbilityDrawer;
    using Repositories.Char.CharacterMission;
    using Repositories.Char.CharacterQualification;
    using Repositories.Char.CharacterSkills;
    using Repositories.Char.CharacterStartingExperience;
    using Repositories.Char.CharacterTeleporter;
    using Repositories.Char.GameAccount;
    using Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class BootcampSkipTests
    {
        [TestMethod]
        public void EntitledPendingSkipAppliesParityWithoutCreatingBootcampMissionRows()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(51, canSkipBootcamp: true);
            var characterId = context.SeedCharacter(51, 1, "SkipMe");
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Pending);
            var client = context.CreateSelectionClient(51);

            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = true
                });

            Assert.AreEqual(BootcampSelectionTestContext.WildernessMapContextId, client.Player.MapContextId);
            Assert.IsFalse(client.Player.MapChannel.IsPrivateInstance);

            using var verify = context.OpenChar();
            var durableCharacter = new CharacterRepository(verify).Get(characterId);
            var durableStart = new CharacterStartingExperienceRepository(verify).Get(characterId);
            var missionRepository = new CharacterMissionRepository(verify);
            var waypointRepository = new CharacterTeleporterRepository(verify);
            var skillRepository = new CharacterSkillsRepository(verify);
            var drawerRepository = new CharacterAbilityDrawerRepository(verify);

            Assert.AreEqual(CharacterStartingExperienceState.Skipped, durableStart.State);
            Assert.AreEqual((uint)CharacterClass.Recruit, durableCharacter.Class);
            Assert.AreEqual((byte)5, durableCharacter.Level);
            Assert.AreEqual(43000U, durableCharacter.Experience);
            Assert.AreEqual(BootcampSelectionTestContext.WildernessMapContextId, durableCharacter.MapContextId);
            Assert.AreEqual(884.11d, durableCharacter.CoordX, 0.001d);
            Assert.AreEqual(305.8d, durableCharacter.CoordY, 0.001d);
            Assert.AreEqual(347.81d, durableCharacter.CoordZ, 0.001d);
            Assert.IsTrue(new CharacterQualificationRepository(verify).HasQualification(
                characterId,
                CharacterQualificationKey.BootcampComplete));
            CollectionAssert.AreEquivalent(
                new[] { BootcampSelectionTestContext.AliaDasWaypointId, BootcampSelectionTestContext.AliaDasHospitalId },
                waypointRepository.Get(characterId).Select(entry => entry.WaypointId).ToArray());
            Assert.AreEqual(
                1,
                skillRepository.GetCharacterSkills(characterId).Count(entry =>
                    entry.SkillId == (uint)SkillId.Lightning &&
                    entry.AbilityId == (int)ActionId.AaRecruitLightning &&
                    entry.SkillLevel == 1));
            Assert.AreEqual(
                1,
                drawerRepository.GetCharacterAbilities(characterId).Count(entry =>
                    entry.AbilitySlot == 0 &&
                    entry.AbilityId == (int)ActionId.AaRecruitLightning &&
                    entry.AbilityLevel == 1));
            CollectionAssert.AreEqual(
                new uint[] { 13066, 13096, 13156, 13186, 13713, 28 },
                context.ReadInventoryTemplates(51, characterId));
            Assert.IsNull(missionRepository.GetByCharacterAndMission(characterId, BootcampSelectionTestContext.MissionInitiation));
            Assert.IsNull(missionRepository.GetByCharacterAndMission(characterId, 1992));
            Assert.IsNull(missionRepository.GetByCharacterAndMission(characterId, 1994));
            Assert.IsNull(missionRepository.GetByCharacterAndMission(characterId, BootcampSelectionTestContext.MissionFinale));
            Assert.IsNull(missionRepository.GetByCharacterAndMission(characterId, BootcampSelectionTestContext.MissionRetryFinale));
        }

        [TestMethod]
        public void SkipEntitlementIsAccountWideAcrossMultiplePendingCharacters()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(52, canSkipBootcamp: true);
            var firstCharacter = context.SeedCharacter(52, 1, "First");
            var secondCharacter = context.SeedCharacter(52, 2, "Second");
            context.SeedStartingExperience(firstCharacter, CharacterStartingExperienceState.Pending);
            context.SeedStartingExperience(secondCharacter, CharacterStartingExperienceState.Pending);

            var firstClient = context.CreateSelectionClient(52);
            context.Characters.RequestSwitchToCharacterInSlot(
                firstClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1, SkipBootcamp = true });

            var secondClient = context.CreateSelectionClient(52);
            context.Characters.RequestSwitchToCharacterInSlot(
                secondClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 2, SkipBootcamp = true });

            using var verify = context.OpenChar();
            var starts = new CharacterStartingExperienceRepository(verify);
            Assert.AreEqual(CharacterStartingExperienceState.Skipped, starts.Get(firstCharacter).State);
            Assert.AreEqual(CharacterStartingExperienceState.Skipped, starts.Get(secondCharacter).State);
            CollectionAssert.AreEqual(
                context.ReadInventoryTemplates(52, firstCharacter),
                context.ReadInventoryTemplates(52, secondCharacter));
        }

        [TestMethod]
        public void IneligiblePendingSkipRequestIsRejectedWithoutMovingOrGrantingAnything()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(53, canSkipBootcamp: false);
            var characterId = context.SeedCharacter(53, 1, "NoSkip");
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Pending);
            var client = context.CreateSelectionClient(53);

            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = true
                });

            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.CharacterSelection, client.State);
            Assert.AreEqual(0U, client.Player.Id);
            Assert.IsNull(client.Player.MapChannel);
            using var verify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Pending,
                new CharacterStartingExperienceRepository(verify).Get(characterId).State);
            Assert.AreEqual(0, context.ReadInventoryTemplates(53, characterId).Length);
            Assert.AreEqual(0, new CharacterTeleporterRepository(verify).Get(characterId).Count);
            Assert.AreEqual(0, new CharacterSkillsRepository(verify).GetCharacterSkills(characterId).Count);
            Assert.AreEqual(0, new CharacterAbilityDrawerRepository(verify).GetCharacterAbilities(characterId).Count);
            Assert.IsFalse(new CharacterQualificationRepository(verify).HasQualification(
                characterId,
                CharacterQualificationKey.BootcampComplete));
        }

        [TestMethod]
        public void SkipRequestAgainstAlreadyStartedCharacterIgnoresPromptAndDoesNotDuplicateParity()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(54, canSkipBootcamp: true);
            var characterId = context.SeedCharacter(
                54,
                1,
                "Started",
                mapContextId: BootcampSelectionTestContext.WildernessMapContextId,
                x: 884.11,
                y: 305.8,
                z: 347.81,
                experience: 43000,
                level: 5);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Skipped);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.AliaDasWaypointId, WaypointType.Waypoint);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.AliaDasHospitalId, WaypointType.Hospital);
            using (var seed = context.CreateChar())
            {
                seed.CharacterSkills.AddOrUpdate(
                    characterId,
                    (uint)SkillId.Lightning,
                    (int)ActionId.AaRecruitLightning,
                    1);
                seed.CharacterAbilityDrawers.AddOrUpdate(
                    characterId,
                    0,
                    (int)ActionId.AaRecruitLightning,
                    1);
                seed.CharacterQualifications.Add(
                    new CharacterQualificationEntry(
                        characterId,
                        CharacterQualificationKey.BootcampComplete));
            }
            var firstClient = context.CreateSelectionClient(54);
            context.Characters.RequestSwitchToCharacterInSlot(
                firstClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1, SkipBootcamp = true });
            var itemTemplatesBefore = context.ReadInventoryTemplates(54, characterId);

            var secondClient = context.CreateSelectionClient(54);
            context.Characters.RequestSwitchToCharacterInSlot(
                secondClient,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1, SkipBootcamp = true });

            Assert.AreEqual(BootcampSelectionTestContext.WildernessMapContextId, secondClient.Player.MapContextId);
            CollectionAssert.AreEqual(itemTemplatesBefore, context.ReadInventoryTemplates(54, characterId));
            using var verify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Skipped,
                new CharacterStartingExperienceRepository(verify).Get(characterId).State);
            Assert.AreEqual(
                1,
                new CharacterSkillsRepository(verify).GetCharacterSkills(characterId)
                    .Count(entry => entry.SkillId == (uint)SkillId.Lightning));
        }

        [TestMethod]
        public void FailedSkipTransactionRollsBackParityPackage()
        {
            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(55, canSkipBootcamp: true);
            var characterId = context.SeedCharacter(55, 1, "SkipRollback");
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Pending);
            var client = context.CreateSelectionClient(55);
            var saveCount = 0;
            context.BeforeSave = db =>
            {
                saveCount++;
                if (db.ItemEntries.Any(entry => entry.ItemTemplateId == 13713))
                    throw new DbUpdateException("boom", new Exception("boom"));
            };

            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new RequestSwitchToCharacterInSlotPacket { SlotNum = 1, SkipBootcamp = true });

            Assert.IsTrue(saveCount > 0);
            Assert.AreEqual(RasaGame::Rasa.Data.ClientState.CharacterSelection, client.State);
            Assert.AreEqual(0U, client.Player.Id);
            Assert.IsNull(client.Player.MapChannel);
            using var verify = context.OpenChar();
            Assert.AreEqual(
                CharacterStartingExperienceState.Pending,
                new CharacterStartingExperienceRepository(verify).Get(characterId).State);
            Assert.AreEqual(0, context.ReadInventoryTemplates(55, characterId).Length);
            Assert.AreEqual(0, new CharacterTeleporterRepository(verify).Get(characterId).Count);
            Assert.IsFalse(new CharacterQualificationRepository(verify).HasQualification(
                characterId,
                CharacterQualificationKey.BootcampComplete));
            Assert.AreEqual(0, new CharacterSkillsRepository(verify).GetCharacterSkills(characterId).Count);
        }
    }
}
