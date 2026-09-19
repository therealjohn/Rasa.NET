using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Structures;
    using Structures.Missions;
    using Structures.World;

    internal static class MissionObjectiveRuntimeAnalyzer
    {
        internal static MissionObjectiveRuntimeAnalysis Analyze(
            uint objectiveId,
            IReadOnlyList<MissionObjectiveTransitionDefinition> transitions)
        {
            var orderedTransitions = (transitions ?? Array.Empty<MissionObjectiveTransitionDefinition>())
                .OrderBy(transition => transition.Sequence)
                .ThenBy(transition => transition.TransitionId)
                .ToArray();
            var diagnostics = new List<MissionObjectiveRuntimeDiagnostic>();
            var executablePaths = new List<string>();
            var selectedConversationTransition = default(MissionObjectiveTransitionDefinition);
            MissionProgressRule selectedProgressRule = null;
            IReadOnlyDictionary<uint, MissionObjectiveCounterDefinition> selectedCounters =
                new Dictionary<uint, MissionObjectiveCounterDefinition>();
            IReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition> selectedItemCounters =
                new Dictionary<uint, MissionObjectiveItemCounterDefinition>();
            var selectedProgressTransition = default(MissionObjectiveTransitionDefinition);

            foreach (var transition in orderedTransitions)
            {
                var conversationTriggers = transition.Triggers
                    .Where(trigger =>
                        trigger.Kind == MissionTriggerKind.Conversation &&
                        trigger.NpcPackageId.HasValue &&
                        trigger.PlayerFlagId.HasValue)
                    .ToArray();
                if (conversationTriggers.Length > 0)
                {
                    executablePaths.Add($"conversation transition {transition.TransitionId}");
                    selectedConversationTransition ??= transition;
                }

                var progressTriggers = transition.Triggers
                    .Where(trigger => trigger.Kind == MissionTriggerKind.ProgressEvent)
                    .ToArray();
                if (progressTriggers.Length == 0 ||
                    !MissionProgressRuleAuthoring.TryBuild(
                        progressTriggers,
                        out var progressRule,
                        out var counters,
                        out var itemCounters,
                        out _))
                    continue;

                executablePaths.Add($"progress transition {transition.TransitionId}");
                selectedProgressTransition ??= transition;
                selectedProgressRule ??= progressRule;
                if (selectedCounters.Count == 0)
                    selectedCounters = counters;
                if (selectedItemCounters.Count == 0)
                    selectedItemCounters = itemCounters;

                if (transition.Actions.Count > 0)
                {
                    diagnostics.Add(new MissionObjectiveRuntimeDiagnostic(
                        "unsupported-progress-transition-actions",
                        $"progress-triggered transitions cannot execute actions in the current runtime; transition {transition.TransitionId} must remove authored actions before it can be operational.",
                        transition.TransitionId));
                }
            }

            if (executablePaths.Count > 1)
            {
                diagnostics.Add(new MissionObjectiveRuntimeDiagnostic(
                    "multiple-executable-transition-paths",
                    $"objective {objectiveId} has multiple executable transition paths ({string.Join(", ", executablePaths)}); current runtime supports exactly one objective-level completion path."));
            }

            if (diagnostics.Count > 0)
                return new MissionObjectiveRuntimeAnalysis(
                    diagnostics,
                    Array.Empty<MissionObjectiveConversation>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>(),
                    null,
                    new Dictionary<uint, MissionObjectiveCounterDefinition>(),
                    new Dictionary<uint, MissionObjectiveItemCounterDefinition>());

            if (selectedConversationTransition != null)
            {
                return new MissionObjectiveRuntimeAnalysis(
                    Array.Empty<MissionObjectiveRuntimeDiagnostic>(),
                    selectedConversationTransition.Triggers
                        .Where(trigger =>
                            trigger.Kind == MissionTriggerKind.Conversation &&
                            trigger.NpcPackageId.HasValue &&
                            trigger.PlayerFlagId.HasValue)
                        .Select(trigger => new MissionObjectiveConversation(
                            trigger.NpcPackageId.Value,
                            trigger.PlayerFlagId.Value,
                            MissionObjectiveConversationType.Completion))
                        .ToArray(),
                    selectedConversationTransition.Actions
                        .Where(action =>
                            action.Kind == MissionActionKind.RevealObjective &&
                            action.TargetObjectiveId.HasValue)
                        .Select(action => action.TargetObjectiveId.Value)
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray(),
                    selectedConversationTransition.Actions
                        .Where(action =>
                            action.Kind == MissionActionKind.ActivateObjective &&
                            action.TargetObjectiveId.HasValue)
                        .Select(action => action.TargetObjectiveId.Value)
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray(),
                    null,
                    new Dictionary<uint, MissionObjectiveCounterDefinition>(),
                    new Dictionary<uint, MissionObjectiveItemCounterDefinition>());
            }

            if (selectedProgressTransition != null)
            {
                return new MissionObjectiveRuntimeAnalysis(
                    Array.Empty<MissionObjectiveRuntimeDiagnostic>(),
                    Array.Empty<MissionObjectiveConversation>(),
                    Array.Empty<uint>(),
                    Array.Empty<uint>(),
                    selectedProgressRule,
                    selectedCounters,
                    selectedItemCounters);
            }

            return new MissionObjectiveRuntimeAnalysis(
                Array.Empty<MissionObjectiveRuntimeDiagnostic>(),
                Array.Empty<MissionObjectiveConversation>(),
                Array.Empty<uint>(),
                Array.Empty<uint>(),
                null,
                new Dictionary<uint, MissionObjectiveCounterDefinition>(),
                new Dictionary<uint, MissionObjectiveItemCounterDefinition>());
        }
    }

    internal sealed class MissionObjectiveRuntimeAnalysis
    {
        public IReadOnlyList<MissionObjectiveRuntimeDiagnostic> Diagnostics { get; }
        public IReadOnlyList<MissionObjectiveConversation> Conversations { get; }
        public IReadOnlyList<uint> RevealedObjectiveIds { get; }
        public IReadOnlyList<uint> ActivatedObjectiveIds { get; }
        public MissionProgressRule ProgressRule { get; }
        public IReadOnlyDictionary<uint, MissionObjectiveCounterDefinition> Counters { get; }
        public IReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition> ItemCounters { get; }

        internal MissionObjectiveRuntimeAnalysis(
            IReadOnlyList<MissionObjectiveRuntimeDiagnostic> diagnostics,
            IReadOnlyList<MissionObjectiveConversation> conversations,
            IReadOnlyList<uint> revealedObjectiveIds,
            IReadOnlyList<uint> activatedObjectiveIds,
            MissionProgressRule progressRule,
            IReadOnlyDictionary<uint, MissionObjectiveCounterDefinition> counters,
            IReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition> itemCounters)
        {
            Diagnostics = diagnostics ?? Array.Empty<MissionObjectiveRuntimeDiagnostic>();
            Conversations = conversations ?? Array.Empty<MissionObjectiveConversation>();
            RevealedObjectiveIds = revealedObjectiveIds ?? Array.Empty<uint>();
            ActivatedObjectiveIds = activatedObjectiveIds ?? Array.Empty<uint>();
            ProgressRule = progressRule;
            Counters = counters ?? new Dictionary<uint, MissionObjectiveCounterDefinition>();
            ItemCounters = itemCounters ?? new Dictionary<uint, MissionObjectiveItemCounterDefinition>();
        }
    }

    internal readonly struct MissionObjectiveRuntimeDiagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public uint? TransitionId { get; }

        internal MissionObjectiveRuntimeDiagnostic(
            string code,
            string message,
            uint? transitionId = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            TransitionId = transitionId;
        }
    }
}
