using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.Communicator.Server;
    using Packets.LootDispenser.Server;
    using Packets.MapChannel.Client;
    using Packets.MapChannel.Server;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.Game.Server;
    using Rasa.Packets.LootDispenser.Client;
    using Repositories.UnitOfWork;
    using Structures;

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
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly MissionManager _missionManager;
        private readonly Func<Client, double> _distance;
        private readonly Action<Item> _beforeItemPublication;
        private readonly object _retirementSyncRoot = new object();
        private readonly Dictionary<ulong, PendingRetirement> _pendingRetirements = new();

        private sealed class PendingRetirement
        {
            internal IGameUnitOfWorkFactory Factory;
            internal uint[] ItemIds;
            internal ulong[] EntityIds;
            internal bool InProgress;
        }

        private sealed class RetirementNotice
        {
            internal Client Client;
            internal ulong LootEntityId;
            internal CanLootItemsPacket CanLoot;
            internal DestroyPhysicalEntityPacket Destroy;
        }

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

        internal LootDispenserManager(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            Func<Client, double> distance = null,
            MissionManager missionManager = null,
            Action<Item> beforeItemPublication = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _missionManager = missionManager;
            _distance = distance ?? (client =>
                client.Server?.Config.GameConfig.CorpseLootDistance ??
                Config.GameConfig.DefaultCorpseLootDistance);
            _beforeItemPublication = beforeItemPublication;
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

        /// <summary>How long a corpse with nothing left on it stays in the world.</summary>
        public const long EmptyCorpseMs = 20000;

        /// <summary>How long a non-lootable mission scenario corpse stays before its spawn pool may rebuild it.</summary>
        public const long ScenarioActorCorpseMs = 1000;

        /// <summary>How long a corpse that still has something on it stays.</summary>
        public const long LootableCorpseMs = 120000;

        /// <summary>How long a corpse someone has the window open on stays, whatever else is true.</summary>
        public const long BeingLootedCorpseMs = 300000;

        /// <summary>
        /// Whether a dead creature can leave the world yet.
        ///
        /// The rule was twenty seconds from the moment its health hit zero, full stop - no regard
        /// for loot still on it or for a player standing over it with the window open. Twenty
        /// seconds is about one more fight, so a corpse routinely vanished between the kill and
        /// the looting, and the window went with it.
        /// </summary>
        internal bool MayDespawn(MapChannel mapChannel, Creature creature, long deadTime)
        {
            RetryPendingRetirements();

            if (mapChannel == null || creature == null)
                return true;

            lock (mapChannel.LootSyncRoot)
                return MayDespawnLocked(mapChannel, creature, deadTime);
        }

        internal bool AdvanceCorpseLifetime(
            MapChannel mapChannel,
            Creature creature,
            long delta)
        {
            RetryPendingRetirements();

            if (mapChannel == null || creature?.Controller == null)
                return true;

            lock (mapChannel.LootSyncRoot)
            {
                if (delta > 0)
                    creature.Controller.DeadTime =
                        creature.Controller.DeadTime > long.MaxValue - delta
                            ? long.MaxValue
                            : creature.Controller.DeadTime + delta;

                return MayDespawnLocked(
                    mapChannel, creature, creature.Controller.DeadTime);
            }
        }

        private static bool MayDespawnLocked(
            MapChannel mapChannel,
            Creature creature,
            long deadTime)
        {
            if (creature?.SpawnPool?.ScenarioKey != null)
            {
                var lifetime = Math.Max(
                    ScenarioActorCorpseMs,
                    creature.SpawnPool.RespawnTime > 0
                        ? creature.SpawnPool.RespawnTime
                        : ScenarioActorCorpseMs);
                return Math.Max(deadTime, creature.Controller?.DeadTime ?? 0) >= lifetime;
            }

            if (creature.CorpseLootEntityId == 0 ||
                !mapChannel.LootDispensers.TryGetValue(
                    creature.CorpseLootEntityId, out var loot))
                return Math.Max(deadTime, creature.Controller?.DeadTime ?? 0) >=
                       EmptyCorpseMs;

            return HasExpired(loot, creature, deadTime);
        }

        internal LootDispenser Create(Client killer, Creature creature)
        {
            RetryPendingRetirements();
            var mapChannel = killer.Player.MapChannel;
            var loot = new LootDispenser();
            loot.IsLootable = true;
            loot.AttachedTo = creature.EntityId;
            loot.Owner = killer.Player.EntityId;
            loot.OwnerClient = killer;
            loot.Player = killer.Player;
            loot.Map = mapChannel;
            loot.Corpse = creature;
            loot.CharacterId = killer.Player.Id;
            loot.AccountId = killer.AccountEntry?.Id ?? 0;
            loot.UnitOfWorkFactory = _gameUnitOfWorkFactory;

            CreateLoot(killer, loot);

            lock (mapChannel.LootSyncRoot)
            {
                mapChannel.LootDispensers.Add(loot.EntityId, loot);
                creature.CorpseLootEntityId = loot.EntityId;
            }

            return loot;
        }

        /// <summary>
        /// One Random, not one per call. Three `new Random()` in a row seeded from the clock gave
        /// three values from the same tick, so the quality tracked the item count.
        /// </summary>
        private static readonly Random Roll = new Random();

        private LootDispenser CreateLoot(Client killer, LootDispenser loot)
        {
            int giveLoot;

            lock (Roll)
            {
                giveLoot = Roll.Next(0, 2);
                loot.Credits = Roll.Next(1, 10);
                loot.LootQuality = (LootQuality)Roll.Next(1, 7);
            }

            if (giveLoot > 0)
            {
                // A real item, made now rather than at the moment it is taken. The corpse window
                // resolves every row to an entity and silently drops the ones it cannot
                // (corpselootwindow: GetEntity(itemId), continue on None), so a row without an
                // item behind it is an empty window. It also means what is taken is what was
                // rolled, rather than a second item built from the same template.
                var item = ItemManager.Instance.CreateFromTemplateId(28, (uint)giveLoot * 3);

                if (item != null)
                    loot.LootItems.Add(new LootItem(item, killer.Player.EntityId, 0));
            }

            return loot;
        }

        internal void Loot(Client client, Creature creature)
        {
            var loot = Create(client, creature);

            client.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(loot.EntityId, loot.EntityClassId));

            AttachInfo(client, loot);
            LootInfo(client, loot);
            OverallQuality(client, loot);
            CanLootItems(client, loot);
        }

        /// <summary>
        /// Attaches a loot dispenser to a scripted world prop (a mission reward crate) instead of
        /// a kill - same client-visible mechanism (AttachInfo/LootInfo/OverallQuality/
        /// CanLootItems) as a corpse, just without any of the kill/corpse semantics Create/Loot
        /// assume; see the AttachedObject branch of TryGetLoot. The rows are real Item instances
        /// (the corpse loot window's GetEntity(itemId) skips any row that does not resolve to
        /// one) but are never saved or added to any inventory here - see ClaimFromObject for why
        /// granting stays the mission's own job.
        /// </summary>
        // Takes mapChannel explicitly rather than reading owner.Player.MapChannel: this is called
        // from MissionScenarioService's scenario-rebuild path, which runs while resolving a
        // reconnecting client's private instance - specifically from inside the expression that
        // CharacterManager.ResolveReconnectMapChannel's caller assigns to client.Player.MapChannel,
        // meaning that property is still null (or stale) for the whole call. Reading it here
        // instead of taking the map the caller already resolved silently no-opped this entire
        // method on every relog - the loot dispenser was never attached, no packets went out, and
        // nothing logged it, which looked exactly like "interacting does nothing".
        internal void AttachRewardLoot(Client owner, MapChannel mapChannel, DynamicObject obj, IReadOnlyList<MissionRewardItem> previewItems)
        {
            if (owner?.Player == null || mapChannel == null || obj == null || obj.LootDispenserEntityId != 0)
            {
                Logger.WriteLog(LogType.Debug,
                    $"[MissionDiag] AttachRewardLoot bailed: hasPlayer={owner?.Player != null} " +
                    $"hasMapChannel={mapChannel != null} hasObj={obj != null} " +
                    $"lootAlready={obj?.LootDispenserEntityId}");
                return;
            }

            var loot = new LootDispenser
            {
                IsLootable = true,
                AttachedTo = obj.EntityId,
                AttachedObject = obj,
                Owner = owner.Player.EntityId,
                OwnerClient = owner,
                Player = owner.Player,
                Map = mapChannel,
                CharacterId = owner.Player.Id,
                AccountId = owner.AccountEntry?.Id ?? 0,
                UnitOfWorkFactory = _gameUnitOfWorkFactory
            };

            foreach (var previewItem in previewItems)
            {
                var item = ItemManager.Instance.CreateFromTemplateId(
                    previewItem.ItemTemplateId, previewItem.Quantity);
                if (item != null)
                    loot.LootItems.Add(new LootItem(item, owner.Player.EntityId, 0));
            }

            lock (mapChannel.LootSyncRoot)
                mapChannel.LootDispensers.Add(loot.EntityId, loot);
            obj.LootDispenserEntityId = loot.EntityId;

            // CreateScenarioDynamicObject hardcodes every scenario prop's initial state to
            // IdStateActive - fine for most of them, wrong for a treasure dispenser, whose closed
            // and opened states (TdStateClosed/TdStateOpened) are what its own FSM and "opening"
            // animation are keyed on (usable.py's _SetState, reached through Recv_ForceState).
            // The object was already broadcast once with the wrong state by the time this runs
            // (EnsureScenarioDynamicObject's AddToWorld already fired), so correct it explicitly
            // rather than relying on the initial creation packet.
            obj.StateId = UseObjectState.TdStateClosed;
            owner.CallMethod(obj.EntityId, new ForceStatePacket(UseObjectState.TdStateClosed, 0));

            owner.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(loot.EntityId, loot.EntityClassId));
            AttachInfo(owner, loot);
            LootInfo(owner, loot);
            OverallQuality(owner, loot);
            CanLootItems(owner, loot);

            // Not disabling Usable here: PhysicalEntity.GetUseActionInfo() breaks Usable-vs-Lootable
            // priority ties in Usable's favor (rcmenuitem.USE_OBJECT sorts before LOOT at the same
            // priority - confirmed via generated/client/rcmenuitem.pyo_dis), so Use wins over Loot
            // whenever both are valid candidates. Disabling Usable to make Loot the only candidate
            // was tried repeatedly (see git history) and reproduced "can't interact with the crate
            // at all" every time, including after the attach-chain bugs elsewhere in this file were
            // fixed and confirmed working via plain corpse loot - so something about this
            // combination reliably breaks client interaction for this class, even though nothing
            // in the decompiled client source conclusively explains why. Use is what a real client
            // will actually send for this object; completion runs off Use, in
            // DynamicObjectManager.FootlockerRecovery, via ClaimFromObject. The dispenser is still
            // attached for the "Lootable" glow FX it drives client-side.
            Logger.WriteLog(LogType.Debug,
                $"[MissionDiag] AttachRewardLoot succeeded: lootEntity={loot.EntityId} attachedTo={obj.EntityId} " +
                $"items={loot.LootItems.Count} map={mapChannel.MapInfo?.MapContextId}");
        }

        /// <summary>
        /// Entry point for FootlockerRecovery (DynamicObjectManager.cs): a Usable+Lootable object
        /// can never actually resolve "Loot" as its client action (see the comment in
        /// AttachRewardLoot), so completion for a reward crate runs off the same generic Use
        /// windup as everything else, and lands here instead of RequestLootAllFromCorpse/
        /// RequestLootItemFromCorpse - the client-driven entry points this same finish normally
        /// comes through for a real corpse.
        /// </summary>
        internal void ClaimFromObject(Client client, ulong lootDispenserEntityId)
        {
            var mapChannel = client?.Player?.MapChannel;
            if (mapChannel == null)
                return;

            LootDispenser loot;
            lock (mapChannel.LootSyncRoot)
                if (!mapChannel.LootDispensers.TryGetValue(lootDispenserEntityId, out loot))
                    return;

            ClaimFromObject(client, loot);
        }

        /// <summary>
        /// Taking loot from an object-attached dispenser never calls Claim/PlanAndSave - the
        /// items shown are display copies, real enough to resolve in a loot window but never
        /// saved to any inventory. What actually grants the reward is the mission's own scripted
        /// GrantRewardPackage step, triggered by the same InteractionUsed progress event fired
        /// here at the end - the one place that grants anything, reached only through the
        /// ClaimFromObject(Client, ulong) overload above (FootlockerRecovery's Use completion),
        /// since a real client can never resolve Loot as this object's action in the first place.
        /// </summary>
        private void ClaimFromObject(Client client, LootDispenser loot)
        {
            var obj = loot.AttachedObject;
            if (obj == null)
                return;

            foreach (var lootItem in loot.LootItems)
                lootItem.Taken = true;
            loot.FullyLooted = true;
            loot.IsLootable = false;
            loot.CurrentLooter = 0;

            MissionManager.TryPublish(
                () => client.CallMethod(loot.EntityId, new ActorGotLootPacket(loot)),
                $"object {loot.EntityId} actor loot result");
            MissionManager.TryPublish(
                () => client.CallMethod(
                    loot.EntityId,
                    new TakenInfoPacket(client.Player.EntityId, Taken(loot))),
                $"object {loot.EntityId} taken state");
            MissionManager.TryPublish(
                () => CanLootItems(client, loot),
                $"object {loot.EntityId} lootability");
            MissionManager.TryPublish(
                () => GotLoot(client, loot),
                $"object {loot.EntityId} completion");

            var mapChannel = client.Player?.MapChannel;
            if (mapChannel != null)
                lock (mapChannel.LootSyncRoot)
                    mapChannel.LootDispensers.Remove(loot.EntityId);

            // TdStateOpened is what usable.py's own FSM plays the "opening" animation on
            // entering (Recv_ForceState -> _SetState -> _OnEnteredState) - forced here rather
            // than left to a client-driven transition, since the generic Use windup path
            // (FootlockerRecovery) that reaches this method for an object-attached dispenser
            // does not otherwise transition object state on its own.
            obj.StateId = UseObjectState.TdStateOpened;
            MissionManager.TryPublish(
                () => CellManager.Instance.CellCallMethod(
                    obj, new ForceStatePacket(UseObjectState.TdStateOpened, 0)),
                $"object {obj.EntityId} opened state");

            // Cleared before RecordProgress: a one-shot object stops answering Use/Loot the
            // moment its dispenser empties, not only once the mission's own DespawnDynamicObject
            // step eventually removes it a beat later.
            obj.IsEnabled = false;
            obj.LootDispenserEntityId = 0;

            (_missionManager ?? MissionManager.Instance).RecordProgress(
                client,
                MissionProgressEvent.Interaction((uint)obj.EntityClassId));
        }

        /// <summary>
        /// Drops every dispenser attached to a creature that is leaving the world: out of the
        /// map's table, off the owner's screen if they are still here, and its entity id freed.
        /// </summary>
        internal void RemoveForCreature(MapChannel mapChannel, Creature creature)
        {
            if (mapChannel == null || creature == null)
                return;

            RetryPendingRetirements();
            var notices = new List<RetirementNotice>();
            lock (mapChannel.LootSyncRoot)
                foreach (var loot in mapChannel.LootDispensers.Values
                             .Where(entry => ReferenceEquals(entry.Corpse, creature) ||
                                             entry.AttachedTo == creature.EntityId).ToArray())
                    notices.Add(Retire(mapChannel, loot, true));
            Publish(notices);
        }

        internal void RemoveForOwner(MapChannel mapChannel, Client client)
        {
            if (mapChannel == null || client == null)
                return;

            RetryPendingRetirements();
            var notices = new List<RetirementNotice>();
            lock (client.SyncRoot)
            {
                lock (mapChannel.LootSyncRoot)
                    foreach (var loot in mapChannel.LootDispensers.Values
                                 .Where(entry => ReferenceEquals(entry.OwnerClient, client) ||
                                                 entry.Owner == client.Player?.EntityId).ToArray())
                        notices.Add(Retire(mapChannel, loot, true));
                Publish(notices);
            }
        }

        /// <summary>
        /// The client has used a corpse and wants the window. Answering with LootCorpse is what
        /// opens it; while this was a stub, nothing ever did, which is why the two per-item
        /// methods had never been reachable.
        /// </summary>
        internal void RequestCorpseLooting(Client client, RequestCorpseLootingPacket packet)
        {
            if (client == null || packet == null)
                return;

            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return;

                lock (map.LootSyncRoot)
                {
                    if (!TryGetLoot(client, packet.EntityId, out var loot))
                        return;

                    var remaining = loot.Remaining();
                    foreach (var lootItem in remaining)
                        if (lootItem.Item != null)
                            ItemManager.Instance.SendItemDataToClient(client, lootItem.Item, false);

                    loot.CurrentLooter = client.Player.EntityId;

                    LootInfo(client, loot);
                    CanLootItems(client, loot);
                    client.CallMethod(loot.EntityId,
                        new LootCorpsePacket(client.Player.EntityId, remaining));
                }
            }
        }

        /// <summary>
        /// The window has closed. The client sends this on any close, not only a deliberate
        /// cancel, and expects no answer - so this only lets go of the corpse.
        /// </summary>
        internal void CancelCorpseLooting(Client client, CancelCorpseLootingPacket packet)
        {
            if (client == null || packet == null)
                return;

            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return;

                lock (map.LootSyncRoot)
                    if (map.LootDispensers.TryGetValue(packet.EntityId, out var loot) &&
                        loot.CurrentLooter == client.Player.EntityId)
                        loot.CurrentLooter = 0;
            }
        }

        /// <summary>Takes one item off a corpse.</summary>
        internal void RequestLootItemFromCorpse(Client client, RequestLootItemFromCorpsePacket packet)
        {
            if (client == null || packet == null)
                return;

            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return;

                lock (map.LootSyncRoot)
                {
                    if (!TryGetLoot(client, packet.EntityId, out var loot))
                        return;

                    var lootItem = loot.Find(packet.ItemId);
                    if (lootItem == null || lootItem.Taken)
                    {
                        client.CallMethod(loot.EntityId,
                            new TakenInfoPacket(client.Player.EntityId, Taken(loot)));
                        return;
                    }

                    // A reward crate is one package, not a real itemized loot table - taking any
                    // one row takes the whole thing, same as Loot All. See ClaimFromObject.
                    if (loot.AttachedObject != null)
                        ClaimFromObject(client, loot);
                    else
                        Claim(client, loot, new[] { lootItem }, packet.DestSlot, false);
                }
            }
        }

        /// <summary>
        /// The client sends this two ways and they are not the same request.
        ///
        /// The Loot All button sends autoLootOnly false: take everything.
        ///
        /// Walking near a corpse sends it with autoLootOnly **true**, from lootdispenser's
        /// _UpdateTick, which runs every frame while a dispenser is attached and fires the
        /// moment the player is inside the corpse's auto-loot radius. Nobody clicked anything.
        /// That one means "take what I said I would pick up automatically", which is items at or
        /// below the player's auto-loot threshold - Junk by default, since that is what the
        /// client's own option defaults to.
        ///
        /// Treating them alike is why looting felt random: walking over a body silently emptied
        /// it, so the window either never opened or opened onto a corpse that had already been
        /// cleared out from under it.
        /// </summary>
        internal void RequestLootAllFromCorpse(Client client, RequestLootAllFromCorpsePacket packet)
        {
            if (client == null || packet == null)
                return;

            lock (client.SyncRoot)
            {
                var map = client.Player?.MapChannel;
                if (map == null)
                    return;

                lock (map.LootSyncRoot)
                {
                    if (!TryGetLoot(client, packet.EntityId, out var loot))
                        return;

                    if (loot.AttachedObject != null)
                    {
                        ClaimFromObject(client, loot);
                        return;
                    }

                    var threshold = client.Player.AutoLootThreshold;
                    var selected = loot.Remaining()
                        .Where(item => !packet.AutoLootOnly || WithinThreshold(item, threshold))
                        .ToArray();
                    Claim(client, loot, selected, null, true);
                }
            }
        }

        /// <summary>Whether walking past a corpse should pick this item up unasked.</summary>
        private static bool WithinThreshold(LootItem lootItem, LootQuality threshold)
        {
            var quality = (LootQuality)(lootItem.Item?.ItemTemplate?.QualityId ?? 0);

            // Rank, not the raw id: the ids are the client's and Junk is the largest of them.
            return quality.Rank() <= threshold.Rank();
        }

        /// <summary>
        /// The best quality this player's client will pick up by walking over a corpse. Sent at
        /// login and whenever the option changes; it was a logged ToDo, so every player was
        /// treated as if they had asked for everything.
        /// </summary>
        internal void SetAutoLootThreshold(Client client, SetAutoLootThresholdPacket packet)
        {
            if (client.Player == null)
                return;

            var threshold = (LootQuality)packet.LootLevel;

            // The client only ever sends one of its five option values, but the packet is the
            // client's word: an unknown one would rank as int.MaxValue and auto-loot everything.
            if (!Enum.IsDefined(typeof(LootQuality), threshold) || threshold == LootQuality.Mission)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry?.Id} sent auto-loot threshold {packet.LootLevel}, which is not a quality.");
                return;
            }

            client.Player.AutoLootThreshold = threshold;
        }

        private void Claim(
            Client client,
            LootDispenser loot,
            IReadOnlyList<LootItem> items,
            uint? destSlot,
            bool includeCredits)
        {
            var currentCredits = client.Player.Credits.GetValueOrDefault(CurencyType.Credits);
            var creditsGranted = includeCredits && loot.Credits != 0;
            int creditsAfter;
            try
            {
                creditsAfter = checked(currentCredits + (includeCredits ? loot.Credits : 0));
            }
            catch (OverflowException)
            {
                return;
            }

            var grant = new InventoryManager.LootGrant(_beforeItemPublication);
            var missionManager = _missionManager ?? MissionManager.Instance;
            var progressPlan =
                MissionManager.MissionProgressPublicationPlan.Empty;
            try
            {
                var factory = loot.UnitOfWorkFactory ?? _gameUnitOfWorkFactory;
                if (factory == null)
                    throw new GameplayRejectionException(
                        "No character persistence factory is available for loot claim.");
                using var unitOfWork = factory.CreateChar();
                unitOfWork.ExecuteTransaction(() =>
                {
                    var character = unitOfWork.Characters.Find(client.Player.Id);
                    if (character == null ||
                        character.AccountId != client.AccountEntry.Id ||
                        character.Credit != currentCredits)
                        throw new GameplayRejectionException(
                            "Durable character ownership or credits changed.");

                    grant.PlanAndSave(client, items, unitOfWork, destSlot);
                    progressPlan = missionManager.PlanProgress(
                        client,
                        items
                            .GroupBy(item => item.ItemClassId)
                            .Select(group =>
                                MissionProgressEvent.ItemAcquired(
                                    group.Key,
                                    group.Aggregate(
                                        0U,
                                        (total, item) => checked(
                                            total + item.ItemQuantity))))
                            .ToArray(),
                        unitOfWork);

                    if (includeCredits && loot.Credits != 0)
                        unitOfWork.Characters.UpdateCharacterCredits(
                            client.Player.Id, creditsAfter);

                    if (!TryGetLoot(client, loot.EntityId, out var current) ||
                        !ReferenceEquals(current, loot))
                        throw new GameplayRejectionException(
                            "Corpse state changed during the claim.");
                });
            }
            catch (Exception error) when (
                error is GameplayRejectionException ||
                error is DbUpdateException ||
                error is DbException)
            {
                Logger.WriteLog(LogType.Error,
                    $"Corpse loot claim failed for character {client.Player.Id}: {error.Message}");
                return;
            }

            grant.Publish(client);

            if (creditsGranted)
            {
                loot.Credits = 0;
                client.Player.Credits[CurencyType.Credits] = creditsAfter;
            }

            if (!loot.HasLoot)
            {
                loot.FullyLooted = true;
                loot.IsLootable = false;
                loot.CurrentLooter = 0;
            }

            if (creditsGranted)
                MissionManager.TryPublish(
                    () => client.CallMethod(
                        client.Player.EntityId,
                        new UpdateCreditsPacket(
                            CurencyType.Credits,
                            creditsAfter,
                            0)),
                    $"corpse {loot.EntityId} credits");
            MissionManager.TryPublish(
                () => client.CallMethod(
                    loot.EntityId,
                    new ActorGotLootPacket(loot)),
                $"corpse {loot.EntityId} actor loot result");
            MissionManager.TryPublish(
                () => client.CallMethod(
                    loot.EntityId,
                    new TakenInfoPacket(
                        client.Player.EntityId,
                        Taken(loot))),
                $"corpse {loot.EntityId} taken state");
            MissionManager.TryPublish(
                () => CanLootItems(client, loot),
                $"corpse {loot.EntityId} lootability");
            if (!loot.HasLoot)
                MissionManager.TryPublish(
                    () => GotLoot(client, loot),
                    $"corpse {loot.EntityId} completion");

            progressPlan.Publish(client);
        }

        private bool TryGetLoot(Client client, ulong entityId, out LootDispenser loot)
        {
            loot = null;
            var player = client?.Player;
            var map = player?.MapChannel;
            var limit = client == null ? double.NaN : _distance(client);

            if (client == null || player == null || map == null ||
                client.State != ClientState.Ingame ||
                !double.IsFinite(limit) || limit <= 0 ||
                player.State == CharacterState.Dead ||
                !player.Attributes.TryGetValue(Attributes.Health, out var health) ||
                health.Current <= 0 ||
                EntityManager.Instance.GetEntityType(player.EntityId) != EntityType.Character ||
                !EntityManager.Instance.Players.TryGetValue(player.EntityId, out var registeredPlayer) ||
                !ReferenceEquals(player, registeredPlayer) ||
                !map.LootDispensers.TryGetValue(entityId, out loot) ||
                !loot.IsLootable || loot.FullyLooted ||
                loot.Owner != player.EntityId ||
                (loot.OwnerClient != null && !ReferenceEquals(loot.OwnerClient, client)) ||
                (loot.Player != null && !ReferenceEquals(loot.Player, player)) ||
                (loot.Map != null && !ReferenceEquals(loot.Map, map)) ||
                (loot.CharacterId != 0 && loot.CharacterId != player.Id) ||
                (loot.AccountId != 0 && loot.AccountId != client.AccountEntry?.Id) ||
                !IsFinite(player.Position))
                return false;

            // A scripted prop (a mission reward crate) instead of something killed - no corpse,
            // no kill-ownership semantics, just: is it still the same object, still in the world,
            // still in range.
            if (loot.AttachedObject != null)
            {
                var attachedObject = loot.AttachedObject;
                return attachedObject.LootDispenserEntityId == loot.EntityId &&
                    attachedObject.IsInWorld &&
                    attachedObject.MapContextId == player.MapContextId &&
                    map.DynamicObjects.Contains(attachedObject) &&
                    IsFinite(attachedObject.Position) &&
                    Vector3.Distance(player.Position, attachedObject.Position) <= limit;
            }

            if (!EntityManager.Instance.Creatures.TryGetValue(loot.AttachedTo, out var corpse) ||
                EntityManager.Instance.GetEntityType(corpse.EntityId) != EntityType.Creature ||
                (loot.Corpse != null && !ReferenceEquals(loot.Corpse, corpse)) ||
                corpse.CorpseLootEntityId != loot.EntityId ||
                corpse.State != CharacterState.Dead ||
                !corpse.Attributes.TryGetValue(Attributes.Health, out var corpseHealth) ||
                corpseHealth.Current > 0 ||
                corpse.MapContextId != player.MapContextId ||
                !map.MapCellInfo.Cells.Values.Any(cell => cell.CreatureList.Contains(corpse)) ||
                !IsFinite(corpse.Position) ||
                HasExpired(loot, corpse))
                return false;

            return Vector3.Distance(player.Position, corpse.Position) <= limit;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);

        private RetirementNotice Retire(MapChannel map, LootDispenser loot, bool notify)
        {
            if (!map.LootDispensers.TryGetValue(loot.EntityId, out var current) ||
                !ReferenceEquals(current, loot))
                return null;

            var unclaimed = loot.LootItems
                .Where(item => !item.Taken && item.Item?.Id > 0)
                .Select(item => (ItemId: item.Item.Id, EntityId: item.EntityId))
                .Distinct()
                .ToArray();

            var durableCleanupSucceeded = true;
            if (unclaimed.Length > 0)
            {
                try
                {
                    var factory = loot.UnitOfWorkFactory ?? _gameUnitOfWorkFactory;
                    if (factory == null)
                        throw new GameplayRejectionException(
                            "No character persistence factory is available for loot cleanup.");
                    using var unitOfWork = factory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        foreach (var item in unclaimed)
                        {
                            unitOfWork.CharacterInventories.DeleteInvItemByItemId(item.ItemId);
                            unitOfWork.Items.DeleteItem(item.ItemId);
                        }
                    });
                }
                catch (Exception error) when (
                    error is GameplayRejectionException ||
                    error is DbUpdateException ||
                    error is DbException)
                {
                    durableCleanupSucceeded = false;
                    Logger.WriteLog(LogType.Error,
                        $"Could not delete {unclaimed.Length} unclaimed loot item row(s): {error.Message}");
                }
            }

            loot.IsLootable = false;
            loot.FullyLooted = true;
            loot.CurrentLooter = 0;

            var owner = loot.OwnerClient ??
                        map.ClientList.Find(client => client.Player?.EntityId == loot.Owner);
            var notice = notify && owner != null
                ? new RetirementNotice
                {
                    Client = owner,
                    LootEntityId = loot.EntityId,
                    CanLoot = new CanLootItemsPacket(false, loot.LootItems),
                    Destroy = new DestroyPhysicalEntityPacket(loot.EntityId)
                }
                : null;

            foreach (var item in loot.LootItems)
                if (!item.Taken && item.Item != null)
                {
                    EntityManager.Instance.UnregisterEntity(item.EntityId);
                    EntityManager.Instance.UnregisterItem(item.EntityId);
                    if (durableCleanupSucceeded)
                        EntityManager.Instance.FreeEntity(item.EntityId);
                }

            if (!durableCleanupSucceeded)
                QueueRetirement(loot.EntityId, loot.UnitOfWorkFactory ?? _gameUnitOfWorkFactory,
                    unclaimed.Select(item => item.ItemId).ToArray(),
                    unclaimed.Select(item => item.EntityId).ToArray());

            loot.LootItems.Clear();
            loot.Credits = 0;
            map.LootDispensers.Remove(loot.EntityId);

            if (loot.Corpse?.CorpseLootEntityId == loot.EntityId)
                loot.Corpse.CorpseLootEntityId = 0;

            return notice;
        }

        private static long LifetimeLimit(LootDispenser loot)
        {
            if (loot.CurrentLooter != 0)
                return BeingLootedCorpseMs;
            return loot.HasLoot ? LootableCorpseMs : EmptyCorpseMs;
        }

        private static bool HasExpired(
            LootDispenser loot,
            Creature corpse,
            long observedDeadTime = 0) =>
            loot == null ||
            corpse?.Controller == null ||
            Math.Max(observedDeadTime, corpse.Controller.DeadTime) >=
            LifetimeLimit(loot);

        private void QueueRetirement(
            ulong lootEntityId,
            IGameUnitOfWorkFactory factory,
            uint[] itemIds,
            ulong[] entityIds)
        {
            lock (_retirementSyncRoot)
                _pendingRetirements[lootEntityId] = new PendingRetirement
                {
                    Factory = factory,
                    ItemIds = itemIds,
                    EntityIds = entityIds
                };
        }

        private void RetryPendingRetirements()
        {
            PendingRetirement[] pending;
            lock (_retirementSyncRoot)
            {
                pending = _pendingRetirements.Values
                    .Where(entry => !entry.InProgress)
                    .ToArray();
                foreach (var entry in pending)
                    entry.InProgress = true;
            }

            foreach (var entry in pending)
            {
                var succeeded = false;
                try
                {
                    if (entry.Factory == null)
                        throw new GameplayRejectionException(
                            "No character persistence factory is available for loot cleanup retry.");
                    using var unitOfWork = entry.Factory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        foreach (var itemId in entry.ItemIds)
                        {
                            unitOfWork.CharacterInventories.DeleteInvItemByItemId(itemId);
                            unitOfWork.Items.DeleteItem(itemId);
                        }
                    });
                    succeeded = true;
                }
                catch (Exception error) when (
                    error is GameplayRejectionException ||
                    error is DbUpdateException ||
                    error is DbException)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Could not retry unclaimed loot cleanup: {error.Message}");
                }

                lock (_retirementSyncRoot)
                {
                    var pair = _pendingRetirements.FirstOrDefault(candidate =>
                        ReferenceEquals(candidate.Value, entry));
                    if (succeeded && pair.Value != null)
                    {
                        _pendingRetirements.Remove(pair.Key);
                        foreach (var entityId in entry.EntityIds)
                            EntityManager.Instance.FreeEntity(entityId);
                    }
                    else
                    {
                        entry.InProgress = false;
                    }
                }
            }
        }

        private static void Publish(IEnumerable<RetirementNotice> notices)
        {
            foreach (var notice in notices.Where(entry => entry?.Client != null))
                if (notice.Client.State == ClientState.Ingame)
                {
                    notice.Client.CallMethod(notice.LootEntityId, notice.CanLoot);
                    notice.Client.CallMethod(SysEntity.ClientMethodId, notice.Destroy);
                }
        }

        /// <summary>The rows TakenInfo should mark; the client keys off the ones it is sent.</summary>
        private static List<LootItem> Taken(LootDispenser loot) => loot.LootItems.FindAll(i => i.Taken);
    }
}
