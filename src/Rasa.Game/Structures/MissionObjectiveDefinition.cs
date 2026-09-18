using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rasa.Structures
{
    using Data;

    public readonly struct MissionObjectiveCounterDefinition
    {
        public uint CounterId { get; }
        public uint InitialValue { get; }
        public uint TargetValue { get; }

        public MissionObjectiveCounterDefinition(uint counterId, uint initialValue, uint targetValue)
        {
            CounterId = counterId;
            InitialValue = initialValue;
            TargetValue = targetValue;
        }
    }

    public readonly struct MissionObjectiveItemCounterDefinition
    {
        public uint ItemClassId { get; }
        public uint InitialValue { get; }
        public uint TargetValue { get; }

        public MissionObjectiveItemCounterDefinition(uint itemClassId, uint initialValue, uint targetValue)
        {
            ItemClassId = itemClassId;
            InitialValue = initialValue;
            TargetValue = targetValue;
        }
    }

    public sealed class MissionObjectiveDefinition
    {
        public uint ObjectiveId { get; }
        public uint? ClientNameTextId { get; }
        public uint? ClientBodyTextId { get; }
        public IReadOnlyList<uint?> ClientCounterTextIds { get; }
        public uint? Ordinal { get; }
        public MissionObjectiveState? InitialState { get; }
        public bool? IsRequired { get; }
        public IReadOnlyDictionary<uint, MissionObjectiveCounterDefinition> Counters { get; }
        public IReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition> ItemCounters { get; }
        public IReadOnlyList<MissionObjectiveConversation> Conversations { get; }
        public IReadOnlyList<uint> RevealedObjectiveIds { get; }
        public IReadOnlyList<uint> ActivatedObjectiveIds { get; }
        public IReadOnlyList<MissionIndicator> Indicators { get; }
        public MissionProgressRule ProgressRule { get; }
        public bool HasCompleteServerContract { get; }

        public MissionObjectiveDefinition(
            uint objectiveId,
            uint? clientNameTextId,
            uint? clientBodyTextId,
            IEnumerable<uint?> clientCounterTextIds,
            uint? ordinal,
            MissionObjectiveState? initialState,
            bool? isRequired,
            IReadOnlyDictionary<uint, MissionObjectiveCounterDefinition> counters,
            IReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition> itemCounters,
            IEnumerable<MissionObjectiveConversation> conversations,
            IEnumerable<uint> revealedObjectiveIds = null,
            IEnumerable<uint> activatedObjectiveIds = null,
            IEnumerable<MissionIndicator> indicators = null,
            MissionProgressRule progressRule = null)
        {
            ObjectiveId = objectiveId;
            ClientNameTextId = clientNameTextId;
            ClientBodyTextId = clientBodyTextId;
            ClientCounterTextIds = Array.AsReadOnly(
                (clientCounterTextIds ?? Array.Empty<uint?>()).ToArray());
            Ordinal = ordinal;
            InitialState = initialState;
            IsRequired = isRequired;
            Counters = new ReadOnlyDictionary<uint, MissionObjectiveCounterDefinition>(
                new Dictionary<uint, MissionObjectiveCounterDefinition>(
                    counters ?? new Dictionary<uint, MissionObjectiveCounterDefinition>()));
            ItemCounters = new ReadOnlyDictionary<uint, MissionObjectiveItemCounterDefinition>(
                new Dictionary<uint, MissionObjectiveItemCounterDefinition>(
                    itemCounters ?? new Dictionary<uint, MissionObjectiveItemCounterDefinition>()));
            Conversations = Array.AsReadOnly(
                (conversations ?? Array.Empty<MissionObjectiveConversation>()).ToArray());
            RevealedObjectiveIds = revealedObjectiveIds == null
                ? null
                : Array.AsReadOnly(revealedObjectiveIds.ToArray());
            ActivatedObjectiveIds = activatedObjectiveIds == null
                ? null
                : Array.AsReadOnly(activatedObjectiveIds.ToArray());
            Indicators = indicators == null
                ? null
                : Array.AsReadOnly(indicators.Select(CloneIndicator).ToArray());
            ProgressRule = progressRule;
            HasCompleteServerContract =
                ClientNameTextId.HasValue &&
                ClientBodyTextId.HasValue &&
                Ordinal.HasValue &&
                InitialState.HasValue &&
                IsRequired.HasValue &&
                RevealedObjectiveIds != null &&
                ActivatedObjectiveIds != null &&
                Indicators != null &&
                Counters.All(counter =>
                    counter.Value.CounterId == counter.Key &&
                    counter.Key < (uint)ClientCounterTextIds.Count &&
                    ClientCounterTextIds[(int)counter.Key].HasValue) &&
                ItemCounters.All(counter => counter.Value.ItemClassId == counter.Key) &&
                (ProgressRule == null || ProgressRule.IsCompatible(this));
        }

        internal MissionObjective CreateRuntime(
            MissionObjectiveState state,
            IReadOnlyDictionary<uint, uint> counters,
            IReadOnlyDictionary<uint, uint> itemCounters)
        {
            if (!HasCompleteServerContract)
                throw new InvalidOperationException("Mission objective definition is incomplete.");

            var objective = new MissionObjective
            {
                ObjectiveId = ObjectiveId,
                State = state,
                Ordinal = Ordinal.Value,
                IsRequired = IsRequired.Value
            };
            foreach (var counter in Counters)
            {
                if (!counters.TryGetValue(counter.Key, out var value))
                    throw new InvalidOperationException("Mission objective counter state is missing.");
                objective.Counters.Add(counter.Key, new MissionObjectiveCounter
                {
                    CounterValue = value,
                    InitialValue = counter.Value.InitialValue,
                    TargetValue = counter.Value.TargetValue
                });
            }
            foreach (var counter in ItemCounters)
            {
                if (!itemCounters.TryGetValue(counter.Key, out var value))
                    throw new InvalidOperationException("Mission objective item counter state is missing.");
                objective.ItemCounters.Add(counter.Key, new MissionObjectiveItemCounter
                {
                    CounterValue = value,
                    TargetValue = counter.Value.TargetValue
                });
            }
            foreach (var indicator in Indicators)
                objective.IndicatorList.Add(CloneIndicator(indicator));
            return objective;
        }

        private static MissionIndicator CloneIndicator(MissionIndicator indicator) =>
            new()
            {
                Position = indicator.Position,
                Radius = indicator.Radius,
                IndicatorId = indicator.IndicatorId,
                Show3DEffect = indicator.Show3DEffect
            };
    }
}
