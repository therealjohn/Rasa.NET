using System;

namespace Rasa.Structures.Missions
{
    using Data;
    using Structures.World;

    public sealed class MissionActionDefinition
    {
        public uint MissionId { get; }
        public string ContentRevision { get; }
        public uint ObjectiveId { get; }
        public uint TransitionId { get; }
        public uint ActionId { get; }
        public MissionContentRequirement Requirement { get; }
        public MissionActionKind Kind { get; }
        public uint Sequence { get; }
        public uint? TargetObjectiveId { get; }
        public byte? ObjectiveStateValue { get; }
        public uint? RewardId { get; }
        public uint? SpawnGroupId { get; }
        public uint? ScenarioId { get; }
        public uint? IndicatorId { get; }
        public uint? PlayerFlagId { get; }
        public uint? PlayerFlagValue { get; }
        public string Comment { get; }

        public MissionActionDefinition(MissionActionEntry entry)
        {
            MissionId = entry.MissionId;
            ContentRevision = entry.ContentRevision;
            ObjectiveId = entry.ObjectiveId;
            TransitionId = entry.TransitionId;
            ActionId = entry.ActionId;
            Requirement = entry.Requirement;
            Kind = entry.Kind;
            Sequence = entry.Sequence;
            TargetObjectiveId = entry.TargetObjectiveId;
            ObjectiveStateValue = entry.ObjectiveState;
            RewardId = entry.RewardId;
            SpawnGroupId = entry.SpawnGroupId;
            ScenarioId = entry.ScenarioId;
            IndicatorId = entry.IndicatorId;
            PlayerFlagId = entry.PlayerFlagId;
            PlayerFlagValue = entry.PlayerFlagValue;
            Comment = entry.Comment;
        }

        public bool HasDefinedKind() =>
            Enum.IsDefined(typeof(MissionActionKind), Kind);

        public bool TryGetObjectiveState(out MissionObjectiveState state)
        {
            if (ObjectiveStateValue.HasValue &&
                Enum.IsDefined(typeof(MissionObjectiveState), (int)ObjectiveStateValue.Value))
            {
                state = (MissionObjectiveState)ObjectiveStateValue.Value;
                return true;
            }

            state = default;
            return false;
        }
    }
}
