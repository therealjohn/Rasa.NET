using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Scenes
{
    using Rasa.Data;
    using Rasa.Missions.Scenes;
    using Rasa.Packets.Mission.Server;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class SceneAssignmentLifecycleTests
    {
        [TestMethod]
        public void AbandonAndReacceptCannotDeliverTheOldAssignmentsTimedGrantOrSignal()
        {
            var now = DateTime.UnixEpoch;
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnScenarioEvent(321, 1, 1), utcNow: () => now);
            var scenes = context.Manager.Scenes;
            scenes.Bind(321, "data.sequence", TimedGrant());
            var independent = scenes.Start(context.Client, "data.sequence", new SceneBindings("world",
                new Dictionary<string, SceneActorDefinition>(), new Dictionary<string, SceneRoute>(),
                new Dictionary<uint, SceneSequence>
                {
                    [0] = new(timers: new[] { new SequenceTimer("independent", 10000, 1) }),
                    [1] = new()
                }));
            var giver = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            string previous;
            using (var unit = context.CreateChar())
                previous = unit.CharacterMissions.Runtime.Scenes(1, 321).Single().RunId;
            now = now.AddSeconds(1);

            Assert.IsTrue(context.Manager.TryAbandon(context.Client, 321));
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            now = now.AddSeconds(1);
            scenes.Tick(context.Map);

            Assert.IsFalse(context.Client.AccountEntry.CanSkipBootcamp, "The abandoned timer must not grant account entitlement.");
            Assert.AreEqual(MissionObjectiveState.Incomplete, context.Client.Player.Missions[321].Objectives[1].State,
                "A prior assignment's signal must not advance the replacement assignment.");
            Assert.IsFalse(scenes.Submit(previous, new SceneObservation(SceneEventKind.Signal, 1, SequenceId: 1)));
            using var verify = context.CreateChar();
            Assert.IsFalse(verify.GameAccounts.Get(1).CanSkipBootcamp);
            Assert.IsTrue(verify.CharacterMissions.Runtime.Scene(previous).Generation > 1);
            Assert.AreEqual("Cancelled", verify.CharacterMissions.Runtime.Timer(previous, "grant").Disposition);
            Assert.AreEqual("Pending", verify.CharacterMissions.Runtime.Timer(independent, "independent").Disposition);
        }

        [TestMethod]
        public void FailedAbandonmentTransactionPreservesTheOriginalAssignmentAndItsWork()
        {
            var now = DateTime.UnixEpoch;
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnScenarioEvent(321, 1, 1), utcNow: () => now);
            context.Manager.Scenes.Bind(321, "data.sequence", TimedGrant());
            var giver = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<MissionSceneEntry>().Any(entry => entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected cancellation persistence failure.");
            };

            Assert.IsFalse(context.Manager.TryAbandon(context.Client, 321));
            Assert.IsTrue(context.Client.Player.Missions.ContainsKey(321));
            using (var unit = context.CreateChar())
            {
                var scene = unit.CharacterMissions.Runtime.Scenes(1, 321).Single();
                Assert.AreEqual(1U, scene.Generation);
                Assert.AreEqual("Pending", unit.CharacterMissions.Runtime.Timer(scene.RunId, "grant").Disposition);
                Assert.IsNotNull(unit.CharacterMissions.GetByCharacterAndMission(1, 321));
            }
            context.BeforeSave = null;
            now = now.AddSeconds(2);
            context.Manager.Scenes.Tick(context.Map);
            Assert.IsTrue(context.Client.AccountEntry.CanSkipBootcamp);
            Assert.AreEqual(MissionObjectiveState.Completed, context.Client.Player.Missions[321].Objectives[1].State);
        }

        [TestMethod]
        public void AbandonmentCancelsPendingMessagesAndEffectsEvenWhenDiscardPublicationFails()
        {
            var now = DateTime.UnixEpoch;
            var failedPublication = false;
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnScenarioEvent(321, 1, 1), utcNow: () => now,
                beforeMissionPacketPublication: packet =>
                {
                    if (packet is MissionDiscardedPacket)
                    {
                        failedPublication = true;
                        throw new InvalidOperationException("Injected discard publication failure.");
                    }
                });
            context.Manager.Scenes.Bind(321, "data.sequence", TimedGrant(pendingEffect: true));
            var giver = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<MissionSceneEntry>().Any(entry => entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected scene input handling failure.");
            };
            context.Manager.TryExecuteScenario(context.Client, 321, 1);
            string previous;
            using (var unit = context.CreateChar())
            {
                previous = unit.CharacterMissions.Runtime.Scenes(1, 321).Single().RunId;
                Assert.IsTrue(unit.CharacterMissions.Runtime.Messages(previous).Any(message => message.Status == "Pending"));
                Assert.IsTrue(unit.CharacterMissions.Runtime.Effects(previous).Any(effect => effect.Status == "Pending"));
            }
            context.BeforeSave = null;

            Assert.IsTrue(context.Manager.TryAbandon(context.Client, 321));
            Assert.IsTrue(failedPublication);
            now = now.AddSeconds(1);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            now = now.AddSeconds(1);
            context.Manager.Scenes.Tick(context.Map);

            Assert.IsFalse(context.Client.AccountEntry.CanSkipBootcamp);
            Assert.AreEqual(MissionObjectiveState.Incomplete, context.Client.Player.Missions[321].Objectives[1].State);
            using var verify = context.CreateChar();
            Assert.IsTrue(verify.CharacterMissions.Runtime.Messages(previous).All(message => message.Status == "Cancelled"));
            Assert.IsTrue(verify.CharacterMissions.Runtime.Effects(previous).All(effect => effect.Status == "Cancelled"));
            Assert.IsFalse(verify.CharacterMissions.Runtime.HasReceipt(previous, 1, "late-grant"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SceneCommitsRecheckTheDurableAssignmentIdentityAndGeneration(bool generationOnly)
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnScenarioEvent(321, 1, 1));
            context.Manager.Scenes.Bind(321, "data.sequence", TimedGrant());
            var giver = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            string runId;
            using (var unit = context.CreateChar())
            {
                runId = unit.CharacterMissions.Runtime.Scenes(1, 321).Single().RunId;
                unit.ExecuteTransaction(() =>
                {
                    var assignment = unit.CharacterMissions.GetByCharacterAndMission(1, 321);
                    if (generationOnly)
                        assignment.Generation++;
                    else
                        assignment.AssignmentId = Guid.NewGuid().ToString("N");
                });
            }

            Assert.IsFalse(context.Manager.Scenes.Submit(runId,
                new SceneObservation(SceneEventKind.Signal, 1, SequenceId: 1)));
            Assert.IsFalse(context.Client.AccountEntry.CanSkipBootcamp);
            Assert.AreEqual(MissionObjectiveState.Incomplete, context.Client.Player.Missions[321].Objectives[1].State);
        }

        private static SceneBindings TimedGrant(bool pendingEffect = false) =>
            new("unversioned", new Dictionary<string, SceneActorDefinition>
                { ["later"] = new("later", SceneActorKind.Object, (uint)EntityClasses.HumanBaseMale, new ScenePosition(0, 0, 0)) },
                new Dictionary<string, SceneRoute>(),
                new Dictionary<uint, SceneSequence>
                {
                    [0] = new(worldIntents: pendingEffect
                            ? new WorldIntent[] { new SetInteractionIntent("later", "later", false) } : Array.Empty<WorldIntent>(),
                        timers: new[] { new SequenceTimer("grant", 2000, 1) }),
                    [1] = new(characterIntents: new CharacterIntent[] { new SetEntitlementIntent("late-grant", true) },
                        signals: new[] { new SceneMissionSignal(321, 1, 1) })
                });
    }
}
