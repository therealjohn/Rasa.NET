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
                    ["invalid-trigger-shape"] = 10,
                    ["multiple-executable-transition-paths"] = 11,
                    ["unsupported-progress-transition-actions"] = 12,
                    ["unsupported-trigger"] = 13,
                    ["unsupported-action"] = 14,
                    ["missing-npc-package"] = 15,
                    ["missing-item-template"] = 16,
                    ["missing-entity-class"] = 17,
                    ["missing-creature"] = 18,
                    ["missing-map-context"] = 19,
                    ["missing-area"] = 20,
                    ["missing-spawn-group"] = 21,
                    ["missing-scenario"] = 22,
                    ["missing-scenario-step"] = 23,
                    ["missing-reward-reference"] = 24,
                    ["ambiguous-reward-reference"] = 25,
                    ["missing-reward"] = 26,
                    ["invalid-radius"] = 27,
                    ["invalid-quantity"] = 28,
                    ["invalid-delay"] = 29,
                    ["invalid-reward-selection"] = 30,
                    ["cross-revision-reference"] = 31,
                    ["required-chain-inactive"] = 32
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
