using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rasa.Structures.Missions
{
    using Structures.World;

    internal sealed class MissionScenarioDefinition
    {
        public uint MissionId { get; }
        public string ContentRevision { get; }
        public uint ScenarioId { get; }
        public MissionContentRequirement Requirement { get; }
        public string Name { get; }
        public string Comment { get; }
        public IReadOnlyList<MissionScenarioStepDefinition> Steps { get; }

        public MissionScenarioDefinition(
            MissionScenarioEntry entry,
            IEnumerable<MissionScenarioStepDefinition> steps)
        {
            MissionId = entry.MissionId;
            ContentRevision = entry.ContentRevision;
            ScenarioId = entry.ScenarioId;
            Requirement = entry.Requirement;
            Name = entry.Name;
            Comment = entry.Comment;
            Steps = new ReadOnlyCollection<MissionScenarioStepDefinition>(
                (steps ?? Array.Empty<MissionScenarioStepDefinition>())
                .OrderBy(step => step.Sequence)
                .ThenBy(step => step.StepId)
                .ToArray());
        }
    }

    internal sealed class MissionScenarioStepDefinition
    {
        public uint MissionId { get; }
        public string ContentRevision { get; }
        public uint ScenarioId { get; }
        public uint StepId { get; }
        public MissionContentRequirement Requirement { get; }
        public MissionScenarioStepKind Kind { get; }
        public uint Sequence { get; }
        public string Comment { get; }

        public MissionScenarioStepDefinition(MissionScenarioStepEntry entry)
        {
            MissionId = entry.MissionId;
            ContentRevision = entry.ContentRevision;
            ScenarioId = entry.ScenarioId;
            StepId = entry.StepId;
            Requirement = entry.Requirement;
            Kind = entry.Kind;
            Sequence = entry.Sequence;
            Comment = entry.Comment;
        }
    }
}
