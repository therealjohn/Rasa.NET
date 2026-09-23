using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Rasa.Missions.Definitions
{
    public sealed record LootDrop(uint TemplateId, int ChancePercent, int Minimum, int Maximum);

    public sealed class AuthoredLootProfile
    {
        public IReadOnlyList<LootDrop> Drops { get; }
        public AuthoredLootProfile(IReadOnlyList<LootDrop> drops)
        {
            if (drops == null || drops.Any(drop => drop.TemplateId == 0 || drop.ChancePercent < 0 ||
                drop.ChancePercent > 100 || drop.Minimum < 1 || drop.Maximum < drop.Minimum || drop.Maximum == int.MaxValue))
                throw new ArgumentException("Invalid authored loot distribution.", nameof(drops));
            Drops = Array.AsReadOnly(drops.ToArray());
        }
        public IEnumerable<(uint TemplateId, uint Quantity)> Roll(Func<int, int, int> next)
        {
            foreach (var drop in Drops)
                if (drop.ChancePercent == 100 || next(0, 100) < drop.ChancePercent)
                    yield return (drop.TemplateId, (uint)(drop.Minimum == drop.Maximum
                        ? drop.Minimum : next(drop.Minimum, drop.Maximum + 1)));
        }
    }

    public sealed class ActorGameplayPolicy
    {
        public static ActorGameplayPolicy Ordinary { get; } = new();
        public bool Invulnerable { get; init; }
        public float DefenseRadius { get; init; }
        public string DefenseTargetTag { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        public bool RewardScenarioKills { get; init; }
        public bool TrackParticipation { get; init; }
        public AuthoredLootProfile Loot { get; init; }
        [JsonIgnore] public bool Defends => DefenseRadius > 0;
    }

    public sealed class ActorPolicyCatalog
    {
        private readonly Dictionary<(uint Map, uint Creature), ActorGameplayPolicy> _policies = new();
        public void Clear() => _policies.Clear();
        public void Add(uint map, uint creature, ActorGameplayPolicy policy)
        {
            if (map == 0 || creature == 0 || policy == null || !float.IsFinite(policy.DefenseRadius) ||
                policy.DefenseRadius < 0 || policy.Defends && string.IsNullOrWhiteSpace(policy.DefenseTargetTag))
                throw new ArgumentException("Invalid actor gameplay policy.");
            _policies.Add((map, creature), policy);
        }
        public ActorGameplayPolicy Get(uint map, uint creature) =>
            _policies.GetValueOrDefault((map, creature), ActorGameplayPolicy.Ordinary);
    }
}
