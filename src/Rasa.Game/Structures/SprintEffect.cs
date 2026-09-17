using System;

namespace Rasa.Structures
{
    using Game;

    internal sealed class SprintEffect
    {
        internal Client Client { get; }
        internal Manifestation Player { get; }
        internal MapChannel Map { get; }
        internal GameEffect Effect { get; }
        internal Func<long> Clock { get; }
        internal Func<bool> IsLearned { get; }
        internal long PlayerLifetime { get; }
        internal long LastUpdate { get; set; }

        internal SprintEffect(Client client, Manifestation player, MapChannel map, GameEffect effect,
            Func<long> clock, Func<bool> isLearned)
        {
            Client = client;
            Player = player;
            Map = map;
            Effect = effect;
            Clock = clock;
            IsLearned = isLearned;
            PlayerLifetime = player.ActionLifetime;
        }
    }
}
