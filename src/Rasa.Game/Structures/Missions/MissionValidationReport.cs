using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rasa.Structures.Missions
{
    using Structures.World;

    internal sealed class MissionValidationReport
    {
        private static readonly IReadOnlyDictionary<string, int> CodeOrder =
            new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["missing-client-text"] = 1,
                    ["invalid-counter-text-binding"] = 2,
                    ["missing-objective-id"] = 3,
                    ["duplicate-objective-id"] = 4,
                    ["duplicate-mission-id"] = 5,
                    ["missing-target"] = 6,
                    ["transition-cycle"] = 7,
                    ["invalid-objective-graph"] = 8,
                    ["invalid-progress-event"] = 9,
                    ["unsupported-trigger"] = 10,
                    ["unsupported-action"] = 11,
                    ["missing-npc-package"] = 12,
                    ["missing-item-template"] = 13,
                    ["missing-entity-class"] = 14,
                    ["missing-map-context"] = 15,
                    ["missing-area"] = 16,
                    ["missing-spawn-group"] = 17,
                    ["missing-scenario"] = 18,
                    ["missing-reward-reference"] = 19,
                    ["ambiguous-reward-reference"] = 20,
                    ["missing-reward"] = 21,
                    ["invalid-radius"] = 22,
                    ["invalid-quantity"] = 23,
                    ["invalid-delay"] = 24,
                    ["invalid-reward-selection"] = 25,
                    ["cross-revision-reference"] = 26,
                    ["required-chain-inactive"] = 27
                });

        private readonly HashSet<uint> _requiredMissionIds;

        public IReadOnlyList<MissionValidationDiagnostic> Diagnostics { get; }
        public bool BlocksReadiness { get; }

        public MissionValidationReport(
            IEnumerable<MissionValidationDiagnostic> diagnostics,
            IEnumerable<uint> requiredMissionIds)
        {
            _requiredMissionIds = new HashSet<uint>(
                requiredMissionIds ?? Array.Empty<uint>());
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? Array.Empty<MissionValidationDiagnostic>())
                .OrderBy(diagnostic => diagnostic.MissionId ?? 0U)
                .ThenBy(diagnostic => diagnostic.ContentRevision, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ObjectiveId ?? 0U)
                .ThenBy(diagnostic => diagnostic.TransitionId ?? 0U)
                .ThenBy(diagnostic => diagnostic.TriggerId ?? 0U)
                .ThenBy(diagnostic => diagnostic.ActionId ?? 0U)
                .ThenBy(diagnostic => CodeOrder.TryGetValue(diagnostic.Code, out var order)
                    ? order
                    : int.MaxValue)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray());
            BlocksReadiness = Diagnostics.Any(diagnostic =>
                diagnostic.MissionId.HasValue &&
                _requiredMissionIds.Contains(diagnostic.MissionId.Value));
        }

        public bool HasErrorsForMission(uint missionId) =>
            Diagnostics.Any(diagnostic => diagnostic.MissionId == missionId);
    }
}
