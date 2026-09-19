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
                    ["missing-objective-id"] = 2,
                    ["duplicate-objective-id"] = 3,
                    ["duplicate-mission-id"] = 4,
                    ["missing-target"] = 5,
                    ["transition-cycle"] = 6,
                    ["invalid-objective-graph"] = 7,
                    ["unsupported-trigger"] = 8,
                    ["unsupported-action"] = 9,
                    ["missing-npc-package"] = 10,
                    ["missing-item-template"] = 11,
                    ["missing-entity-class"] = 12,
                    ["missing-map-context"] = 13,
                    ["missing-area"] = 14,
                    ["missing-spawn-group"] = 15,
                    ["missing-scenario"] = 16,
                    ["missing-reward"] = 17,
                    ["invalid-radius"] = 18,
                    ["invalid-quantity"] = 19,
                    ["invalid-delay"] = 20,
                    ["invalid-reward-selection"] = 21,
                    ["cross-revision-reference"] = 22,
                    ["required-chain-inactive"] = 23
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
