using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Structures.Missions;
    using Rasa.Structures.World;

    [TestClass]
    public class MissionProgressRuleAuthoringTests
    {
        [TestMethod]
        public void CounterRuleRejectsEqualRangeBeforeRuntime()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateCounterTrigger(initialValue: 5, targetValue: 5) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsFalse(success);
            Assert.IsNull(rule);
            Assert.AreEqual(0, counters.Count);
            Assert.AreEqual(0, itemCounters.Count);
            Assert.AreEqual(
                "counter progress rule counter_id 1 has initial_value 5 and target_value 5; target_value must be greater than initial_value so runtime monotonic progress can advance.",
                diagnostic);
        }

        [TestMethod]
        public void CounterRuleRejectsDecreasingRangeBeforeRuntime()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateCounterTrigger(initialValue: 6, targetValue: 5) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsFalse(success);
            Assert.IsNull(rule);
            Assert.AreEqual(0, counters.Count);
            Assert.AreEqual(0, itemCounters.Count);
            Assert.AreEqual(
                "counter progress rule counter_id 1 has initial_value 6 and target_value 5; target_value must be greater than initial_value so runtime monotonic progress can advance.",
                diagnostic);
        }

        [TestMethod]
        public void CounterRuleAcceptsMonotonicRange()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateCounterTrigger(initialValue: 2, targetValue: 5) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsTrue(success);
            Assert.IsNotNull(rule);
            Assert.AreEqual(1, counters.Count);
            Assert.AreEqual(0, itemCounters.Count);
            Assert.AreEqual((uint)2, counters[1].InitialValue);
            Assert.AreEqual((uint)5, counters[1].TargetValue);
            Assert.IsNull(diagnostic);
        }

        [TestMethod]
        public void CounterRuleAcceptsUIntBoundaryRange()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateCounterTrigger(initialValue: uint.MaxValue - 1, targetValue: uint.MaxValue) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsTrue(success);
            Assert.IsNotNull(rule);
            Assert.AreEqual(1, counters.Count);
            Assert.AreEqual(0, itemCounters.Count);
            Assert.AreEqual(uint.MaxValue - 1, counters[1].InitialValue);
            Assert.AreEqual(uint.MaxValue, counters[1].TargetValue);
            Assert.IsNull(diagnostic);
        }

        [TestMethod]
        public void ItemCounterRuleRejectsEqualRangeBeforeRuntime()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateItemCounterTrigger(initialValue: 5, targetValue: 5) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsFalse(success);
            Assert.IsNull(rule);
            Assert.AreEqual(0, counters.Count);
            Assert.AreEqual(0, itemCounters.Count);
            Assert.AreEqual(
                "item counter progress rule subject_id 3147 has initial_value 5 and target_value 5; target_value must be greater than initial_value so runtime monotonic progress can advance.",
                diagnostic);
        }

        [TestMethod]
        public void ItemCounterRuleRejectsDecreasingRangeBeforeRuntime()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateItemCounterTrigger(initialValue: 6, targetValue: 5) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsFalse(success);
            Assert.IsNull(rule);
            Assert.AreEqual(0, counters.Count);
            Assert.AreEqual(0, itemCounters.Count);
            Assert.AreEqual(
                "item counter progress rule subject_id 3147 has initial_value 6 and target_value 5; target_value must be greater than initial_value so runtime monotonic progress can advance.",
                diagnostic);
        }

        [TestMethod]
        public void ItemCounterRuleAcceptsMonotonicRange()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateItemCounterTrigger(initialValue: 2, targetValue: 5) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsTrue(success);
            Assert.IsNotNull(rule);
            Assert.AreEqual(0, counters.Count);
            Assert.AreEqual(1, itemCounters.Count);
            Assert.AreEqual((uint)2, itemCounters[3147].InitialValue);
            Assert.AreEqual((uint)5, itemCounters[3147].TargetValue);
            Assert.IsNull(diagnostic);
        }

        [TestMethod]
        public void ItemCounterRuleAcceptsUIntBoundaryRange()
        {
            var success = MissionProgressRuleAuthoring.TryBuild(
                new[] { CreateItemCounterTrigger(initialValue: uint.MaxValue - 1, targetValue: uint.MaxValue) },
                out var rule,
                out var counters,
                out var itemCounters,
                out var diagnostic);

            Assert.IsTrue(success);
            Assert.IsNotNull(rule);
            Assert.AreEqual(0, counters.Count);
            Assert.AreEqual(1, itemCounters.Count);
            Assert.AreEqual(uint.MaxValue - 1, itemCounters[3147].InitialValue);
            Assert.AreEqual(uint.MaxValue, itemCounters[3147].TargetValue);
            Assert.IsNull(diagnostic);
        }

        private static MissionTriggerDefinition CreateCounterTrigger(
            uint initialValue,
            uint targetValue,
            uint counterId = 1,
            uint subjectId = 501) =>
            new(new MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 10,
                TransitionId = 20,
                TriggerId = 1,
                Requirement = MissionContentRequirement.Required,
                Kind = MissionTriggerKind.ProgressEvent,
                Sequence = 1,
                EventKind = (byte)MissionProgressEventKind.CreatureKilled,
                SubjectId = subjectId,
                CounterId = counterId,
                InitialValue = initialValue,
                TargetValue = targetValue,
                Comment = "Counter progress trigger"
            });

        private static MissionTriggerDefinition CreateItemCounterTrigger(
            uint initialValue,
            uint targetValue,
            MissionProgressEventKind kind = MissionProgressEventKind.ItemAcquired,
            uint itemClassId = 3147) =>
            new(new MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 10,
                TransitionId = 20,
                TriggerId = 1,
                Requirement = MissionContentRequirement.Required,
                Kind = MissionTriggerKind.ProgressEvent,
                Sequence = 1,
                EventKind = (byte)kind,
                SubjectId = itemClassId,
                InitialValue = initialValue,
                TargetValue = targetValue,
                Comment = "Item counter progress trigger"
            });
    }
}
