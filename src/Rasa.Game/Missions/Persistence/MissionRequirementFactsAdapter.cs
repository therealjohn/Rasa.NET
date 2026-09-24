using System;
using System.Collections.Generic;
using System.Linq;
using Rasa.Data;
using Rasa.Missions.Runtime;
using Rasa.Repositories.Char;
using Rasa.Structures;
using Rasa.Structures.Char;

namespace Rasa.Game.Missions.Persistence
{
    internal sealed class MissionRequirementFactsAdapter
    {
        internal static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "character.level-is-even", "account.starting-experience-entitled", "character.starting-experience-completed"
        };

        internal MissionRequirementFacts Read(Manifestation player, IReadOnlyCollection<string> requested, ICharUnitOfWork unit = null)
        {
            var custom = new Dictionary<string, bool>(StringComparer.Ordinal);
            var character = unit?.Characters.Get(player.Id);
            if (unit != null && character == null)
                throw new MissionRuleException($"No authoritative character facts are available for {player.Id}.");
            foreach (var fact in requested)
            {
                if (!Supported.Contains(fact))
                    throw new MissionRuleException($"No Game fact provider is registered for {fact}.");
                custom[fact] = fact switch
                {
                    "character.level-is-even" => (character?.Level ?? player.Level) % 2 == 0,
                    "account.starting-experience-entitled" => ReadAccountEntitlement(player, character, unit),
                    "character.starting-experience-completed" => unit == null
                        ? player.StartingExperienceCompleted
                        : HasCompletedStartingExperience(unit, player.Id),
                    _ => throw new MissionRuleException($"Unknown fact {fact}.")
                };
            }
            return new MissionRequirementFacts(character?.Level ?? player.Level,
                unit == null ? player.Missions.ToDictionary(entry => entry.Key, entry => entry.Value.State) :
                    unit.CharacterMissions.Get(player.Id).ToDictionary(entry => entry.MissionId, entry => (MissionState)entry.MissionState),
                unit == null ? player.MissionHistory :
                    unit.CharacterMissions.Runtime.History(player.Id).ToDictionary(entry => entry.MissionId, entry => (MissionState)entry.Outcome),
                unit == null ? player.PlayerFlags : unit.CharacterFlags.Get(player.Id), custom);
        }

        private static bool ReadAccountEntitlement(Manifestation player, CharacterEntry character, ICharUnitOfWork unit)
        {
            var account = unit != null
                ? unit.GameAccounts.Get(character.AccountId)
                : player.MapChannel?.ClientList?.FirstOrDefault(client => ReferenceEquals(client.Player, player))?.AccountEntry;
            if (account == null)
                throw new MissionRuleException($"No authoritative account entitlement fact is available for character {player.Id}.");
            return account.CanSkipBootcamp;
        }

        internal static bool HasCompletedStartingExperience(ICharUnitOfWork unit, uint characterId) =>
            unit.CharacterStartingExperience.Get(characterId)?.State is
                CharacterStartingExperienceState.Completed or CharacterStartingExperienceState.Skipped ||
            unit.CharacterFlags.HasValue(characterId, CharacterFlagIds.BootcampComplete);
    }
}
