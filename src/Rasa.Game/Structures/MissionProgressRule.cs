using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rasa.Structures
{
    using Data;

    internal enum MissionProgressRuleType
    {
        CompleteExact,
        CompleteDistinctSet,
        IncrementExactCounter
    }

    public sealed class MissionProgressRule
    {
        private readonly HashSet<uint> _subjectSet;

        public MissionProgressEventKind Kind { get; }
        public IReadOnlyCollection<uint> Subjects { get; }
        public uint? CounterId { get; }
        public uint? InitialValue { get; }
        public uint? TargetValue { get; }
        public bool? SourceSpawnResolved { get; }
        internal MissionProgressRuleType RuleType { get; }

        private MissionProgressRule(
            MissionProgressRuleType ruleType,
            MissionProgressEventKind kind,
            IEnumerable<uint> subjects,
            uint? counterId = null,
            uint? initialValue = null,
            uint? targetValue = null,
            bool? sourceSpawnResolved = null)
        {
            if (!Enum.IsDefined(typeof(MissionProgressEventKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            _subjectSet = new HashSet<uint>(subjects ?? Array.Empty<uint>());
            if (_subjectSet.Count == 0 || _subjectSet.Contains(0))
                throw new ArgumentException("Progress rules require non-zero subjects.", nameof(subjects));

            RuleType = ruleType;
            Kind = kind;
            Subjects = new ReadOnlyCollection<uint>(_subjectSet.OrderBy(value => value).ToArray());
            CounterId = counterId;
            InitialValue = initialValue;
            TargetValue = targetValue;
            SourceSpawnResolved = sourceSpawnResolved;
        }

        public static MissionProgressRule CompleteOnExactSubject(
            MissionProgressEventKind kind,
            uint subjectId)
        {
            if (kind == MissionProgressEventKind.WaypointAcquired)
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    "Exact completion supports Logos, creature, or completed mission events.");
            return CompleteOnExactSubject(kind, subjectId, null);
        }

        internal static MissionProgressRule CompleteOnExactSubject(
            MissionProgressEventKind kind,
            uint subjectId,
            bool? sourceSpawnResolved) =>
            new(
                MissionProgressRuleType.CompleteExact,
                kind,
                new[] { subjectId },
                sourceSpawnResolved: sourceSpawnResolved);

        public static MissionProgressRule CompleteWhenAllDistinctSubjectsObserved(
            MissionProgressEventKind kind,
            IReadOnlySet<uint> subjects)
        {
            if (kind != MissionProgressEventKind.WaypointAcquired &&
                kind != MissionProgressEventKind.LogosAcquired)
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    "Distinct completion supports waypoint or Logos events.");
            return new MissionProgressRule(
                MissionProgressRuleType.CompleteDistinctSet, kind, subjects);
        }

        public static MissionProgressRule IncrementCounterOnExactSubject(
            MissionProgressEventKind kind,
            uint subjectId,
            uint counterId,
            uint initialValue,
            uint targetValue) =>
            new(
                MissionProgressRuleType.IncrementExactCounter,
                kind,
                new[] { subjectId },
                counterId,
                initialValue,
                targetValue);

        internal bool Matches(MissionProgressEvent progress) =>
            progress.Kind == Kind && _subjectSet.Contains(progress.SubjectId);

        internal bool IsCompatible(MissionObjectiveDefinition objective)
        {
            if (RuleType != MissionProgressRuleType.IncrementExactCounter)
                return true;
            return CounterId.HasValue &&
                InitialValue.HasValue &&
                TargetValue.HasValue &&
                objective.Counters.TryGetValue(CounterId.Value, out var counter) &&
                counter.CounterId == CounterId.Value &&
                counter.InitialValue == InitialValue.Value &&
                counter.TargetValue == TargetValue.Value;
        }

        internal bool AreAllSubjectsObserved(IReadOnlySet<uint> observed) =>
            _subjectSet.IsSubsetOf(observed);
    }
}
