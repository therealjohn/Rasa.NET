using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Scenes
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Game.Missions;
    using Rasa.Game.Missions.World;
    using Rasa.Managers;
    using Rasa.Missions.Scenes;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class SceneApplicationTests
    {
        [TestMethod]
        public void AuthoredActorDefeatQueuesOneDurableSequenceWithoutARewardRecipient()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var app = Application(context, new WorldBoundary());
            var bindings = new SceneBindings("test",
                new Dictionary<string, SceneActorDefinition>
                { ["hostile"] = new("hostile", SceneActorKind.Creature, 77, new ScenePosition(0, 0, 0)) },
                new Dictionary<string, SceneRoute>(),
                new Dictionary<uint, SceneSequence>
                {
                    [0] = new(),
                    [1] = new(characterIntents: new CharacterIntent[]
                    { new SetQualificationIntent("defeat-qualification", (byte)CharacterQualificationKey.BootcampComplete, true) })
                }, defeatSequences: new Dictionary<string, uint> { ["hostile"] = 1 });
            var id = app.Start(context.Client, "data.sequence", bindings);
            var creature = context.AddNpc(77);
            creature.SpawnPool = new SpawnPool
            {
                SceneRunId = id, SceneActorRole = "hostile", SceneGeneration = 1,
                ScenarioOwnerCharacterId = context.Client.Player.Id
            };

            app.RecordDefeat(context.Map, creature, null);
            app.RecordDefeat(context.Map, creature, null);

            Assert.IsTrue(context.Client.Player.StartingExperienceCompleted);
            using var verify = context.CreateChar();
            Assert.HasCount(1, verify.CharacterMissions.Runtime.ActorStates(id));
            var input = verify.CharacterMissions.Runtime.Messages(id).Single();
            Assert.AreEqual("Handled", input.Status);
            Assert.AreEqual(1U, input.SequenceId);
        }

        [TestMethod]
        public void QualificationChangesConvergeTheCharacterRequirementFactAfterCommit()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var app = Application(context, new WorldBoundary());
            var bindings = new SceneBindings("test",
                new Dictionary<string, SceneActorDefinition>(),
                new Dictionary<string, SceneRoute>(),
                new Dictionary<uint, SceneSequence>
                {
                    [0] = new(characterIntents: new CharacterIntent[]
                    {
                        new SetQualificationIntent("graduate", (byte)CharacterQualificationKey.BootcampComplete, true)
                    }),
                    [1] = new(characterIntents: new CharacterIntent[]
                    {
                        new SetQualificationIntent("remove-graduation", (byte)CharacterQualificationKey.BootcampComplete, false)
                    })
                });
            var id = app.Start(context.Client, "data.sequence", bindings);
            Assert.IsTrue(context.Client.Player.StartingExperienceCompleted);
            using (var unit = context.CreateChar())
                Assert.IsTrue(Rasa.Game.Missions.Persistence.MissionRequirementFactsAdapter
                    .HasCompletedStartingExperience(unit, context.Client.Player.Id));

            Assert.IsTrue(app.Submit(id, new SceneObservation(SceneEventKind.Signal, 1, SequenceId: 1)));
            Assert.IsFalse(context.Client.Player.StartingExperienceCompleted);
            using (var unit = context.CreateChar())
                Assert.IsFalse(Rasa.Game.Missions.Persistence.MissionRequirementFactsAdapter
                    .HasCompletedStartingExperience(unit, context.Client.Player.Id));
        }

        [TestMethod]
        public void ResettingScenesCannotReattachOrExecuteTheirOldGeneration()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var app = Application(context, new WorldBoundary());
            var id = app.Start(context.Client, "data.sequence", Bindings());
            using (var unit = context.CreateChar())
                unit.ExecuteTransaction(() =>
                {
                    var scene = unit.CharacterMissions.Runtime.Scene(id);
                    scene.Status = "Resetting"; scene.Generation++; scene.Version++;
                });
            app.Attach(context.Client, id, Bindings());
            Assert.IsFalse(app.Submit(id, new SceneObservation(SceneEventKind.Signal, 1, SequenceId: 1)));
            Assert.IsFalse(app.Tick(context.Map));
        }

        [TestMethod]
        public void ReacceptedMissionDispatchesOnlyToItsNewAssignmentScene()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var app = Application(context, new WorldBoundary());
            app.Bind(321, "data.sequence", Bindings());
            var giver = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            Assert.IsTrue(app.Execute(context.Client, 321, 0, started: true));
            Assert.IsTrue(context.Manager.TryAbandon(context.Client, 321));
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            Assert.IsTrue(app.Execute(context.Client, 321, 0, started: true));
            Assert.IsTrue(app.Execute(context.Client, 321, 1));
            using var unit = context.CreateChar();
            var assignment = unit.CharacterMissions.GetByCharacterAndMission(1, 321);
            var current = unit.CharacterMissions.Runtime.AssignmentScene(assignment.AssignmentId);
            Assert.AreEqual(2, unit.CharacterMissions.Runtime.Scenes(1, 321).Count);
            Assert.AreEqual(1, unit.CharacterMissions.Runtime.Messages(current.RunId).Count(message => message.Status == "Handled"));
        }

        [TestMethod]
        public void EffectAcknowledgmentCannotResurrectACancelledGeneration()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var world = new WorldBoundary
            {
                BeforeApply = run =>
                {
                    using var unit = context.CreateChar();
                    unit.ExecuteTransaction(() =>
                    {
                        var scene = unit.CharacterMissions.Runtime.Scene(run.Id);
                        scene.Generation++; scene.Version++; scene.Status = "Resetting";
                        foreach (var effect in unit.CharacterMissions.Runtime.Effects(run.Id))
                            effect.Status = "Cancelled";
                    });
                }
            };
            var app = Application(context, world);
            var id = app.Start(context.Client, "data.sequence", Bindings());
            using var verify = context.CreateChar();
            Assert.AreEqual("Cancelled", verify.CharacterMissions.Runtime.Effects(id).Single().Status);
        }

        [TestMethod]
        public void FailedWorldApplicationRemainsPendingAndRetriesWithoutRecommittingTheScene()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var world = new WorldBoundary { Fail = true };
            var app = Application(context, world);
            var id = app.Start(context.Client, "data.sequence", Bindings());
            using (var unit = context.CreateChar())
            {
                Assert.AreEqual("Pending", unit.CharacterMissions.Runtime.Effects(id).Single().Status);
                Assert.AreEqual(1L, unit.CharacterMissions.Runtime.Scene(id).Version);
            }
            world.Fail = false;
            app.Reconcile(id);
            using (var unit = context.CreateChar())
            {
                Assert.AreEqual("Applied", unit.CharacterMissions.Runtime.Effects(id).Single().Status);
                Assert.AreEqual(1L, unit.CharacterMissions.Runtime.Scene(id).Version);
            }
        }

        [TestMethod]
        public void DeferredWorldApplicationRetriesWithoutLoggingAFailureOrRecommittingTheScene()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var now = DateTime.UnixEpoch;
            var world = new WorldBoundary { Defer = true };
            var app = Application(context, world, () => now);
            var originalOutput = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                var id = app.Start(context.Client, "data.sequence", Bindings());
                for (var retry = 0; retry < 3; retry++)
                {
                    now = now.AddSeconds(1);
                    app.Tick(context.Map);
                }
                using (var unit = context.CreateChar())
                {
                    var effect = unit.CharacterMissions.Runtime.Effects(id).Single();
                    Assert.AreEqual("Pending", effect.Status);
                    Assert.IsNull(effect.Failure);
                    Assert.AreEqual(1L, unit.CharacterMissions.Runtime.Scene(id).Version);
                }
                world.Defer = false;
                now = now.AddSeconds(1);

                app.Tick(context.Map);

                using var verify = context.CreateChar();
                Assert.AreEqual("Applied", verify.CharacterMissions.Runtime.Effects(id).Single().Status);
                Assert.AreEqual(1L, verify.CharacterMissions.Runtime.Scene(id).Version);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }
            Assert.IsFalse(output.ToString().Contains("remains pending", StringComparison.Ordinal), output.ToString());
        }

        [TestMethod]
        public void IdleSceneTicksDoNotOpenCharacterStorage()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var app = Application(context, new WorldBoundary());
            app.Start(context.Client, "data.sequence", Bindings());
            var baseline = context.CharUnitsCreated;
            for (var tick = 0; tick < 1000; tick++)
                app.Tick(context.Map);
            Assert.AreEqual(baseline, context.CharUnitsCreated);
        }

        [TestMethod]
        public void ReconnectPreservesWallClockAndResumesAnActiveSceneWait()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var now = DateTime.UnixEpoch;
            var world = new WorldBoundary();
            var bindings = Bindings(timed: true);
            var app = Application(context, world, () => now);
            var id = app.Start(context.Client, "data.sequence", bindings);
            now = now.AddSeconds(1);
            app.Detach(1, context.Map);
            now = now.AddSeconds(100);
            app = Application(context, world, () => now);
            app.Attach(context.Client, id, bindings);
            using var unit = context.CreateChar();
            var timers = unit.CharacterMissions.Runtime.Timers(id);
            Assert.AreEqual(DateTime.UnixEpoch.AddSeconds(600), timers.Single(timer => timer.Name == "deadline").DueAtUtc);
            Assert.AreEqual(now.AddSeconds(1), timers.Single(timer => timer.Name == "dialogue").DueAtUtc);
            Assert.AreEqual("Pending", timers.Single(timer => timer.Name == "dialogue").Disposition);
        }

        [TestMethod]
        public void JournalCleanupDoesNotDeleteSceneCheckpointsOrWorldReceipts()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            context.SeedMission(1, 321, (uint)MissionState.Completed, false);
            context.ReloadPlayerMissions();
            var app = Application(context, new WorldBoundary());
            var id = app.Start(context.Client, "data.sequence", Bindings(), 321);
            Assert.IsTrue(context.Manager.TryClear(context.Client, 321));
            using var unit = context.CreateChar();
            Assert.IsNotNull(unit.CharacterMissions.Runtime.Scene(id));
            Assert.AreEqual("Applied", unit.CharacterMissions.Runtime.Effects(id).Single().Status);
            Assert.AreEqual(1, unit.CharacterMissions.Runtime.History(1).Count);
        }

        private static SceneApplication Application(MissionTestContext context, WorldBoundary world, Func<DateTime> now = null) =>
            new(context, context.Manager, new ManifestationManager(context),
                worldFactory: _ => world, utcNow: now ?? (() => DateTime.UnixEpoch));

        private static SceneBindings Bindings(bool timed = false) => new("test",
            new Dictionary<string, SceneActorDefinition>
            { ["guide"] = new("guide", SceneActorKind.Creature, 77, new ScenePosition(0, 0, 0)) },
            new Dictionary<string, SceneRoute>(),
            new Dictionary<uint, SceneSequence>
            {
                [0] = new(new WorldIntent[] { new EnsureActorIntent("guide", "guide") },
                    timers: timed ? new[]
                    {
                        new SequenceTimer("deadline", 600000, 1),
                        new SequenceTimer("dialogue", 2000, 2, SceneClockPolicy.ActiveScene)
                    } : Array.Empty<SequenceTimer>()),
                [1] = new(),
                [2] = new()
            });

        private sealed class WorldBoundary : ISceneWorld
        {
            internal bool Fail { get; set; }
            internal bool Defer { get; set; }
            internal Action<SceneRun> BeforeApply { get; set; }
            public void Attach(SceneRun run, SceneBindings bindings, Client owner, MapChannel map) { }
            public WorldEffectResult Apply(SceneRun run, WorldIntent intent)
            {
                BeforeApply?.Invoke(run);
                return Fail ? WorldEffectResult.Failed("Injected world failure") :
                    Defer ? WorldEffectResult.Deferred() : WorldEffectResult.Applied();
            }
            public void Tick(MapChannel map, DateTime utcNow) { }
            public void Detach(string runId) { }
            public void Pause(string runId) { }
            public void Terminate(SceneRun run, MapChannel map) { }
        }
    }
}
