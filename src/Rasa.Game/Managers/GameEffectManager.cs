using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.MapChannel.Server;
    using Structures;

    public class GameEffectManager
    {
        private static readonly GameEffectManager Singleton = new();
        public static GameEffectManager Instance => Singleton;

        private GameEffectManager() { }

        public void AddToList(Actor actor, GameEffect gameEffect) =>
            actor.ActiveEffects.Add(gameEffect.EffectId, gameEffect);

        private static int NextId(MapChannel map)
        {
            lock (map.SprintEffects)
                return ++map.CurrentEffectId;
        }

        public void AttachSprint(MapChannel mapChannel, Actor actor, uint effectLevel, int duration)
        {
            if (effectLevel < 1 || effectLevel > 5 || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(effectLevel), "Timed Sprint requires rank 1-5 and a positive duration.");
            var effect = new GameEffect
            {
                Duration = duration, TypeId = HistoricalAbilities.SprintEffectType,
                EffectId = NextId(mapChannel), EffectLevel = effectLevel
            };
            AddToList(actor, effect);
            CellManager.Instance.CellCallMethod(mapChannel, actor, AttachedPacket(actor, effect));
            UpdateMovementMod(mapChannel, actor);
        }

        internal void StartSprint(Client client, int rank, Func<long> clock, Func<bool> isLearned)
        {
            var player = client.Player;
            var map = player.MapChannel;
            var effect = new GameEffect
            {
                TypeId = HistoricalAbilities.SprintEffectType, EffectId = NextId(map),
                EffectLevel = (uint)rank, IsToggle = true
            };
            var sprint = new SprintEffect(client, player, map, effect, clock, isLearned) { LastUpdate = clock() };
            player.Sprint = sprint;
            AddToList(player, effect);
            lock (map.SprintEffects)
                map.SprintEffects.Add(sprint);
            CellManager.Instance.CellCallMethod(map, player, AttachedPacket(player, effect));
            UpdateMovementMod(map, player);
        }

        internal void CancelSprint(Client client)
        {
            if (client?.Player == null)
                return;
            lock (client.SyncRoot)
            {
                var sprint = client.Player.Sprint;
                if (sprint == null)
                    return;
                UpdateSprint(sprint);
                StopSprint(sprint);
            }
        }

        private void StopSprint(SprintEffect sprint)
        {
            lock (sprint.Map.SprintEffects)
                sprint.Map.SprintEffects.Remove(sprint);
            var wasActive = ReferenceEquals(sprint.Player.Sprint, sprint);
            var hadEffect = sprint.Player.ActiveEffects.ContainsKey(sprint.Effect.EffectId);
            if (ReferenceEquals(sprint.Player.Sprint, sprint))
                sprint.Player.Sprint = null;
            DettachEffect(sprint.Map, sprint.Player, sprint.Effect);
            if (wasActive && !hadEffect)
                UpdateMovementMod(sprint.Map, sprint.Player);
        }

        public void DettachEffect(MapChannel mapChannel, Actor actor, GameEffect gameEffect)
        {
            if (actor is Manifestation player && player.Sprint is { } sprint &&
                ReferenceEquals(sprint.Effect, gameEffect))
            {
                if (!ReferenceEquals(sprint.Map, mapChannel))
                {
                    Logger.WriteLog(LogType.Error, "Rejected Sprint detachment from a different map.");
                    return;
                }
                UpdateSprint(sprint);
                lock (sprint.Map.SprintEffects)
                    sprint.Map.SprintEffects.Remove(sprint);
                player.Sprint = null;
            }
            if (!actor.ActiveEffects.Remove(gameEffect.EffectId))
                return;
            CellManager.Instance.CellCallMethod(mapChannel, actor, new GameEffectDetachedPacket { EffectId = gameEffect.EffectId });
            if (gameEffect.TypeId == HistoricalAbilities.SprintEffectType)
                UpdateMovementMod(mapChannel, actor);
        }

        public void DoWork(MapChannel mapChannel, long passedTime)
        {
            SprintEffect[] sprints;
            lock (mapChannel.SprintEffects)
                sprints = mapChannel.SprintEffects.ToArray();
            foreach (var sprint in sprints)
                lock (sprint.Client.SyncRoot)
                    UpdateSprint(sprint);

            if (passedTime <= 0)
                return;
            foreach (var client in mapChannel.ClientList.ToArray())
            {
                lock (client.SyncRoot)
                {
                    if (client.Player == null || client.Player.MapChannel != mapChannel)
                        continue;
                    foreach (var effect in client.Player.ActiveEffects.Values.Where(effect => !effect.IsToggle).ToArray())
                    {
                        effect.EffectTime += Math.Min(passedTime, long.MaxValue - effect.EffectTime);
                        if (effect.EffectTime >= effect.Duration)
                            DettachEffect(mapChannel, client.Player, effect);
                    }
                }
            }
        }

        private void UpdateSprint(SprintEffect sprint)
        {
            var player = sprint.Player;
            if (!ReferenceEquals(sprint.Client.Player, player) || !ReferenceEquals(player.MapChannel, sprint.Map) ||
                player.ActionLifetime != sprint.PlayerLifetime ||
                !ReferenceEquals(player.Sprint, sprint) || !player.ActiveEffects.ContainsKey(sprint.Effect.EffectId) ||
                !AbilityManager.CanAct(sprint.Client) || !sprint.IsLearned() ||
                !player.Attributes.TryGetValue(Attributes.Chi, out var chi) || chi.Current <= 0 ||
                chi.CurrentMax <= 0 || chi.Current > chi.CurrentMax)
            {
                StopSprint(sprint);
                return;
            }
            var now = sprint.Clock();
            if (now <= sprint.LastUpdate)
                return;
            var elapsed = (decimal)now - sprint.LastUpdate;
            sprint.LastUpdate = now;
            var drain = player.SprintDrainRemainder +
                elapsed / 1000m * chi.CurrentMax *
                HistoricalAbilities.Sprint((int)sprint.Effect.EffectLevel).MaximumAdrenalinePerSecond;
            var spent = drain >= chi.Current ? chi.Current : (int)decimal.Floor(drain);
            chi.Current -= spent;
            player.SprintDrainRemainder = chi.Current == 0 ? 0 : drain - spent;
            if (spent > 0)
                sprint.Client.CallMethod(player.EntityId, new UpdateChiPacket(chi, 0));
            if (chi.Current == 0)
                StopSprint(sprint);
        }

        public void RemoveFromList(Actor actor, GameEffect gameEffect) =>
            actor.ActiveEffects.Remove(gameEffect.EffectId);

        public void UpdateMovementMod(MapChannel mapChannel, Actor actor)
        {
            var sprint = actor.ActiveEffects.Values.FirstOrDefault(effect => effect.TypeId == HistoricalAbilities.SprintEffectType);
            var movementMod = 1d + (sprint == null ? 0d : HistoricalAbilities.Sprint((int)sprint.EffectLevel).SpeedBonus);
            actor.MovementSpeed = movementMod;
            CellManager.Instance.CellCallMethod(mapChannel, actor, new MovementModChangePacket(movementMod));
        }

        internal static GameEffectAttachedPacket AttachedPacket(Actor actor, GameEffect effect, bool announce = true) => new()
        {
            EffectTypeId = effect.TypeId, EffectId = effect.EffectId, EffectLevel = effect.EffectLevel,
            SourceId = actor.EntityId, Announced = announce,
            Duration = effect.IsToggle ? null : effect.Duration,
            DamageType = 0, AttrId = (int)Attributes.Speed,
            IsActive = true, IsBuff = true
        };

        internal static IEnumerable<GameEffectAttachedPacket> SnapshotEffects(Client client)
        {
            lock (client.SyncRoot)
                return client.Player.ActiveEffects.Values
                    .Where(effect => effect.TypeId == HistoricalAbilities.SprintEffectType)
                    .Select(effect => AttachedPacket(client.Player, effect, false)).ToArray();
        }
    }
}
