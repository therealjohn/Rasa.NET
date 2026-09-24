using Rasa.Missions.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Content
{
    using Rasa.Data;
    using Rasa.Game.Missions.Content;
    using Rasa.Game.Missions.Integration;
    using Rasa.Managers;
    using Rasa.Missions.Runtime;
    using Rasa.Missions.Scenes;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Structures;
    using Rasa.Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class MissionRequirementProductionTests
    {
        [TestMethod]
        public void LoadedAdmissionHandlerControlsOffersAndDurableAcceptanceButNotLaterStages()
        {
            using var harness = BootcampRuntimeTestHarness.Create(configureScenes: packs =>
                packs[1990].Requirement = new AllRequirements(new MissionRequirement[]
                {
                    new CustomRequirement("example.even-level"), new FlagRequirement(903, 1)
                }));
            var giver = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            harness.Client.Player.PlayerFlags[903] = 1;
            using (var unit = harness.Context.CreateChar())
                unit.CharacterFlags.Set(harness.Client.Player.Id, 903, 1);
            Assert.IsFalse(HasMission(Converse(harness, giver), ConversationType.MissionDispense, 1990));
            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));

            harness.Client.Player.Level = 2;
            Assert.IsTrue(HasMission(Converse(harness, giver), ConversationType.MissionDispense, 1990));
            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
            using (var unit = harness.Context.CreateChar())
                Assert.IsNull(unit.CharacterMissions.GetByCharacterAndMission(harness.Client.Player.Id, 1990));

            SetLevel(harness, 2);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
            using (var unit = harness.Context.CreateChar())
                Assert.IsNotNull(unit.CharacterMissions.GetByCharacterAndMission(harness.Client.Player.Id, 1990));

            SetLevel(harness, 3);
            harness.Client.Player.PlayerFlags.Remove(903);
            using (var unit = harness.Context.CreateChar())
                unit.CharacterFlags.Remove(harness.Client.Player.Id, 903);
            CompleteInitiationObjectives(harness);
            Assert.IsTrue(HasMission(Converse(harness, giver), ConversationType.MissionComplete, 1990));
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(harness.Client, giver.EntityId, 1990, null, null));
            using (var unit = harness.Context.CreateChar())
                Assert.AreEqual((byte)MissionState.Completed,
                    unit.CharacterMissions.Runtime.History(harness.Client.Player.Id).Single(entry => entry.MissionId == 1990).Outcome);
        }

        [TestMethod]
        public void LoadedObjectiveHandlerGatesNpcConversationAndItsDurableMutation()
        {
            using var harness = BootcampRuntimeTestHarness.Create(configureScenes: packs =>
                packs[1992].ObjectiveRequirements[4] = new CustomRequirement("example.even-level"));
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, false);
            var giver = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            var delessio = harness.AddNpc(BootcampRuntimeTestHarness.CaptainDelessioCreatureId,
                BootcampRuntimeTestHarness.CaptainDelessioPackageId);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1992));

            Assert.IsFalse(HasObjective(Converse(harness, delessio), 1992, 4));
            Assert.IsFalse(harness.Manager.TryCompleteNpcObjective(harness.Client, delessio.EntityId, 1992, 4, 1));
            harness.Client.Player.Level = 2;
            Assert.IsTrue(HasObjective(Converse(harness, delessio), 1992, 4));
            Assert.IsFalse(harness.Manager.TryCompleteNpcObjective(harness.Client, delessio.EntityId, 1992, 4, 1));
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1992].Objectives[4].State);

            SetLevel(harness, 2);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(harness.Client, delessio.EntityId, 1992, 4, 1));
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1992].Objectives[4].State);
        }

        [TestMethod]
        public void LoadedObjectiveHandlerGatesRuntimeAndDurableWorldProgress()
        {
            using var harness = BootcampRuntimeTestHarness.Create(configureScenes: packs =>
                packs[1990].ObjectiveRequirements[1] = new CustomRequirement("example.even-level"));
            var giver = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
            var progress = MissionProgressEvent.Area(1990, 430);
            Assert.IsFalse(harness.Manager.RecordProgress(harness.Client, progress));
            harness.Client.Player.Level = 2;
            Assert.IsFalse(harness.Manager.RecordProgress(harness.Client, progress));
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1990].Objectives[1].State);
            SetLevel(harness, 2);
            Assert.IsTrue(harness.Manager.RecordProgress(harness.Client, progress));
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1990].Objectives[1].State);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void LoadedTurnInHandlerGatesNpcQueryAndDurableRewardWithoutBlockingProgress(bool legacySuccess)
        {
            using var harness = BootcampRuntimeTestHarness.Create(configureScenes: packs =>
                packs[1990].TurnInRequirement = new CustomRequirement("example.even-level"));
            var giver = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
            CompleteInitiationObjectives(harness);
            if (legacySuccess)
            {
                using var unit = harness.Context.CreateChar();
                unit.ExecuteTransaction(() =>
                {
                    var mission = unit.CharacterMissions.GetByCharacterAndMission(harness.Client.Player.Id, 1990);
                    mission.MissionState = (uint)MissionState.Success;
                    mission.Completeable = false;
                });
                harness.Client.Player.Missions[1990].State = MissionState.Success;
                harness.Client.Player.Missions[1990].Completeable = false;
            }
            Assert.IsFalse(HasMission(Converse(harness, giver), ConversationType.MissionComplete, 1990));
            Assert.IsFalse(harness.Manager.TryCompleteNpcMission(harness.Client, giver.EntityId, 1990, null, null));
            harness.Client.Player.Level = 2;
            Assert.IsTrue(HasMission(Converse(harness, giver), ConversationType.MissionComplete, 1990));
            Assert.IsFalse(harness.Manager.TryCompleteNpcMission(harness.Client, giver.EntityId, 1990, null, null));
            using (var unit = harness.Context.CreateChar())
                Assert.AreEqual(0, unit.CharacterMissions.Runtime.History(harness.Client.Player.Id).Count);
            SetLevel(harness, 2);
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(harness.Client, giver.EntityId, 1990, null, null));
        }

        [TestMethod]
        public void LoadedObjectiveHandlerAlsoGatesSceneCompletionIntents()
        {
            using var harness = BootcampRuntimeTestHarness.Create(configureScenes: packs =>
            {
                packs[1990] = new MissionSceneDefinition
                {
                    Script = "data.sequence",
                    ObjectiveRequirements = new() { [1] = new CustomRequirement("example.even-level") },
                    Sequences = new()
                    {
                        [0] = new(),
                        [1] = new()
                        {
                            Character = new()
                            {
                                new ObjectiveIntent("finish", 1990, 1, MissionObjectiveState.Completed)
                            }
                        }
                    }
                };
            });
            var giver = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
            string runId;
            using (var unit = harness.Context.CreateChar())
                runId = unit.CharacterMissions.Runtime.Scenes(harness.Client.Player.Id, 1990).Single().RunId;
            var signal = new SceneObservation(SceneEventKind.Signal, 1, SequenceId: 1);
            Assert.IsFalse(harness.Manager.Scenes.Submit(runId, signal));
            harness.Client.Player.Level = 2;
            Assert.IsFalse(harness.Manager.Scenes.Submit(runId, signal));
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1990].Objectives[1].State);
            using (var unit = harness.Context.CreateChar())
                Assert.IsFalse(unit.CharacterMissions.Runtime.HasReceipt(runId, 1, "finish"));

            SetLevel(harness, 2);
            Assert.IsTrue(harness.Manager.Scenes.Submit(runId, signal));
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1990].Objectives[1].State);
        }

        [TestMethod]
        public void LoadedAccountHandlerUsesLiveFactsAndRechecksDurableEntitlement()
        {
            using var harness = BootcampRuntimeTestHarness.Create(configureScenes: packs =>
                packs[1990].Requirement = new CustomRequirement("account.starting-experience-entitlement"));
            var giver = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsFalse(HasMission(Converse(harness, giver), ConversationType.MissionDispense, 1990));
            harness.Client.AccountEntry.CanSkipBootcamp = true;
            Assert.IsTrue(HasMission(Converse(harness, giver), ConversationType.MissionDispense, 1990));
            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
            using (var unit = harness.Context.CreateChar())
                unit.GameAccounts.UpdateCanSkipBootcamp(harness.Client.AccountEntry.Id, true);
            harness.Client.ReloadGameAccountEntry();
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, giver.EntityId, 1990));
        }

        [TestMethod]
        [DataRow("admission")]
        [DataRow("objective")]
        [DataRow("turn-in")]
        [DataRow("missing-objective")]
        public void InvalidMigratedRequirementBindingsAreRejectedAtStartup(string stage)
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var packs = MissionContentTestSupport.ReadScenes(harness.WorldContext);
            var scene = packs[1990];
            var unknown = new CustomRequirement("missing.handler");
            switch (stage)
            {
                case "admission": scene.Requirement = unknown; break;
                case "objective": scene.ObjectiveRequirements[1] = unknown; break;
                case "turn-in": scene.TurnInRequirement = unknown; break;
                case "missing-objective":
                    scene.ObjectiveRequirements[999] = new CustomRequirement("example.even-level");
                    break;
            }
            var binding = harness.WorldContext.Set<MissionSceneBindingEntry>().Find(1990U, "deployment_11");
            binding.Bindings = JsonSerializer.Serialize(scene, MissionContentCodec.Options);
            harness.WorldContext.SaveChanges();
            Assert.ThrowsExactly<MissionRuleException>(() => harness.Manager.LoadMissions());
        }

        [TestMethod]
        public void RegisteredHandlersCannotRequireFactsWithoutAnAuthoritativeGameProvider()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var evaluator = new MissionRequirementEvaluator();
            evaluator.Register("test.unsupported-fact", new UnsupportedFactRequirement());
            var mission = context.Manager.LoadedMissions[321].WithPolicies(
                new Dictionary<uint, MissionCreditPolicy>(), new CustomRequirement("test.unsupported-fact"));
            var error = Assert.ThrowsExactly<MissionRuleException>(() =>
                new MissionRequirementService(evaluator).Validate(mission));
            StringAssert.Contains(error.Message, "unsupported Game fact");
        }

        [TestMethod]
        public void MissingRuntimeAccountFactsCannotSatisfyANegatedEntitlementRequirement()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            context.Map.ClientList.Remove(context.Client);
            var service = new MissionRequirementService();
            Assert.ThrowsExactly<MissionRuleException>(() => service.Evaluate(context.Client.Player,
                new NotRequirement(new CustomRequirement("account.starting-experience-entitlement"))));
        }


        private static void SetLevel(BootcampRuntimeTestHarness.Harness harness, byte level)
        {
            using var unit = harness.Context.CreateChar();
            unit.Characters.UpdateCharacterProgression(harness.Client.Player.Id, harness.Client.Player.Experience, level);
            harness.Client.Player.Level = level;
        }

        private static void CompleteInitiationObjectives(BootcampRuntimeTestHarness.Harness harness)
        {
            Assert.IsTrue(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Area(1990, 430)));
            Assert.IsTrue(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Area(1990, 431)));
        }

        private static Dictionary<ConversationType, object> Converse(BootcampRuntimeTestHarness.Harness harness, Creature npc)
        {
            harness.Drain();
            new NpcManager(harness.Context, harness.Manager).RequestNpcConverse(
                harness.Client, new RequestNPCConversePacket { EntityId = npc.EntityId });
            return harness.Drain().OfType<ConversePacket>().Single().ConvoDataDict;
        }

        private static bool HasMission(Dictionary<ConversationType, object> data, ConversationType type, uint id) =>
            data.TryGetValue(type, out var value) && (type == ConversationType.MissionDispense
                ? ((Dictionary<uint, MissionInfo>)value).ContainsKey(id)
                : ((Dictionary<uint, RewardInfo>)value).ContainsKey(id));

        private static bool HasObjective(Dictionary<ConversationType, object> data, uint mission, uint objective) =>
            data.TryGetValue(ConversationType.ObjectiveComplete, out var value) &&
            ((List<CompleteableObjectives>)value).Any(entry => entry.MissionId == mission && entry.ObjectiveId == objective);

        private sealed class UnsupportedFactRequirement : IMissionRequirementHandler
        {
            public IReadOnlyCollection<string> RequiredFacts { get; } = new[] { "unsupported.fact" };
            public bool Evaluate(IReadOnlyDictionary<string, bool> facts) => facts["unsupported.fact"];
        }
    }
}
