using System;

namespace Rasa.Structures.Missions
{
    internal sealed class MissionValidationDiagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public uint? MissionId { get; }
        public string ContentRevision { get; }
        public uint? ObjectiveId { get; }
        public uint? TransitionId { get; }
        public uint? TriggerId { get; }
        public uint? ActionId { get; }

        public MissionValidationDiagnostic(
            string code,
            string message,
            uint? missionId = null,
            string contentRevision = null,
            uint? objectiveId = null,
            uint? transitionId = null,
            uint? triggerId = null,
            uint? actionId = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            MissionId = missionId;
            ContentRevision = contentRevision;
            ObjectiveId = objectiveId;
            TransitionId = transitionId;
            TriggerId = triggerId;
            ActionId = actionId;
        }

        public string ToOperatorMessage()
        {
            var prefix = MissionId.HasValue
                ? $"Mission {MissionId.Value}"
                : "Mission content";
            if (!string.IsNullOrWhiteSpace(ContentRevision))
                prefix += $"@{ContentRevision}";
            if (ObjectiveId.HasValue)
                prefix += $" objective {ObjectiveId.Value}";
            if (TransitionId.HasValue)
                prefix += $" transition {TransitionId.Value}";
            if (TriggerId.HasValue)
                prefix += $" trigger {TriggerId.Value}";
            if (ActionId.HasValue)
                prefix += $" action {ActionId.Value}";
            return $"{prefix}: {Message}";
        }
    }
}
