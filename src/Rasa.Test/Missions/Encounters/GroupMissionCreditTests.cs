using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Encounters
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Missions.Runtime;
    using Rasa.Missions.Scenes;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class GroupMissionCreditTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ObjectiveEligibilityIsFrozenAtTheEventRatherThanReevaluatedAtDelivery(bool eligibleAtEvent)
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(MissionProgressEventKind.CreatureKilled, 55, 0, 0, 5),
                counters: new Dictionary<uint, MissionObjectiveCounterDefinition> { [0] = new(0, 0, 5) },
                creditPolicy: new MissionCreditPolicy(MissionCreditMode.NearbyParty, 20),
                objectiveRequirement: new CustomRequirement("example.even-level"));
            var member = context.CreateAdditionalClient(2);
            var giver = context.AddNpc(77);
            foreach (var client in new[] { context.Client, member })
                Assert.IsTrue(context.Manager.TryAcceptNpcMission(client, giver.EntityId, 321));
            SetLevel(context, context.Client, 2);
            SetLevel(context, member, eligibleAtEvent ? (byte)2 : (byte)1);
            member.Player.Level = 2;
            using var party = new PartyScope(context.Client, member);
            var eventId = Guid.NewGuid().ToString("N");

            Assert.IsTrue(context.Manager.Credit.Record(context.Client, MissionProgressEvent.Creature(55), Vector3.Zero, eventId));
            SetLevel(context, member, eligibleAtEvent ? (byte)1 : (byte)2);
            context.Manager.Credit.Tick(context.Map);
            context.Manager.Credit.Record(context.Client, MissionProgressEvent.Creature(55), Vector3.Zero, eventId);
            context.Manager.Credit.Deliver(member);

            Assert.AreEqual(1U, context.Client.Player.Missions[321].Objectives[1].Counters[0]);
            Assert.AreEqual(eligibleAtEvent ? 1U : 0U, member.Player.Missions[321].Objectives[1].Counters[0]);
            using var database = context.Open();
            Assert.AreEqual(eligibleAtEvent ? 1 : 0,
                database.Set<MissionCreditDeliveryEntry>().Count(delivery => delivery.CharacterId == 2));
        }

        private static void SetLevel(MissionTestContext context, Client client, byte level)
        {
            using var unit = context.CreateChar();
            unit.Characters.UpdateCharacterProgression(client.Player.Id, client.Player.Experience, level);
            client.Player.Level = level;
        }

        [TestMethod]
        [DataRow("replacement")]
        [DataRow("generation")]
        [DataRow("completed")]
        public void FrozenCreditCannotEscapeItsAssignmentGenerationOrCurrentObjective(string change)
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(MissionProgressEventKind.CreatureKilled, 55, 0, 0, 5),
                counters: new Dictionary<uint, MissionObjectiveCounterDefinition> { [0] = new(0, 0, 5) },
                creditPolicy: new MissionCreditPolicy(MissionCreditMode.NearbyParty, 20));
            var member = context.CreateAdditionalClient(2);
            var giver = context.AddNpc(77);
            foreach (var client in new[] { context.Client, member })
                Assert.IsTrue(context.Manager.TryAcceptNpcMission(client, giver.EntityId, 321));
            using var party = new PartyScope(context.Client, member);
            Assert.IsTrue(context.Manager.Credit.Record(context.Client, MissionProgressEvent.Creature(55), Vector3.Zero));
            if (change == "replacement")
            {
                Assert.IsTrue(context.Manager.TryAbandon(member, 321));
                Assert.IsTrue(context.Manager.TryAcceptNpcMission(member, giver.EntityId, 321));
            }
            else if (change == "generation")
            {
                using var unit = context.CreateChar();
                unit.ExecuteTransaction(() => unit.CharacterMissions.GetByCharacterAndMission(2, 321).Generation++);
            }
            else
                for (var count = 0; count < 5; count++)
                    Assert.IsTrue(context.Manager.RecordProgress(member, MissionProgressEvent.Creature(55)));

            context.Manager.Credit.Deliver(member);

            Assert.AreEqual(change == "completed" ? 5U : 0U, member.Player.Missions[321].Objectives[1].Counters[0]);
            using var database = context.Open();
            Assert.AreEqual("Expired", database.Set<MissionCreditDeliveryEntry>().Single(entry => entry.CharacterId == 2).Status);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void IdenticalSharedSignalsFromIndependentRunsOrGenerationsProduceIndependentDeliveries(bool newGeneration)
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(MissionProgressEventKind.ScenarioEvent, 55, 0, 0, 5),
                counters: new Dictionary<uint, MissionObjectiveCounterDefinition> { [0] = new(0, 0, 5) },
                creditPolicy: new MissionCreditPolicy(MissionCreditMode.NearbyParty, 20));
            var member = context.CreateAdditionalClient(2);
            var giver = context.AddNpc(77);
            foreach (var client in new[] { context.Client, member })
                Assert.IsTrue(context.Manager.TryAcceptNpcMission(client, giver.EntityId, 321));
            using var party = new PartyScope(context.Client, member);
            var bindings = new SceneBindings("test", new Dictionary<string, SceneActorDefinition>(),
                new Dictionary<string, SceneRoute>(), new Dictionary<uint, SceneSequence>
                {
                    [0] = new(signals: new[] { new SceneMissionSignal(321, 9, 55) })
                });
            var scenes = context.Manager.Scenes;
            var first = scenes.Start(context.Client, "data.sequence", bindings, 321);
            context.Manager.Credit.Tick(context.Map);
            string second;
            if (newGeneration)
            {
                using (var unit = context.CreateChar())
                    unit.ExecuteTransaction(() =>
                    {
                        var run = unit.CharacterMissions.Runtime.Scene(first);
                        run.Generation++;
                        run.Version++;
                        run.Checkpoint = "{}";
                    });
                scenes.Attach(context.Client, first, bindings);
                Assert.IsTrue(scenes.Submit(first, new SceneObservation(SceneEventKind.Started, 2)));
                second = first;
            }
            else
                second = scenes.Start(member, "data.sequence", bindings, 321);
            context.Manager.Credit.Tick(context.Map);
            scenes.Submit(second, new SceneObservation(SceneEventKind.Started, newGeneration ? 2U : 1U));
            context.Manager.Credit.Tick(context.Map);

            Assert.AreEqual(2U, context.Client.Player.Missions[321].Objectives[1].Counters[0]);
            Assert.AreEqual(2U, member.Player.Missions[321].Objectives[1].Counters[0]);
            using var database = context.Open();
            var outcomes = database.Set<MissionOutcomeEntry>().ToArray();
            Assert.AreEqual(2, outcomes.Length);
            Assert.AreEqual(2, outcomes.Select(outcome => outcome.EventId).Distinct().Count());
            Assert.AreEqual(newGeneration ? 1 : 2, outcomes.Select(outcome => outcome.RunId).Distinct().Count());
            Assert.AreEqual(newGeneration ? 2 : 1, outcomes.Select(outcome => outcome.Generation).Distinct().Count());
            Assert.AreEqual(4, database.Set<MissionCreditDeliveryEntry>().Count(delivery => delivery.Status == "Applied"));
            Assert.AreEqual(4, database.Set<MissionReceiptEntry>().Count(receipt => receipt.Kind == "Credit"));
        }

        [TestMethod]
        public void OnlyCurrentlyEligibleNearbyPartyAssignmentsReceiveFutureKillCredit()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(MissionProgressEventKind.CreatureKilled, 55),
                creditPolicy: new MissionCreditPolicy(MissionCreditMode.NearbyParty, 20));
            var eligible = context.CreateAdditionalClient(2);
            var unaccepted = context.CreateAdditionalClient(3);
            var far = context.CreateAdditionalClient(4);
            var stranger = context.CreateAdditionalClient(5);
            var giver = context.AddNpc(77);
            foreach (var client in new[] { context.Client, eligible, far, stranger })
                Assert.IsTrue(context.Manager.TryAcceptNpcMission(client, giver.EntityId, 321));
            far.Player.Position = new Vector3(100, 0, 0);
            using var party = new PartyScope(context.Client, eligible, unaccepted, far);

            Assert.IsTrue(context.Manager.Credit.Record(context.Client, MissionProgressEvent.Creature(55), Vector3.Zero));
            context.Manager.Credit.Tick(context.Map);

            Assert.AreEqual(MissionObjectiveState.Completed, eligible.Player.Missions[321].Objectives[1].State);
            Assert.AreEqual(MissionObjectiveState.Incomplete, far.Player.Missions[321].Objectives[1].State);
            Assert.AreEqual(MissionObjectiveState.Incomplete, stranger.Player.Missions[321].Objectives[1].State);
            Assert.IsFalse(unaccepted.Player.Missions.ContainsKey(321));
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(unaccepted, giver.EntityId, 321));
            context.Manager.Credit.Tick(context.Map);
            Assert.AreEqual(MissionObjectiveState.Incomplete, unaccepted.Player.Missions[321].Objectives[1].State);
        }

        [TestMethod]
        public void PartialDeliveryRetriesOnlyFrozenAssignmentsAndDoesNotRepeatSuccessfulCredit()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(MissionProgressEventKind.CreatureKilled, 55, 0, 0, 5),
                counters: new Dictionary<uint, MissionObjectiveCounterDefinition> { [0] = new(0, 0, 5) },
                creditPolicy: new MissionCreditPolicy(MissionCreditMode.NearbyParty, 20),
                objectiveRequirement: new CustomRequirement("example.even-level"));
            var member = context.CreateAdditionalClient(2);
            var later = context.CreateAdditionalClient(3);
            var giver = context.AddNpc(77);
            foreach (var client in new[] { context.Client, member })
            {
                Assert.IsTrue(context.Manager.TryAcceptNpcMission(client, giver.EntityId, 321));
                SetLevel(context, client, 2);
            }
            using var party = new PartyScope(context.Client, member, later);
            context.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<CharacterMissionObjectiveCounterEntry>().Any(entry =>
                    entry.State == EntityState.Modified && entry.Entity.CharacterId == 2))
                    throw new DbUpdateException("Injected recipient-only failure.");
            };
            var eventId = Guid.NewGuid().ToString("N");
            context.Manager.Credit.Record(context.Client, MissionProgressEvent.Creature(55), Vector3.Zero, eventId);
            context.Manager.Credit.Tick(context.Map);
            Assert.AreEqual(1U, context.Client.Player.Missions[321].Objectives[1].Counters[0]);
            Assert.AreEqual(0U, member.Player.Missions[321].Objectives[1].Counters[0]);
            context.BeforeSave = null;
            SetLevel(context, member, 1);
            SetLevel(context, later, 2);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(later, giver.EntityId, 321));
            context.Manager.Credit.Deliver(member);
            context.Manager.Credit.Record(context.Client, MissionProgressEvent.Creature(55), Vector3.Zero, eventId);

            Assert.AreEqual(1U, context.Client.Player.Missions[321].Objectives[1].Counters[0]);
            Assert.AreEqual(1U, member.Player.Missions[321].Objectives[1].Counters[0]);
            Assert.AreEqual(0U, later.Player.Missions[321].Objectives[1].Counters[0]);
        }

        internal sealed class PartyScope : IDisposable
        {
            private readonly uint _id;
            private readonly Client[] _clients;
            internal PartyScope(params Client[] clients)
            {
                _clients = clients; _id = PartyManager.Instance.GetPartyId;
                PartyManager.Instance.Parties[_id] = new Party(_id, clients[0].AccountEntry.Id,
                    clients.Select(client => new PartyMember(client)).ToList());
                foreach (var client in clients)
                    client.Player.PartyId = _id;
            }
            public void Dispose()
            {
                foreach (var client in _clients)
                    client.Player.PartyId = 0;
                PartyManager.Instance.Parties.Remove(_id);
                PartyManager.Instance.FreePartyId(_id);
            }
        }
    }
}
