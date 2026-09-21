using System;
using System.Linq;
using System.Numerics;

namespace Rasa.Managers
{
    using Data;
    using Structures;

    internal static class PracticeTargetManager
    {
        internal const uint EntityClassId = 29365;
        internal const uint HitPoints = 100;
        private static readonly Vector3[] Positions =
        {
            new(386, 120, 184.7f),
            new(380, 120, 186),
            new(375, 120, 186)
        };

        internal static void Initialize(MapChannel map)
        {
            if (map?.MapInfo?.MapContextId != 1985)
                return;

            foreach (var position in Positions)
            {
                if (map.DynamicObjects.Any(obj =>
                    obj.DynamicObjectType == DynamicObjectType.PracticeDummy && obj.Position == position))
                    continue;

                var target = new DynamicObject
                {
                    EntityClassId = (EntityClasses)EntityClassId,
                    DynamicObjectType = DynamicObjectType.PracticeDummy,
                    MapContextId = map.MapInfo.MapContextId,
                    RuntimeMapChannel = map,
                    Position = position,
                    StateId = UseObjectState.StateNull,
                    IsEnabled = true,
                    Comment = "Bootcamp practice dummy"
                };
                map.DynamicObjects.Add(target);
                CellManager.Instance.AddToWorld(map, target);
                target.IsInWorld = true;
            }
        }

        internal static bool TryGetTarget(MapChannel map, ulong entityId, out DynamicObject target)
        {
            target = null;
            if (map == null || !EntityManager.Instance.TryGetObject(entityId, out var candidate) ||
                candidate.DynamicObjectType != DynamicObjectType.PracticeDummy ||
                (uint)candidate.EntityClassId != EntityClassId ||
                !candidate.IsInWorld || !candidate.IsEnabled ||
                !ReferenceEquals(candidate.RuntimeMapChannel, map) ||
                !map.DynamicObjects.Contains(candidate) ||
                !IsFinite(candidate.Position))
                return false;
            target = candidate;
            return true;
        }

        internal static bool CanHit(MapChannel map, Actor source, DynamicObject target)
        {
            return source is Manifestation player &&
                ReferenceEquals(player.MapChannel, map) &&
                player.State != CharacterState.Dead &&
                player.Attributes.TryGetValue(Attributes.Health, out var health) && health.Current > 0 &&
                IsFinite(player.Position) &&
                EntityManager.Instance.Players.TryGetValue(player.EntityId, out var registered) &&
                ReferenceEquals(player, registered) &&
                (!map.IsPrivateInstance || map.OwnerCharacterId == player.Id) &&
                TryGetTarget(map, target?.EntityId ?? 0, out var current) &&
                ReferenceEquals(current, target);
        }

        internal static void RecordHit(
            MapChannel map, Actor source, DynamicObject target, ActionId actionId,
            MissionManager missions = null)
        {
            if (!CanHit(map, source, target))
                return;

            var client = map.ClientList.FirstOrDefault(candidate => ReferenceEquals(candidate.Player, source));
            if (client?.State != ClientState.Ingame || client.PendingTransfer != null)
                return;

            (missions ?? MissionManager.Instance).RecordProgress(client,
                MissionProgressEvent.ObjectHit((uint)target.EntityClassId, (uint)actionId));
        }

        private static bool IsFinite(Vector3 position) =>
            float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }
}
