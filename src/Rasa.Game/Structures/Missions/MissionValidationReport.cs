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
                    ["multiple-executable-transition-paths"] = 10,
                    ["unsupported-progress-transition-actions"] = 11,
                    ["unsupported-trigger"] = 12,
                    ["unsupported-action"] = 13,
                    ["missing-npc-package"] = 14,
                    ["missing-item-template"] = 15,
                    ["missing-entity-class"] = 16,
                    ["missing-map-context"] = 17,
                    ["missing-area"] = 18,
                    ["missing-spawn-group"] = 19,
                    ["missing-scenario"] = 20,
                    ["missing-reward-reference"] = 21,
                    ["ambiguous-reward-reference"] = 22,
                    ["missing-reward"] = 23,
                    ["invalid-radius"] = 24,
                    ["invalid-quantity"] = 25,
                    ["invalid-delay"] = 26,
                    ["invalid-reward-selection"] = 27,
                    ["cross-revision-reference"] = 28,
                    ["required-chain-inactive"] = 29
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
