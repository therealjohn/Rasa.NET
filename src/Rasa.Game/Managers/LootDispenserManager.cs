using System;
using System.Numerics;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.LootDispenser.Server;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.Game.Server;
    using Rasa.Packets.LootDispenser.Client;
    using Structures;
    using Repositories.UnitOfWork;

    public class LootDispenserManager
    {
        /*      LootDispenser Packets
         * - LootInfo(self, lootItems):
         * - AttachInfo(self, attachedEntityId):
         * - OverallQuality(self, overallQualityId):
         * - CanLootItems(self, isLootable, canLootPerItem):
         * - TakenInfo(self, takenItems):
         * - ActorGotLoot(self, actorId, lootEntityIds):
         * - Use(self, actorId, curStateId, * args):
         * - LootCorpse(self, actorId, lootItems):
         * 
         *      LootDispenser Handlers:
         * - RequestCorpseLooting (self.entityId,)
         * - CancelCorpseLooting (self.entityId,)
         * - RequestLootAllFromCorpse (self.entityId, autoLootOnly)
         * - RequestLootItemFromCorpse (self.entityId, itemId, destSlot)
         */

        private static LootDispenserManager _instance;
        private static readonly object InstanceLock = new object();
        private readonly IGameUnitOfWorkFactory _factory;
        private readonly Func<Client, double> _distance;

        public static LootDispenserManager Instance
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new LootDispenserManager(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        internal LootDispenserManager(IGameUnitOfWorkFactory factory, Func<Client, double> distance = null)
        {
            _factory = factory;
            _distance = distance ?? (client => client.Server?.Config.GameConfig.CorpseLootDistance ??
                Config.GameConfig.DefaultCorpseLootDistance);
        }

        internal void AttachInfo(Client client, LootDispenser loot)
        {
            client.CallMethod(loot.EntityId, new AttachInfoPacket(loot.AttachedTo));
        }

        internal void LootInfo(Client client, LootDispenser loot)
        {
            client.CallMethod(loot.EntityId, new LootInfoPacket(loot.LootItems));
        }

        internal void OverallQuality(Client client, LootDispenser loot)
        {
            client.CallMethod(loot.EntityId, new OverallQualityPacket(loot.LootQuality));
        }

        internal void CanLootItems(Client client, LootDispenser loot)
        {
            client.CallMethod(loot.EntityId, new CanLootItemsPacket(loot.IsLootable, loot.LootItems));
        }

        internal void GotLoot(Client client, LootDispenser loot)
        {
            client.CallMethod(SysEntity.ClientMethodId, new GotLootPacket(loot));
        }

        internal LootDispenser Create(Client killer, Creature creature)
        {
            if (killer == null || creature == null)
                return null;
            lock (killer.SyncRoot)
            {
                var mapChannel = killer.Player?.MapChannel;
                if (mapChannel == null)
                    return null;
                lock (mapChannel.LootSyncRoot)
                {
                    if (creature.LootCreated || creature.State != CharacterState.Dead ||
                        !EntityManager.Instance.Creatures.TryGetValue(creature.EntityId, out var registered) ||
                        !ReferenceEquals(registered, creature) || creature.MapContextId != mapChannel.MapInfo.MapContextId ||
                        creature.Cells == null || creature.Cells.GetLength(0) < 3 || creature.Cells.GetLength(1) < 3 ||
                        !mapChannel.MapCellInfo.Cells.TryGetValue(creature.Cells[2, 2], out var cell) ||
                        !cell.CreatureList.Contains(creature))
                        return null;
                    var loot = new LootDispenser
                    {
                        IsLootable = true,
                        AttachedTo = creature.EntityId,
                        Owner = killer.Player.EntityId,
                        OwnerClient = killer,
                        Player = killer.Player,
                        Map = mapChannel,
                        Corpse = creature,
                        PlayerLifetime = killer.Player.AbilityLifetime,
                        CorpseLifetime = creature.AbilityLifetime,
                        CharacterId = killer.Player.Id,
                        AccountId = killer.AccountEntry?.Id ?? 0
                    };
                    CreateLoot(killer, loot);
                    mapChannel.LootDispensers.Add(loot.EntityId, loot);
                    creature.LootDispenserObjectEntityId = loot.EntityId;
                    creature.LootCreated = true;
                    return loot;
                }
            }
        }

        private LootDispenser CreateLoot(Client killer, LootDispenser loot)
        {
            var giveLoot = new Random().Next(0, 2);
            if (giveLoot > 0)
                loot.LootItems.Add(new LootItem(28, 3147, (uint)giveLoot * 3, killer.Player.EntityId, 0));

            loot.Credits = new Random().Next(1, 10);
            loot.LootQuality = (LootQuality)new Random().Next(1, 7);

            return loot;
        }

        internal void Loot(Client client, Creature creature)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return;
                lock (map.LootSyncRoot)
                {
                    var loot = Create(client, creature);
                    if (loot == null)
                        return;
                    client.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(loot.EntityId, loot.EntityClassId));
                    AttachInfo(client, loot);
                    LootInfo(client, loot);
                    OverallQuality(client, loot);
                    CanLootItems(client, loot);
                }
            }
        }

        internal bool RequestLootAllFromCorpse(Client client, RequestLootAllFromCorpsePacket packet)
        {
            if (client == null || packet == null)
                return Reject(packet?.EntityId ?? 0, "Missing active client or request.");
            // AutoLootOnly retains legacy take-all behavior until threshold filtering is implemented.
            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return Reject(packet.EntityId, "Missing active map.");
                lock (map.LootSyncRoot)
                {
                    if (!TryGetLoot(client, packet.EntityId, out var loot))
                        return Reject(packet.EntityId, "Invalid owner, corpse, lifetime, or distance.");
                    using var grant = new InventoryManager.LootGrant();
                    int credits;
                    try
                    {
                        if (client.AccountEntry == null || loot.Credits < 0 ||
                            !client.Player.Credits.TryGetValue(CurencyType.Credits, out var current) || current < 0)
                            return Reject(packet.EntityId, "Invalid account or credit state.");
                        credits = checked(current + loot.Credits);
                        using var unitOfWork = _factory.CreateChar();
                        unitOfWork.ExecuteTransaction(() =>
                        {
                            var character = unitOfWork.Characters.Get(client.Player.Id);
                            if (character.AccountId != client.AccountEntry.Id || character.Credit != current)
                                throw new GameplayRejectionException("Durable character owner or credits changed.");
                            grant.PlanAndSave(client, loot.LootItems, unitOfWork);
                            if (loot.Credits > 0)
                                unitOfWork.Characters.UpdateCharacterCredits(client.Player.Id, credits);
                            if (!TryGetLoot(client, packet.EntityId, out var currentLoot) || !ReferenceEquals(loot, currentLoot))
                                throw new GameplayRejectionException("Corpse lifetime changed during the claim.");
                        });
                    }
                    catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                    {
                        Logger.WriteLog(LogType.Error, $"Corpse loot save failed for character {client.Player.Id}: {error}");
                        return Reject(packet.EntityId, $"Persistence failed: {error.Message}");
                    }
                    loot.FullyLooted = true;
                    loot.IsLootable = false;
                    grant.Publish(client);
                    client.Player.Credits[CurencyType.Credits] = credits;
                    if (loot.Credits > 0)
                        client.CallMethod(client.Player.EntityId,
                            new Packets.MapChannel.Server.UpdateCreditsPacket(CurencyType.Credits, credits, 0));
                    client.CallMethod(loot.EntityId, new ActorGotLootPacket(loot));
                    client.CallMethod(loot.EntityId, new TakenInfoPacket(client.Player.EntityId, loot.LootItems));
                    CanLootItems(client, loot);
                    GotLoot(client, loot);
                    Retire(loot.Map, loot, false);
                    return true;
                }
            }
        }

        internal bool RequestCorpseLooting(Client client, RequestCorpseLootingPacket packet)
        {
            if (client == null || packet == null)
                return Reject(packet?.EntityId ?? 0, "Missing active client or request.");
            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return Reject(packet.EntityId, "Missing active map.");
                lock (map.LootSyncRoot)
                {
                    if (!TryGetLoot(client, packet.EntityId, out var loot))
                        return Reject(packet.EntityId, "Invalid owner, corpse, lifetime, or distance.");
                    AttachInfo(client, loot);
                    LootInfo(client, loot);
                    OverallQuality(client, loot);
                    CanLootItems(client, loot);
                    return true;
                }
            }
        }

        private bool TryGetLoot(Client client, ulong entityId, out LootDispenser loot)
        {
            loot = null;
            var limit = _distance(client);
            if (!double.IsFinite(limit) || limit <= 0)
                return Reject(entityId, "GameConfig.CorpseLootDistance must be finite and greater than zero.");
            if (!ManifestationManager.CanUseWeapons(client) || client.Player.LogoutActive ||
                EntityManager.Instance.GetEntityType(client.Player.EntityId) != EntityType.Character ||
                !client.Player.Attributes.TryGetValue(Attributes.Health, out var health) || health.Current <= 0 ||
                !client.Player.MapChannel.LootDispensers.TryGetValue(entityId, out loot) ||
                loot.Owner != client.Player.EntityId || !loot.IsLootable || loot.FullyLooted ||
                !ReferenceEquals(loot.OwnerClient, client) || !ReferenceEquals(loot.Player, client.Player) ||
                !ReferenceEquals(loot.Map, client.Player.MapChannel) ||
                loot.PlayerLifetime != client.Player.AbilityLifetime || loot.CharacterId != client.Player.Id ||
                loot.AccountId != (client.AccountEntry?.Id ?? 0) ||
                !EntityManager.Instance.Creatures.TryGetValue(loot.AttachedTo, out var corpse) ||
                EntityManager.Instance.GetEntityType(corpse.EntityId) != EntityType.Creature ||
                !ReferenceEquals(loot.Corpse, corpse) || loot.CorpseLifetime != corpse.AbilityLifetime ||
                corpse.LootDispenserObjectEntityId != loot.EntityId ||
                corpse.State != CharacterState.Dead || corpse.Controller.DeadTime >= Creature.CorpseLifetimeMilliseconds ||
                !corpse.Attributes.TryGetValue(Attributes.Health, out var corpseHealth) || corpseHealth.Current > 0 ||
                corpse.MapContextId != client.Player.MapContextId ||
                corpse.Cells == null || corpse.Cells.GetLength(0) < 3 || corpse.Cells.GetLength(1) < 3 ||
                !client.Player.MapChannel.MapCellInfo.Cells.TryGetValue(corpse.Cells[2, 2], out var cell) ||
                !cell.CreatureList.Contains(corpse))
                return false;
            var playerPosition = client.Player.Position;
            var corpsePosition = corpse.Position;
            if (!IsFinite(playerPosition) || !IsFinite(corpsePosition))
                return false;
            var x = (double)playerPosition.X - corpsePosition.X;
            var y = (double)playerPosition.Y - corpsePosition.Y;
            var z = (double)playerPosition.Z - corpsePosition.Z;
            return Math.Sqrt(x * x + y * y + z * z) <= limit;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private static bool Reject(ulong id, string reason)
        {
            Logger.WriteLog(LogType.Network, $"Rejected corpse loot {id}: {reason}");
            return false;
        }

        internal static void RemoveForCorpse(MapChannel map, Creature corpse)
        {
            lock (map.LootSyncRoot)
                foreach (var loot in map.LootDispensers.Values.Where(loot => ReferenceEquals(loot.Corpse, corpse)).ToArray())
                    Retire(map, loot, true);
        }

        internal static void RemoveForOwner(MapChannel map, Client client)
        {
            lock (map.LootSyncRoot)
                foreach (var loot in map.LootDispensers.Values.Where(loot => ReferenceEquals(loot.OwnerClient, client)).ToArray())
                    Retire(map, loot, true);
        }

        private static void Retire(MapChannel map, LootDispenser loot, bool disable)
        {
            if (!map.LootDispensers.TryGetValue(loot.EntityId, out var current) || !ReferenceEquals(current, loot))
                return;
            loot.FullyLooted = true;
            loot.IsLootable = false;
            var client = loot.OwnerClient;
            if (client?.State == ClientState.Ingame && ReferenceEquals(client.Player, loot.Player) &&
                ReferenceEquals(client.Player.MapChannel, map) && !client.Player.Disconected)
            {
                if (disable)
                    client.CallMethod(loot.EntityId, new CanLootItemsPacket(false, loot.LootItems));
                client.CallMethod(SysEntity.ClientMethodId,
                    new Packets.MapChannel.Server.DestroyPhysicalEntityPacket(loot.EntityId));
            }
            foreach (var item in loot.LootItems)
                EntityManager.Instance.FreeEntity(item.EntityId);
            loot.LootItems.Clear();
            loot.Credits = 0;
            map.LootDispensers.Remove(loot.EntityId);
            if (loot.Corpse.LootDispenserObjectEntityId == loot.EntityId)
                loot.Corpse.LootDispenserObjectEntityId = 0;
        }
    }
}
