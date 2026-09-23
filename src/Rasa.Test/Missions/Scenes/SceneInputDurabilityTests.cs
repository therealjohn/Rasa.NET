using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Scenes
{
    using Rasa.Data;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class SceneInputDurabilityTests
    {
        [TestMethod]
        public void CommittedNpcObjectiveRecoversItsSceneInputAfterTheSceneWriteFails()
        {
            using var harness = BootcampRuntimeTestHarness.CreateFromPendingSelection(useWorldContent: true);
            harness.SpawnWorldNpcs();
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, true);
            var giver = BootcampRuntimeTestHarness.FindCreature(harness.BootcampMap, 510203);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1992));
            var delessio = BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2560);
            harness.Context.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<MissionSceneEntry>().Any(entry => entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected post-objective scene write failure.");
            };

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(harness.Client, delessio.EntityId, 1992, 4, 1));
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1992].Objectives[4].State);
            var crate = BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-equipment-crate");
            Assert.IsFalse(crate.IsEnabled);
            using (var unit = harness.Context.CreateChar())
            {
                var run = unit.CharacterMissions.Runtime.Scenes(harness.Client.Player.Id, 1992).Single();
                Assert.AreEqual(1, unit.CharacterMissions.Runtime.Messages(run.RunId).Count(message => message.Status == "Pending"));
            }

            harness.Context.BeforeSave = null;
            harness.ReconnectFresh();

            crate = BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-equipment-crate");
            Assert.IsTrue(crate.IsEnabled, "The objective's durable input must survive even though its post-commit scene dispatch failed.");
            using (var unit = harness.Context.CreateChar())
            {
                var run = unit.CharacterMissions.Runtime.Scenes(harness.Client.Player.Id, 1992).Single();
                Assert.AreEqual(1, unit.CharacterMissions.Runtime.Messages(run.RunId).Count(message => message.Status == "Handled"));
            }
        }
    }
}
