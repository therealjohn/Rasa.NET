using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Structures.Char;

    [TestClass]
    public class MissionLifecycleTests
    {
        [TestMethod]
        public void MissionRowsAreScopedByPersistentCharacterAndSupportMultipleMissions()
        {
            using var db = new MissionTestContext();
            db.SeedCharacter(10, 0, 100);
            db.SeedCharacter(10, 1, 101);
            db.SeedCharacter(20, 0, 200);
            db.SeedMission(100, 321, 0, false);
            db.SeedMission(100, 429, 0, true);
            db.SeedMission(101, 321, 4, false);
            db.SeedMission(200, 429, 0, false);

            using var unit = db.CreateChar();
            CollectionAssert.AreEquivalent(new uint[] { 321, 429 },
                unit.CharacterMissions.Get(100).Select(x => x.MissionId).ToArray());
            CollectionAssert.AreEquivalent(new uint[] { 321 },
                unit.CharacterMissions.Get(101).Select(x => x.MissionId).ToArray());
            CollectionAssert.AreEquivalent(new uint[] { 429 },
                unit.CharacterMissions.Get(200).Select(x => x.MissionId).ToArray());
        }

        [TestMethod]
        public void MissionStateMutationsPersistAndRemoveOnlyTheSelectedMission()
        {
            using var db = new MissionTestContext();
            db.SeedCharacter(10, 0, 100);

            using (var unit = db.CreateChar())
            {
                unit.CharacterMissions.Add(new CharacterMissionEntry(100, 321, 0));
                unit.CharacterMissions.Add(new CharacterMissionEntry(100, 429, 1));
                unit.CharacterMissions.SetState(100, 321, 4);
                unit.CharacterMissions.SetCompletable(100, 321, true);
            }

            using (var unit = db.CreateChar())
            {
                var mission = unit.CharacterMissions.Get(100, 321);
                Assert.AreEqual(4U, mission.MissionState);
                Assert.IsTrue(mission.Completeable);
                Assert.AreSame(mission, unit.CharacterMissions.Get(100, 321));
                unit.CharacterMissions.Remove(100, 321);
            }

            using var reopened = db.CreateChar();
            Assert.IsNull(reopened.CharacterMissions.Get(100, 321));
            CollectionAssert.AreEqual(new uint[] { 429 },
                reopened.CharacterMissions.Get(100).Select(x => x.MissionId).ToArray());
        }
    }
}
