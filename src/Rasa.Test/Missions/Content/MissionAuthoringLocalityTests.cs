using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Content
{
    using Rasa.Data;
    using Rasa.Game.Missions.Content;
    using Rasa.Managers;
    using Rasa.Missions.Scenes;
    using Rasa.Structures;
    using Rasa.Structures.Missions;

    [TestClass]
    [DoNotParallelize]
    public class MissionAuthoringLocalityTests
    {
        [TestMethod]
        public void DataOnlyExampleCompletesKillCollectAndTalkWithoutAManagerBranch()
        {
            var pack = Read("ordinary.inactive.json");
            Assert.IsFalse(pack.Enabled);
            Assert.IsTrue(pack.Synthetic);
            Assert.IsNull(pack.Scene);
            var snapshot = new MissionContentLoader().Load(new MissionPackStore.PackRepository(new[] { pack }));
            var definitions = MissionDefinitionCatalog.CreateDefinitions(snapshot,
                new MissionValidationReport(Array.Empty<MissionValidationDiagnostic>(), Array.Empty<uint>()));
            using var context = MissionTestContext.WithCustomDefinitions(definitions);
            var giver = context.AddNpc(510206);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 1994));
            Assert.IsTrue(context.Manager.RecordProgress(context.Client, MissionProgressEvent.Creature(510210)));
            Assert.IsTrue(context.Manager.RecordProgress(context.Client, MissionProgressEvent.ItemAcquired(3147, 1)));
            var receiver = context.AddNpc(510207, npcPackageId: 2561);
            Assert.IsTrue(context.Manager.TryCompleteNpcObjective(context.Client, receiver.EntityId, 1994, 3, 1));
            Assert.IsTrue(context.Client.Player.Missions[1994].Completeable);
        }

        [TestMethod]
        public void ScriptedExampleUsesARegisteredScriptAndExistingWorldIntents()
        {
            var pack = Read("escort.inactive.json");
            Assert.IsFalse(pack.Enabled);
            Assert.IsTrue(pack.Synthetic);
            var run = new SceneRun(Guid.NewGuid().ToString("N"), pack.Definition.ContentRevision,
                pack.Scene.Script, 1, 1, 0, "{}", SceneStatus.Running, 1, pack.Definition.MissionId);
            var result = new SceneRuntime(new SceneScriptRegistry()).Evaluate(run,
                pack.Scene.Bindings(run.Release), new SceneObservation(SceneEventKind.Started, 1), DateTime.UnixEpoch);
            Assert.IsTrue(result.Accepted, result.Rejection);
            Assert.IsTrue(result.Decision.WorldIntents.OfType<RunRouteIntent>().Any());
            Assert.IsTrue(result.Decision.WorldIntents.OfType<SetInteractionIntent>().Any());
        }

        private static MissionPackDocument Read(string name)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Rasa.NET.sln")))
                root = root.Parent;
            return MissionPackCodec.Read(File.ReadAllText(Path.Combine(root!.FullName, "content", "missions", "examples", name)));
        }
    }
}
