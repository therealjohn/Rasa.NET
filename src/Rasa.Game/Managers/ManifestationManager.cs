using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets;
    using Packets.Communicator.Server;
    using Packets.Game.Server;
    using Packets.Manifestation.Server;
    using Packets.MapChannel.Client;
    using Packets.MapChannel.Server;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;
    public class ManifestationManager
    {
        /* Actor: Player "bodies" (ManifestationClass)
         * 
         *    Manifestation Packets:
         *  - CurrentCharacterId                => implemented
         *  - AllCredits                        => implemented
         *  - UpdateCredits                     => implemented
         *  - LockboxFunds                      => implemented
         *  - WonBattleground                   => ToDo
         *  - LostBattleground                  => ToDo
         *  - WeaponDrawerSlot                  => implemented
         *  - AbilityDrawerSlot                 => implemented
         *  - AbilityDrawer                     => implemented
         *  - ArmWeaponFailed                   => ToDo
         *  - ArmAbilityFailed                  => ToDo
         *  - AdvancementStats                  => implemented
         *  - ExperienceChanged                 => ToDo
         *  - LevelChanged                      => ToDo
         *  - CharacterClass                    => implemented
         *  - AvailableAllocationPoints
         *  - AvailableCharacterClasses
         *  - TierAdvancementInfo
         *  - InvitedToJoinFriend
         *  - InvitationDeclined
         *  - InvitationCancelled
         *  - CannotInvite
         *  - InvitedToAddAndJoinFriend
         *  - RequestToJoin
         *  - JoinFriendDeclined
         *  - JoinFriendCancelled
         *  - CannotJoin
         *  - ForceConverse
         *  - LogosStoneTabula
         *  - LogosStoneAdded
         *  - LogosStoneRemoved
         *  - ShowHelmetChanged
         *  - Titles
         *  - TitleChanged
         *  - TitleAdded
         *  - TitleRemoved
         *  - PlayerFlags
         *  - CloneCredits
         *  - WaypointGained
         *  - GraveyardGained
         *  - CharacterName
         *  - RaceId
         *  - PlayerAfk
         *  - PlayerInactiveWarning
         *  - ClanId
         *  - IsTrialAccount
         *  - PlayerEnteredCombat
         *  - PlayerExitedCombat
         *  - MinionAdded
         *  - MinionStayAck
         *  - MinionGoAck
         *  - MinionFollowMeAck
         *  - MinionFollowTargetAck
         *  - MinionTargetMeAck
         *  - MinionTargetAck
         *  - MinionAssistMeAck
         *  - MinionAssistTargetAck
         *  - MinionTemperamentAck
         *  - MinionCommandAck
         *  
         *  Manifestation Handlrs:
         *  - AutoFireKeepAlive         => ToDo
         *  - ChangeShowHelmet          => ToDo
         *  - ChangeTitle               => implemented, but need more work on it
         *  - RespondToAddAndJoinFriend => ToDo
         *  - RespondToJoinFriend       => ToDo
         *  - RespondToRequestToJoin    => ToDo
         *  - RequestArmAbility         => implemented
         *  - RequestArmWeapon          => implemented
         *  - RequestSetAbilitySlot     => implemented
         *  - RequestSwapAbilitySlots   => implemented
         *  - StartAutoFire             => implemented, but need more work on it
         *  - StopAutoFire              => implemented, but need more work on it
         */
        private static ManifestationManager _instance;
        private static readonly object InstanceLock = new object();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly Action<Client> _disconnect;
        private readonly Func<long> _weaponClock;

        private static List<AutoFireTimer> AutoFire = new List<AutoFireTimer>();
        public static byte MaxPlayerLevel = 50;
        public static ManifestationManager Instance
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new ManifestationManager(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        internal ManifestationManager(IGameUnitOfWorkFactory gameUnitOfWorkFactory)
            : this(gameUnitOfWorkFactory, null)
        {
        }

        internal ManifestationManager(IGameUnitOfWorkFactory gameUnitOfWorkFactory, Action<Client> disconnect,
            Func<long> weaponClock = null, Func<int, int, int> abilityRoll = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _disconnect = disconnect ?? (client => client.Close(false));
            _weaponClock = weaponClock ?? (() => Environment.TickCount64);
            _loadout = new AbilityLoadoutManager(this, gameUnitOfWorkFactory);
            Abilities = new AbilityManager(_loadout, _weaponClock, abilityRoll);
        }

        private readonly AbilityLoadoutManager _loadout;
        internal AbilityManager Abilities { get; }

        // constant skillId data
        public readonly int[] SkillIById = {
            1,8,14,19,20,21,22,23,24,
            25,26,28,30,31,32,34,35,
            36,37,39,40,43,47,48,49,
            50,54,55,57,58,63,66,67,
            68,72,73,77,79,80,82,89,
            92,102,110,111,113,114,121,135,
            136,147,148,149,150,151,152,153,
            154,155,156,157,158,159,160,161,
            162,163,164,165,166,172,173,174
        };
        // table for skillId to skillIndex mapping
        private readonly int[] SkillId2Idx =
        {
            -1,0,-1,-1,-1,-1,-1,-1,1,-1,-1,-1,-1,-1,2,-1,-1,-1,-1,3,
            4,5,6,7,8,9,10,-1,11,-1,12,13,14,-1,15,16,17,18,-1,19,
            20,-1,-1,21,-1,-1,-1,22,23,24,25,-1,-1,-1,26,27,-1,28,29,-1,
            -1,-1,-1,30,-1,-1,31,32,33,-1,-1,-1,34,35,-1,-1,-1,36,-1,37,
            38,-1,39,-1,-1,-1,-1,-1,-1,40,-1,-1,41,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,42,-1,-1,-1,-1,-1,-1,-1,43,44,-1,45,46,-1,-1,-1,-1,-1,
            -1,47,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,48,49,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,50,51,52,53,54,55,56,57,58,59,60,61,62,
            63,64,65,66,67,68,69,-1,-1,-1,-1,-1,70,71,72,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1
        };
        // table for skillIndex to ability mapping
        public readonly int[] SkillIdx2AbilityId =
        {
            -1, -1, -1, -1, 137, -1, -1, -1, -1, 178, 177, 158, -1, -1,
            197, 186, 188, 162, 187, -1, -1, 233, 234, -1, 194, -1, -1,
            -1, -1, -1, 301, -1, -1, 185, 251, 240, 302, 232, 229, -1,
            231, 305, 392, 252, 282, 381, 267, 298, 246, 253, 307, 393,
            281, 390, 295, 304, 386, 193, 385, 176, 260, 384, 383, 303,
            388, 389, 387, 380, 401, 430, 262, 421, 446
        };

        public readonly int[] requiredSkillLevelPoints = { 0, 1, 3, 6, 10, 15 };

        #region Handlers
        public void AutoFireKeepAlive(Client client, int keepAliveDelay)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
                lock (AutoFire)
                    foreach (var timer in AutoFire)
                        if (timer.Client == client)
                            timer.MaxAliveTime = Math.Max(0L, (long)keepAliveDelay * 4);
        }

        public void ChangeShowHelmet(Client client, ChangeShowHelmetPacket packet)
        {
            Logger.WriteLog(LogType.Debug, "ToDo ChangeShowHelmet");
        }

        public void ChangeTitle(Client client, uint titleId)
        {
            //if (titleId != 0)
            //{
            client.Player.CurrentTitle = titleId;
            client.CallMethod(client.Player.EntityId, new TitleChangedPacket(titleId));
            /*}
            else
            {
                client.SendPacket(client.MapClient.Player.Actor.EntityId, new TitleRemovedPacket(client.MapClient.Player.CurrentTitle));
                client.MapClient.Player.CurrentTitle = titleId;
            }

            client.MapClient.Player.CurentTitle = titleId;*/
        }

        public bool PlayerTryFireWeapon(Client client)
        {
            if (client == null)
                return false;
            lock (client.SyncRoot)
                return TryFireWeapon(client);
        }

        private bool TryFireWeapon(Client client)
        {
            if (!CanUseWeapons(client) ||
                !InventoryManager.TryGetEquippedWeapon(client, out var weapon, out var weaponClassInfo))
            {
                Logger.WriteLog(LogType.Network, "Ignored weapon fire without an active, living owner and equipped weapon.");
                return false;
            }
            if (client.Player.PendingWeaponAction != null || client.Player.PendingAbility != null ||
                _weaponClock() < weapon.NextWeaponFireTime)
                return false;
            if (!MissileManager.TryGetTarget(client.Player.MapChannel, client.Player.Target, out _))
                return false;
            // ToDo: isOverheated, isJammed, and some other checks
            if (!client.Player.WeaponReady)
            {
                RequestWeaponDraw(client);
                return false;
            }

            // do we need to reload?
            if (weapon.CurrentAmmo < weapon.ItemTemplate.WeaponInfo.AmmoPerShot)
            {
                RequestWeaponReload(client, true);
                return false;
            }

            var remainingAmmo = weapon.CurrentAmmo - weapon.ItemTemplate.WeaponInfo.AmmoPerShot;
            try
            {
                using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                unitOfWork.ExecuteTransaction(() =>
                {
                    InventoryManager.RequireInventoryItem(unitOfWork.CharacterInventories.GetItems(client.AccountEntry.Id),
                        client.Player.Id, weapon.Id, InventoryType.WeaponDrawerInventory, client.Player.ActiveWeapon);
                    ItemManager.SaveWeaponAmmo(unitOfWork, weapon, remainingAmmo);
                });
            }
            catch (Exception exception) when (GameplayRejectionException.IsExpected(exception))
            {
                Logger.WriteLog(LogType.Error, $"Weapon fire save failed for item {weapon.Id}: {exception}");
                return false;
            }

            weapon.CurrentAmmo = remainingAmmo;
            weapon.NextWeaponFireTime = _weaponClock() + weapon.ItemTemplate.WeaponInfo.Refire;
            client.CallMethod(weapon.EntityId, new WeaponAmmoInfoPacket(remainingAmmo));

            // let's calculate damage
            var damageRange = weaponClassInfo.MaxDamage - weaponClassInfo.MinDamage;
            var damage = weaponClassInfo.MinDamage + new Random().Next(0, damageRange + 1);
            var action = new ActionData(client.Player, weaponClassInfo.WeaponAttackActionId, weaponClassInfo.WeaponAttackArgId, client.Player.Target, 0);
            // launch correct missile type depending on weapon type
            MissileManager.Instance.MissileLaunch(client.Player.MapChannel, action, damage);
            
            return true;
        }

        public void RequestArmAbility(Client client, int abilityDrawerSlot)
        {
            _loadout.Select(client, abilityDrawerSlot);
        }

        public void RequestArmWeapon(Client client, uint requestedWeaponDrawerSlot)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
            {
                if (!CanUseWeapons(client) || requestedWeaponDrawerSlot >= client.Player.Inventory.WeaponDrawer.Count ||
                    !InventoryManager.TryGetWeapon(client, client.Player.Inventory.WeaponDrawer[(int)requestedWeaponDrawerSlot],
                        out var weapon, out _) || weapon.OwnerSlotId != requestedWeaponDrawerSlot)
                    return;
                var equipInfo = EntityClassManager.Instance.GetEquipableClassInfo(weapon);
                if (equipInfo == null)
                    return;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        if (!unitOfWork.CharacterInventories.GetItems(client.AccountEntry.Id).Any(slot =>
                            slot.CharacterId == client.Player.Id && slot.ItemId == weapon.Id &&
                            slot.InventoryType == (uint)InventoryType.WeaponDrawerInventory && slot.SlotId == requestedWeaponDrawerSlot))
                            throw new GameplayRejectionException("Requested weapon is not in the character's drawer.");
                        unitOfWork.CharacterAppearances.AddOrUpdate(client.Player.Id,
                            new CharacterAppearanceEntry((uint)equipInfo.EquipmentSlotId, (uint)weapon.ItemTemplate.Class, weapon.Color));
                        unitOfWork.Characters.UpdateCharacterActiveWeapon(client.Player.Id, (byte)requestedWeaponDrawerSlot);
                    });
                }
                catch (Exception exception) when (GameplayRejectionException.IsExpected(exception))
                {
                    Logger.WriteLog(LogType.Error, $"Weapon arm save failed for item {weapon.Id}: {exception}");
                    return;
                }

                CancelWeaponAction(client);
                AbilityManager.CancelPending(client);
                CancelAutoFire(client);
                client.Player.ActiveWeapon = (byte)requestedWeaponDrawerSlot;
                client.Player.Inventory.EquippedInventory[13] = weapon.EntityId;
                ApplyWeaponAppearance(client, weapon, equipInfo.EquipmentSlotId);
                client.CallMethod(client.Player.EntityId, new WeaponDrawerSlotPacket(requestedWeaponDrawerSlot, true));
                NotifyEquipmentUpdate(client);
                UpdateAppearance(client);
                client.CallMethod(weapon.EntityId, new WeaponAmmoInfoPacket(weapon.CurrentAmmo));
            }
        }

        internal static void ApplyWeaponAppearance(Client client, Item weapon, EquipmentData slot)
        {
            client.Player.AppearanceData[slot] = new AppearanceData
            {
                SlotId = slot, Class = weapon == null ? 0 : (uint)weapon.ItemTemplate.Class,
                Color = new Color(weapon?.Color ?? 0), Hue2 = new Color(weapon?.Color ?? 0)
            };
        }

        public void RequestSetAbilitySlot(Client client, RequestSetAbilitySlotPacket packet)
        {
            _loadout.Set(client, packet);
        }

        public void RequestSwapAbilitySlots(Client client, RequestSwapAbilitySlotsPacket packet)
        {
            _loadout.Swap(client, packet);
        }

        public void StartAutoFire(Client client, double yaw)
        {
            // ToDo:
            // yaw is probobly used to mach player and target orientation,
            // some creatures recive more damage from back then from front

            if (client == null)
                return;
            lock (client.SyncRoot)
            {
                if (PlayerTryFireWeapon(client))
                {
                    ActorManager.Instance.RequestVisualCombatMode(client, true);
                    RegisterAutoFire(client);
                }
            }
        }

        public void StopAutoFire(Client client)
        {
            if (CanUseWeapons(client))
                ActorManager.Instance.RequestVisualCombatMode(client, false);
            CancelAutoFire(client);
        }

        internal void CancelAutoFire(Client client)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
                lock (AutoFire)
                    AutoFire.RemoveAll(timer => timer.Client == client);
        }

        private static bool IsActiveWorldClient(Client client)
        {
            return client?.State == ClientState.Ingame && client.PendingTransfer == null &&
                client.Player?.MapChannel != null && !client.Player.Disconected &&
                !client.Player.RemoveFromMap && CellManager.Instance.IsInWorld(client);
        }

        internal static bool CanUseWeapons(Client client)
        {
            return IsActiveWorldClient(client) && client.Player.State != CharacterState.Dead &&
                client.Player.State != CharacterState.Dying &&
                client.Player.MapChannel.ClientList.Contains(client) &&
                client.Player.MapContextId == client.Player.MapChannel.MapInfo.MapContextId &&
                EntityManager.Instance.Players.TryGetValue(client.Player.EntityId, out var registered) &&
                ReferenceEquals(client.Player, registered) &&
                (!client.Player.Attributes.TryGetValue(Attributes.Health, out var health) || health.Current > 0);
        }

        internal static void CancelWeaponAction(Client client)
        {
            if (client?.Player == null)
                return;
            lock (client.SyncRoot)
            {
                var action = client.Player.PendingWeaponAction;
                if (action == null)
                    return;
                action.WeaponActionCompleted = true;
                action.WeaponMap?.PerformRecovery.Remove(action);
                client.Player.PendingWeaponAction = null;
            }
        }

        internal static void CancelCombatActions(Client client)
        {
            if (client?.Player == null)
                return;
            lock (client.SyncRoot)
            {
                CancelWeaponAction(client);
                AbilityManager.CancelPending(client);
                GameEffectManager.Instance.CancelSprint(client);
                Instance.CancelAutoFire(client);
                // Local travel ends combat work without ending corpse-loot ownership.
                client.Player.InvalidateActionLifetime();
            }
        }

        private static bool TakeWeaponAction(ActionData action)
        {
            var client = action?.WeaponClient;
            if (client == null || action.WeaponActionCompleted)
                return false;
            var valid = !action.IsInrerrupted && ReferenceEquals(client.Player, action.Actor) &&
                client.Player.ActionLifetime == action.WeaponActorLifetime &&
                ReferenceEquals(client.Player.PendingWeaponAction, action) &&
                ReferenceEquals(client.Player.MapChannel, action.WeaponMap) &&
                action.WeaponMap.PerformRecovery.Contains(action) && CanUseWeapons(client) &&
                InventoryManager.TryGetEquippedWeapon(client, out var weapon, out _) && ReferenceEquals(weapon, action.Weapon);
            action.WeaponActionCompleted = true;
            action.WeaponMap.PerformRecovery.Remove(action);
            if (ReferenceEquals(client.Player.PendingWeaponAction, action))
                client.Player.PendingWeaponAction = null;
            return valid;
        }

        #endregion

        #region Helper Functions

        public void AllocateAttributePoints(Client client, AllocateAttributePointsPacket packet)
        {
            if (client == null)
                return;

            lock (client.SyncRoot)
            {
                if (!IsActiveWorldClient(client))
                {
                    Logger.WriteLog(LogType.Network, "Ignored attribute allocation outside the active world state.");
                    return;
                }

                var player = client.Player;
                if (!ValidateProgressionForClient(client))
                    return;
                if (packet == null || packet.Body < 0 || packet.Mind < 0 || packet.Spirit < 0 ||
                    player.SpentBody < 0 || player.SpentMind < 0 || player.SpentSpirit < 0)
                {
                    Logger.WriteLog(LogType.Network, $"Rejected invalid attribute allocation for character {player.Id}.");
                    return;
                }

                var body = (long)player.SpentBody + packet.Body;
                var mind = (long)player.SpentMind + packet.Mind;
                var spirit = (long)player.SpentSpirit + packet.Spirit;
                if (body + mind + spirit > 3 * (player.Level - 1))
                {
                    Logger.WriteLog(LogType.Network, $"Rejected over-budget attribute allocation for character {player.Id}.");
                    return;
                }

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.Characters.UpdateCharacterAttributes(player.Id, (int)body, (int)mind, (int)spirit);
                }
                catch (Exception error) when (error is DbUpdateException || error is DbException)
                {
                    Logger.WriteLog(LogType.Error, $"Unable to save attribute allocation for character {player.Id}: {error.Message}");
                    return;
                }

                player.SpentBody = (int)body;
                player.SpentMind = (int)mind;
                player.SpentSpirit = (int)spirit;
                UpdateStatsValues(client, false);
                client.CallMethod(player.EntityId, CreateAttributeInfoSnapshot(player));
                SendAvailableAllocationPoints(client);
            }
        }

        public void AssignPlayer(Client client)
        {
            var player = client.Player;
            if (!_loadout.Validate(player))
                return;
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
            // get charaterOptions
            var optionsList = unitOfWork.CharacterOptions.Get(player.Id);

            foreach (var characterOption in optionsList)
                player.CharacterOptions.Add(new CharacterOptions((CharacterOption)characterOption.OptionId, characterOption.Value));

            client.CallMethod(SysEntity.ClientMethodId, new CharacterOptionsPacket(player.CharacterOptions));

            client.CallMethod(SysEntity.ClientMethodId, new SetControlledActorIdPacket(player.EntityId));

            client.CallMethod(player.EntityId, new WeaponDrawerSlotPacket(player.ActiveWeapon, false));

            client.CallMethod(SysEntity.ClientGameMapId, new SetSkyTimePacket { RunningTime = 6666666 });   // ToDo add actual time how long map is running

            client.CallMethod(SysEntity.ClientMethodId, new SetCurrentContextIdPacket(client.Player.MapChannel.MapInfo.MapContextId));

            SocialManager.Instance.SetSocialContactList(client);

            client.CallMethod(player.EntityId, new ActorInfoPacket(player));
            MissionManager.Instance.PublishInitialState(client);

            client.CallMethod(player.EntityId, new UpdateRegionsPacket { RegionIdList = client.Player.MapChannel.MapInfo.BaseRegionId });  // ToDo this should be list of regions? or just curent region wher player is

            client.CallMethod(player.EntityId, new AdvancementStatsPacket(
                player.Level,
                player.Experience,
                GetAvailableAttributePoints(player),
                0,       // trainPoints (are not used by the client??)
                GetSkillPointsAvailable(player)
            ));

            client.CallMethod(player.EntityId, new SkillsPacket(player.Skills));

            client.CallMethod(player.EntityId, new AbilitiesPacket(player.Skills));

            PublishAbilityLoadout(client);

            client.CallMethod(player.EntityId, new TitlesPacket(player.Titles));

            client.CallMethod(player.EntityId, new UpdateAttributesPacket(player.Attributes, 0));

            client.CallMethod(player.EntityId, new UpdateHealthPacket(player.Attributes[Attributes.Health], 0));
            PublishAbilityResources(client);

            client.CallMethod(player.EntityId, new LogosStoneTabulaPacket(player.Logos));

            client.CallMethod(player.EntityId, new AllCreditsPacket(player.Credits));

            client.CallMethod(player.EntityId, new LockboxFundsPacket(player.LockboxCredits));
        }

        public void AutoFireTimerDoWork(long delta)
        {
            AutoFireTimer[] timers;
            lock (AutoFire)
                timers = AutoFire.ToArray();
            foreach (var timer in timers)
            {
                lock (timer.Client.SyncRoot)
                {
                    lock (AutoFire)
                        if (!AutoFire.Contains(timer))
                            continue;
                    if (!CanUseWeapons(timer.Client) || timer.Client.Player != timer.Player ||
                        timer.Player.ActionLifetime != timer.PlayerLifetime ||
                        timer.Client.Player.MapChannel != timer.Map ||
                        !InventoryManager.TryGetEquippedWeapon(timer.Client, out var weapon, out _) ||
                        !ReferenceEquals(weapon, timer.Weapon))
                    {
                        CancelAutoFire(timer.Client);
                        continue;
                    }
                    timer.MaxAliveTime -= Math.Max(0, delta);
                    if (timer.MaxAliveTime <= 0)
                    {
                        CancelAutoFire(timer.Client);
                        continue;
                    }
                    timer.Delay -= Math.Max(0, delta);
                    if (timer.Delay <= 0)
                    {
                        PlayerTryFireWeapon(timer.Client);
                        timer.Delay = timer.RefireTime;
                    }
                }
            }
        }

        public void CellDiscardClientToPlayers(Client client, List<Client> notifyClients)
        {
            foreach (var tempClient in notifyClients)
            {
                if (tempClient == client)
                    continue;

                tempClient.CallMethod(SysEntity.ClientMethodId, new DestroyPhysicalEntityPacket(client.Player.EntityId));
            }
        }

        public void CellDiscardPlayersToClient(Client client, List<Client> notifyClients)
        {
            foreach (var tempClient in notifyClients)
            {
                if (tempClient == null)
                    continue;

                if (tempClient == client)
                    continue;

                client.CallMethod(SysEntity.ClientMethodId, new DestroyPhysicalEntityPacket(tempClient.Player.EntityId));
            }

        }

        public void CellIntroduceClientToPlayers(Client client, List<Client> clientList)
        {
            var player = client.Player;

            foreach (var tempClient in clientList)
            {
                // don't send data about yourself
                if (tempClient == client)
                    continue;

                tempClient.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(player.EntityId, player.EntityClass, CreatePlayerEntityData(client)));

            }
        }

        public void CellIntroduceClientToSefl(Client client)
        {
            client.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(client.Player.EntityId, client.Player.EntityClass, CreatePlayerEntityData(client)));
        }

        public void CellIntroducePlayersToClient(Client client, List<Client> clientList)
        {
            foreach (var tempClient in clientList)
            {
                if (tempClient == null)
                    continue;

                if (tempClient == client)
                    continue;

                client.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(tempClient.Player.EntityId, tempClient.Player.EntityClass, CreatePlayerEntityData(tempClient)));
            }
        }
		
		public List<PythonPacket> CreatePlayerEntityData(Client client)
        {
            var player = client.Player;

            var entityData = new List<PythonPacket>
            {
                // PhysicalEntity
                new IsTargetablePacket(EntityClassManager.Instance.GetClassInfo(player.EntityClass).TargetFlag),
                new WorldLocationDescriptorPacket(player.Position, player.Rotation),
                // Manifestation
                new CurrentCharacterIdPacket(player.EntityId),
                new CharacterClassPacket(player.Class),
                new AttributeInfoPacket(player.Attributes),
                new PreloadDataPacket(client.Player.Inventory.EquippedInventory[13], player.Abilities),
                new AppearanceDataPacket(player.AppearanceData),
                new ResistanceDataPacket(player.ResistanceData),
                new ActorControllerInfoPacket(true),
                new MovementModChangePacket(player.MovementSpeed),
                new LevelPacket(player.Level),
                new CharacterNamePacket(player.Name),
                new ActorNamePacket(player.FamilyName),
                new IsRunningPacket(player.IsRunning),
                new TargetCategoryPacket(Factions.AFS),
                new PlayerFlagsPacket(),
                new EquipmentInfoPacket(client.Player.Inventory.EquippedInventory)
            };

            entityData.AddRange(GameEffectManager.SnapshotEffects(client));
            return entityData;
        }

        internal sealed class ProgressionGrant
        {
            internal bool HasChanges { get; init; }
            internal uint ExperienceAward { get; init; }
            internal uint TotalExperience { get; init; }
            internal byte PreviousLevel { get; init; }
            internal byte FinalLevel { get; init; }
        }

        internal ProgressionGrant PlanExperience(
            Client client,
            uint experience,
            CharacterEntry durableCharacter,
            Repositories.Char.ICharUnitOfWork unitOfWork)
        {
            var player = client.Player;
            if (player.Level >= MaxPlayerLevel)
                return new ProgressionGrant();
            EnsureValidProgressionLevel(player);
            if (durableCharacter.Id != player.Id ||
                durableCharacter.Experience != player.Experience ||
                durableCharacter.Level != player.Level)
                throw new GameplayRejectionException("Runtime progression no longer matches durable character state.");

            var totalExperience = checked(durableCharacter.Experience + experience);
            var previousLevel = durableCharacter.Level;
            var finalLevel = previousLevel;
            while (finalLevel < MaxPlayerLevel)
            {
                var requiredExperience = GetLevelNeededExperience(finalLevel);
                if (requiredExperience < 0 || totalExperience < requiredExperience)
                    break;
                finalLevel++;
            }

            unitOfWork.Characters.UpdateCharacterProgression(player.Id, totalExperience, finalLevel);
            return new ProgressionGrant
            {
                HasChanges = true,
                ExperienceAward = experience,
                TotalExperience = totalExperience,
                PreviousLevel = previousLevel,
                FinalLevel = finalLevel
            };
        }

        internal void PublishExperience(Client client, ProgressionGrant grant)
        {
            if (grant == null || !grant.HasChanges)
                return;

            var player = client.Player;
            player.Experience = grant.TotalExperience;
            player.Level = grant.FinalLevel;
            client.CallMethod(player.EntityId, new ExperienceChangedPacket(
                new XPInfo(grant.TotalExperience, grant.ExperienceAward, grant.ExperienceAward)));
            if (grant.FinalLevel == grant.PreviousLevel)
                return;

            for (var level = grant.PreviousLevel + 1; level <= grant.FinalLevel; level++)
            {
                client.CallMethod(player.EntityId, new LevelUpPacket((byte)level));
                var message = new Dictionary<string, string>
                {
                    { "level", level.ToString() },
                    { "attributePts", "3" },
                    { "skillPts", (GetSkillPointsForLevel(level) - GetSkillPointsForLevel(level - 1)).ToString() }
                };
                client.CallMethod(SysEntity.CommunicatorId,
                    new DisplayClientMessagePacket(PlayerMessage.PmLevelIncreased, message, MsgFilterId.LeveledUp));
            }

            UpdateStatsValues(client, true);
            var attributes = CreateAttributeInfoSnapshot(player);
            client.CallMethod(player.EntityId, attributes);
            SendAvailableAllocationPoints(client);

            foreach (var observer in CellManager.Instance.GetClientsInCells(player.MapChannel, player.Cells, client))
            {
                if (!IsActiveWorldClient(observer))
                    continue;
                observer.CallMethod(player.EntityId, new LevelPacket(grant.FinalLevel));
                observer.CallMethod(player.EntityId, attributes);
            }
        }

        internal void GainExperience(Client client, uint experience)
        {
            if (client == null)
                return;

            lock (client.SyncRoot)
            {
                if (!IsActiveWorldClient(client))
                {
                    Logger.WriteLog(LogType.Network, "Ignored experience award outside the active world state.");
                    return;
                }

                var player = client.Player;
                if (!ValidateProgressionForClient(client))
                    return;
                if (player.Level >= MaxPlayerLevel)
                    return; // cannot gain xp over level 50
                try
                {
                    _ = checked(player.Experience + experience);
                }
                catch (OverflowException)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Rejected experience overflow for character {player.Id}: {player.Experience} + {experience}.");
                    return;
                }

                ProgressionGrant grant = null;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        var durableCharacter = unitOfWork.Characters.Get(player.Id);
                        grant = PlanExperience(client, experience, durableCharacter, unitOfWork);
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to save experience for character {player.Id}: {error.Message}");
                    return;
                }

                PublishExperience(client, grant);
            }
        }

        public void DebugChgPlayerClass(Client client, uint newClassId)
        {
            client.Player.Class = newClassId;
            client.CallMethod(client.Player.EntityId, new CharacterClassPacket(client.Player.Class));
            CharacterManager.Instance.UpdateCharacter(client, CharacterUpdate.Class, client.Player.Class);
        }

        public void GainCredits(Client client, int credits)
        {
            CharacterManager.Instance.UpdateCharacter(client, CharacterUpdate.Credits, credits);
            // send player message
            client.CallMethod(SysEntity.CommunicatorId, new DisplayClientMessagePacket(PlayerMessage.PmGotMoneyLootFromUnknown, new Dictionary<string, string> { { "amount", credits.ToString() } }, MsgFilterId.LootObtained));
        }

        public void LossCredits(Client client, int credits)
        {
            CharacterManager.Instance.UpdateCharacter(client, CharacterUpdate.Credits, credits);
        }

        public int GetAvailableAttributePoints(Manifestation player)
        {
            var points = 3 * (player.Level - 1);
            points -= player.SpentBody;
            points -= player.SpentMind;
            points -= player.SpentSpirit;
            //points = Math.Max(points, 0); Probably do not need this? (StaticVariable)
            return points;
        }

        public void GetCustomizationChoices(Client client, GetCustomizationChoicesPacket packet)
        {
            // ToDo
            var test = EntityManager.Instance.GetEntityType(packet.EntityId);
            var testChoices = new Dictionary<int, int>
            {
                { 3663, 36 },
                { 3672, 42 },
                { 3812, 60 }
            };
            client.CallMethod(SysEntity.ClientMethodId, new CustomizationChoicesPacket(packet.EntityId, testChoices));
        }

        private int GetLevelNeededExperience(int level)
        {
            if (level < 1 || level >= 50)
                return -1;

            return ExpPerLevel.ExpRequred[level];
        }

        public int GetSkillIndexById(int skillId)
        {
            return skillId < 0 ? -1 : skillId >= 200 ? -1 : SkillId2Idx[skillId];
        }

        public int GetSkillPointsAvailable(Manifestation player)
        {
            var pointsAvailable = GetSkillPointsForLevel(player.Level);

            // subtract spent skill levels
            foreach (var skill in player.Skills)
            {
                var skillLevel = skill.Value.SkillLevel;
                if (skillLevel < 0 || skillLevel > 5)
                    continue; // should not be possible
                pointsAvailable -= requiredSkillLevelPoints[skillLevel];
            }
            return Math.Max(0, pointsAvailable);
        }

        internal static int GetSkillPointsForLevel(int level)
        {
            var pointsAvailable = (level - 1) * 2 + 5;

            if (level >= 5)
                pointsAvailable += 2;

            if (level >= 15)
                pointsAvailable += 2;

            if (level >= 30)
                pointsAvailable += 2;

            if (level >= 50)
                pointsAvailable += 4;

            return pointsAvailable;
        }

        public void LevelSkills(Client client, LevelSkillsPacket packet)
        {
            _loadout.Train(client, packet);
        }

        internal void PublishAbilityLoadout(Client client) => _loadout.Publish(client);

        internal bool ValidateAbilityLoadoutForClient(Client client)
        {
            if (_loadout.Validate(client?.Player))
                return true;
            if (client != null)
                _disconnect(client);
            return false;
        }

        internal static void PublishAbilityResources(Client client)
        {
            client.CallMethod(client.Player.EntityId, new UpdatePowerPacket(client.Player.Attributes[Attributes.Power], 0));
            client.CallMethod(client.Player.EntityId, new UpdateChiPacket(client.Player.Attributes[Attributes.Chi], 0));
        }

        public void NotifyEquipmentUpdate(Client client)
        {
            client.CallMethod(client.Player.EntityId, new EquipmentInfoPacket(client.Player.Inventory.EquippedInventory));
        }

        public void RegisterAutoFire(Client client)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
            {
                if (!CanUseWeapons(client) || !InventoryManager.TryGetEquippedWeapon(client, out var weapon, out _))
                    return;
                lock (AutoFire)
                {
                    if (AutoFire.Any(timer => timer.Client == client))
                        return;
                    AutoFire.Add(new AutoFireTimer(client, weapon.ItemTemplate.WeaponInfo.Refire, weapon.ItemTemplate.WeaponInfo.Refire)
                    {
                        Weapon = weapon, Player = client.Player, Map = client.Player.MapChannel,
                        PlayerLifetime = client.Player.ActionLifetime
                    });
                }
            }
        }

        public void RemovePlayerCharacter(Client client)
        {
            if (client?.Player?.MapChannel != null)
                lock (client.SyncRoot)
                    LootDispenserManager.RemoveForOwner(client.Player.MapChannel, client);
            CancelCombatActions(client);
        }

        public void RemoveAppearanceItem(Client client, EquipmentData equipmentSlotId)
        {
            if (equipmentSlotId == 0)
                return;

            client.Player.AppearanceData[equipmentSlotId].Class = 0;
            // update appearance data in database
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
            unitOfWork.CharacterAppearances.AddOrUpdate(client.Player.Id, new CharacterAppearanceEntry((uint)equipmentSlotId, 0, 0));
            unitOfWork.Complete();
        }

        public void RequestCustomization(Client client, RequestCustomizationPacket packet)
        {
            // ToDo
            Logger.WriteLog(LogType.Debug, $"ToDo: RequestCustomization");
        }

        public void RequestPerformAbility(Client client, RequestPerformAbilityPacket packet)
        {
            Abilities.Request(client, packet);
        }

        public void RequestToggleRun(Client client)
        {
            client.Player.IsRunning = !client.Player.IsRunning;

            client.CallMethod(client.Player.EntityId, new IsRunningPacket(client.Player.IsRunning));
        }

        public void RequestWeaponDraw(Client client)
        {
            QueueWeaponReady(client, true);
        }

        public void RequestWeaponReload(Client client, bool isRequested)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
                QueueWeaponReload(client, isRequested);
        }

        private void QueueWeaponReload(Client client, bool isRequested)
        {
            if (!CanUseWeapons(client) || client.Player.PendingAbility != null ||
                !InventoryManager.TryGetEquippedWeapon(client, out var weapon, out var weaponClassInfo))
                return;
            if (client.Player.PendingWeaponAction != null)
            {
                if (!client.Player.PendingWeaponAction.IsInrerrupted)
                    return;
                CancelWeaponAction(client);
            }
            var changes = InventoryManager.PlanWeaponReload(client, weapon, weaponClassInfo);
            if (changes.Count == 0)
                return;
            var foundAmmo = weapon.CurrentAmmo + (uint)changes.Sum(change => (long)change.Item.StackSize - change.Remaining);

            if (isRequested)
                client.CellCallMethod(client, client.Player.EntityId, new PerformWindupPacket(PerformType.TwoArgs, ActionId.WeaponReload, (uint)weaponClassInfo.ReloadActionId));
            else
                client.CellIgnoreSelfCallMethod(client, new PerformWindupPacket(PerformType.TwoArgs, ActionId.WeaponReload, (uint)weaponClassInfo.ReloadActionId));

            var action = new ActionData(client.Player, ActionId.WeaponReload, (uint)weaponClassInfo.ReloadActionId, foundAmmo, weapon.ItemTemplate.WeaponInfo.ReloadTime)
            {
                WeaponClient = client, WeaponMap = client.Player.MapChannel, Weapon = weapon,
                WeaponActorLifetime = client.Player.ActionLifetime
            };
            client.Player.PendingWeaponAction = action;
            client.Player.MapChannel.PerformRecovery.Add(action);
        }

        public void RequestWeaponStow(Client client)
        {
            QueueWeaponReady(client, false);
        }

        private void QueueWeaponReady(Client client, bool ready)
        {
            if (client == null)
                return;
            lock (client.SyncRoot)
            {
                if (!CanUseWeapons(client) || client.Player.PendingAbility != null ||
                    !InventoryManager.TryGetEquippedWeapon(client, out var weapon, out var info))
                    return;
                var actionId = ready ? ActionId.WeaponDraw : ActionId.WeaponStow;
                if (client.Player.PendingWeaponAction?.ActionId == actionId || client.Player.WeaponReady == ready)
                    return;
                CancelWeaponAction(client);
                if (!ready)
                    CancelAutoFire(client);
                var action = new ActionData(client.Player, actionId, ready ? info.DrawActionId : info.StowActionId, 500)
                {
                    WeaponClient = client, WeaponMap = client.Player.MapChannel, Weapon = weapon,
                    WeaponActorLifetime = client.Player.ActionLifetime
                };
                client.Player.PendingWeaponAction = action;
                client.Player.MapChannel.PerformRecovery.Add(action);
                WeaponReady(client, ready);
            }
        }

        internal void RecoverWeaponReady(ActionData action)
        {
            if (action?.WeaponClient == null)
                return;
            lock (action.WeaponClient.SyncRoot)
            {
                if (TakeWeaponAction(action))
                    CellManager.Instance.CellCallMethod(action.WeaponMap, action.Actor,
                        new PerformRecoveryPacket(PerformType.TwoArgs, action.ActionId, action.ActionArgId));
            }
        }

        public void SaveCharacterOptions(Client client, SaveCharacterOptionsPacket packet)
        {
            if (packet.OptionsList.Count == 0)
                return;

            client.Player.CharacterOptions = packet.OptionsList;
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();

            foreach (var option in client.Player.CharacterOptions)
                unitOfWork.CharacterOptions.AddOrUpdate(client.Player.Id, (uint)option.OptionId, option.Value);
        }

        // maybe move this to other manager becose it's account related
        public void SaveUserOptions(Client client, SaveUserOptionsPacket packet)
        {
            client.UserOptions = packet.OptionsList;
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();

            foreach (var option in client.UserOptions)
                unitOfWork.UserOptions.AddOrUpdate(client.AccountEntry.Id, (uint)option.OptionId, option.Value);

            unitOfWork.Complete();
        }

        internal void SendAvailableAllocationPoints(Client client)
        {
            // update available allocation points (attributes, trainPts, skillPts)

            var attributePoints = GetAvailableAttributePoints(client.Player);
            var trainPoints = 0;    // not used by te client
            var skillPoints = GetSkillPointsAvailable(client.Player);

            client.CallMethod(client.Player.EntityId, new AvailableAllocationPointsPacket(attributePoints, trainPoints, skillPoints));
        }

        public void SetAppearanceItem(Client client, Item item)
        {
            var equipmentSlotId = EntityClassManager.Instance.GetEquipableClassInfo(item).EquipmentSlotId;
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();

            if (!client.Player.AppearanceData.ContainsKey(equipmentSlotId))
                client.Player.AppearanceData.Add(equipmentSlotId, new AppearanceData { SlotId = equipmentSlotId });

            client.Player.AppearanceData[equipmentSlotId].Class = (uint)item.ItemTemplate.Class;
            client.Player.AppearanceData[equipmentSlotId].Color = new Color(item.Color);
            client.Player.AppearanceData[equipmentSlotId].Hue2 = new Color(item.Color);

            // update appearance data in database

            unitOfWork.CharacterAppearances.AddOrUpdate(client.Player.Id, new CharacterAppearanceEntry((uint)equipmentSlotId, (uint)item.ItemTemplate.Class, item.Color));
            unitOfWork.Complete();
        }

        public void SetDesiredCrouchState(Client client, bool crouching)
        {
            client.Player.IsCrouching = crouching;

            client.CallMethod(client.Player.EntityId, new SetDesiredCrouchStatePacket(client.Player.IsCrouching ? CharacterState.Crouched : CharacterState.Standing));
        }

        public void SetTargetId(Client client, ulong entityId)
        {
            client.Player.Target = entityId;
        }

        public void SetTrackingTarget(Client client, ulong entityId)
        {
            client.Player.TrackingTargetEntityId = entityId;
        }

        public void UpdateAppearance(Client client)
        {
            if (client.Player == null)
                return;

            client.CellCallMethod(client, client.Player.EntityId, new AppearanceDataPacket(client.Player.AppearanceData));
        }

        // Health calculation:
        //levelBasedHealth = 20000.0
        //for (int i = level; i< 50; ++i)
        //levelBasedHealth = levelBasedHealth - 0.082995 * levelBasedHealth;

        private readonly float[] HealthBaselinePerLevel =
         {
            286.5784148f,
            312.51565127f,
            340.8003787f,
            371.6450605f,
            405.28138942f,
            441.96202792f,
            481.96250612f,
            525.58329139f,
            573.15204539f,
            625.02608535f,
            681.59506802f,
            743.28391668f,
            810.55601298f,
            883.91667764f,
            963.91696626f,
            1051.15780858f,
            1146.29452247f,
            1250.04173638f,
            1363.17875735f,
            1486.55542483f,
            1621.09849437f,
            1767.818599f,
            1927.81784069f,
            2102.29806892f,
            2292.56990847f,
            2500.06260431f,
            2726.33475751f,
            2973.08603281f,
            3242.1699258f,
            3535.60768567f,
            3855.60349799f,
            4204.56104164f,
            4585.10154431f,
            5000.08347207f,
            5452.62400104f,
            5946.1224323f,
            6484.28572615f,
            7071.15634718f,
            7711.14262974f,
            8409.05189147f,
            9170.12654399f,
            10000.08347172f,
            10905.15697485f,
            11892.14559882f,
            12968.4632023f,
            14142.19464703f,
            15422.15652808f,
            16817.9634005f,
            18340.1f,
            20000f
        };

        private static AttributeInfoPacket CreateAttributeInfoSnapshot(Manifestation player)
        {
            var attributes = new Dictionary<Attributes, ActorAttributes>();
            foreach (var entry in player.Attributes)
            {
                var value = entry.Value;
                attributes.Add(entry.Key, new ActorAttributes(value.AttributeId, value.NormalMax,
                    value.CurrentMax, value.Current, value.RefreshAmount, value.RefreshPeriod));
            }
            return new AttributeInfoPacket(attributes);
        }

        private void EnsureValidProgressionLevel(Manifestation player)
        {
            if (player.Level < 1 || player.Level > HealthBaselinePerLevel.Length)
            {
                var error = new InvalidProgressionLevelException(player.Id, player.Level, HealthBaselinePerLevel.Length);
                Logger.WriteLog(LogType.Error, error.Message);
                throw error;
            }
        }

        internal bool ValidateProgressionForClient(Client client)
        {
            try
            {
                EnsureValidProgressionLevel(client.Player);
                return true;
            }
            catch (InvalidProgressionLevelException)
            {
                _disconnect(client);
                MapChannelManager.Instance.CleanupDisconnected(client);
                return false;
            }
        }

        /*
         * ToDO (this still need work, this is just copied from c++ projet
         * Updates all attributes depending on level, spent attribute points, etc.
         * Does not send values to clients
         * If fullreset is true, the current values of each attribute are set to the maximum
         */
        public void UpdateStatsValues(Client client, bool fullreset)
        {
            var player = client.Player;
            var attribute = player.Attributes;

            int level = player.Level;
            EnsureValidProgressionLevel(player);

            int levelBasedBody   = 0;
            int levelBasedMind   = 0;
            int levelBasedSpirit = 0;

            switch (player.Race)
            {
                case Race.Human:
                    levelBasedBody = levelBasedMind = levelBasedSpirit = 2 * (level - 1) + 10;
                    break;

                case Race.Forean:
                    levelBasedBody = (level - 1) + 10;
                    levelBasedMind = 3 * (level - 1) + 10;
                    levelBasedSpirit = 2 * (level - 1) + 10;
                    break;

                case Race.Brann:
                    levelBasedBody = levelBasedMind = (level - 1) + 10;
                    levelBasedSpirit = 4 * (level - 1) + 10;
                    break;

                case Race.Thrax:
                    levelBasedBody = 3 * (level - 1) + 10;
                    levelBasedMind = (level - 1) + 10;
                    levelBasedSpirit = 2 * (level - 1) + 10;
                    break;
            }

            int totalBody   = levelBasedBody   + player.SpentBody;
            int totalMind   = levelBasedMind   + player.SpentMind;
            int totalSpirit = levelBasedSpirit + player.SpentSpirit;

            // Health
            float levelBasedHealth = HealthBaselinePerLevel[level - 1];
            levelBasedHealth = levelBasedHealth / (2 * (level - 1) + 2 * (2 * (level - 1) + 10) + 10);
            int totalHealth = (int)(levelBasedHealth * (totalSpirit + 2 * totalBody));

            // Power
            float basePower = (3 * level + 100f) / (2 * (level - 1) + 2 * (2 * (level - 1) + 10) + 10);
            int totalPower  = (int)(basePower * (totalBody + 2 * totalMind));

            // Regen
            float baseRegen = (2 * level + 100f) / (2 * (level - 1) + 2 * (2 * (level - 1) + 10) + 10);
            int totalRegen = (int)(baseRegen * (totalMind + 2 * totalSpirit));
          
            // Bonuses
            var bodyBonus = 0;
            var mindBonus = 0;
            var spiritBonus = 0;

            var healthBonus = 0;
            var chiBonus    = 0;
            var regenBonus  = 0;

            float armorBonusPercent = (float)Math.Max(0.0, (totalBody - (2 * (level - 1) + 10)) * 0.667);   // every body attribute over the default base attribute gives 0.667% bonus armo;
            var logosBonusPercent = GetLogosBonusPercent(level, totalMind);
            float critBonusPercent = (float)Math.Max(0.0, (totalSpirit - (2 * (level - 1) + 10)) * 0.065);  // every spirit attribute over the default base attribute gives 0.065% bonus crit chance;


            // body
            attribute[Attributes.Body].NormalMax    = totalBody;
            attribute[Attributes.Body].CurrentMax   = attribute[Attributes.Body].NormalMax + bodyBonus;
            attribute[Attributes.Body].Current      = attribute[Attributes.Body].CurrentMax;

            attribute[Attributes.Mind].NormalMax    = totalMind;
            attribute[Attributes.Mind].CurrentMax   = attribute[Attributes.Mind].NormalMax + mindBonus;
            attribute[Attributes.Mind].Current      = attribute[Attributes.Mind].CurrentMax;

            attribute[Attributes.Spirit].NormalMax  = totalSpirit;
            attribute[Attributes.Spirit].CurrentMax = attribute[Attributes.Spirit].NormalMax + spiritBonus;
            attribute[Attributes.Spirit].Current    = attribute[Attributes.Spirit].CurrentMax;

            // health
            attribute[Attributes.Health].NormalMax  = totalHealth;
            attribute[Attributes.Health].CurrentMax = totalHealth;

            // chi/adrenaline
            attribute[Attributes.Chi].NormalMax     = totalPower;
            attribute[Attributes.Chi].CurrentMax    = totalPower;

            attribute[Attributes.Regen].NormalMax   = totalRegen; // regenRate in percent
            attribute[Attributes.Regen].CurrentMax  = totalRegen;

            if (fullreset)
            {
                attribute[Attributes.Health].Current = attribute[Attributes.Health].CurrentMax;
                attribute[Attributes.Chi].Current = attribute[Attributes.Chi].CurrentMax;
            }
            else
            {
                attribute[Attributes.Health].Current = Math.Min(attribute[Attributes.Health].Current, attribute[Attributes.Health].CurrentMax);
                attribute[Attributes.Chi].Current = Math.Min(attribute[Attributes.Chi].Current, attribute[Attributes.Chi].CurrentMax);
            }


            // update regen rate
            attribute[Attributes.Regen].RefreshAmount = (int)Math.Round(2D * (attribute[Attributes.Regen].CurrentMax / 100D), 0);
            // 2.0 per second is the base regeneration for health
            // calculate armor max
            var armorMax = 0.0d;
            //float armorBonus = 0; // todo! (From item modules)
            var armorBonusPct = player.Attributes[Attributes.Body].CurrentMax * 0.0066666d;
            var armorRegenRate = 0;

            for (var i = 1; i < player.Inventory.EquippedInventory.Count; i++)
            {
                if (client.Player.Inventory.EquippedInventory[i] == 0)
                    continue;

                // skip weapon slot
                if (i == 13)
                    continue;

                var equipmentItem = EntityManager.Instance.GetItem(client.Player.Inventory.EquippedInventory[i]);
                if (equipmentItem?.ItemTemplate == null)
                {
                    // this is very bad, how can the item disappear while it is still linked in the inventory?
                    Logger.WriteLog(LogType.Error, "UpdateStatsValues: Equipment item has no physical copy (item is missing)");
                    continue;
                }
                var classInfo = EntityClassManager.Instance.GetClassInfo(equipmentItem.ItemTemplate.Class);
                if (classInfo?.ArmorClassInfo == null)
                {
                    // how can the player equip non-armor?
                    Logger.WriteLog(LogType.Error, "UpdateStatsValues: Player try to equip non_armor item");
                    continue;
                }
                armorMax += equipmentItem.ItemTemplate.ArmorValue;      // ToDo
                armorRegenRate += classInfo.ArmorClassInfo.RegenRate;
                
                // what about damage absorbed? Was it used at all?
            }
            armorMax = armorMax * (1.0d + armorBonusPct);
            attribute[Attributes.Armor].RefreshAmount = armorRegenRate;
            attribute[Attributes.Armor].NormalMax = (int)Math.Round(armorMax, 0);
            attribute[Attributes.Armor].CurrentMax = attribute[Attributes.Armor].NormalMax;
            if (fullreset)
                attribute[Attributes.Armor].Current = attribute[Attributes.Armor].CurrentMax;
            else
                attribute[Attributes.Armor].Current = Math.Min(attribute[Attributes.Armor].Current, attribute[Attributes.Armor].CurrentMax);
            // added by krssrb
            // power test
            attribute[Attributes.Power].NormalMax = 100 + (player.Level - 1) * 2 * 4 + player.SpentMind * 3;
            var powerBonus = 0;
            attribute[Attributes.Power].CurrentMax = attribute[Attributes.Power].NormalMax + powerBonus;
            if (fullreset)
                attribute[Attributes.Power].Current = attribute[Attributes.Power].CurrentMax;
            else
                attribute[Attributes.Power].Current = Math.Min(attribute[Attributes.Power].Current, attribute[Attributes.Power].CurrentMax);
        }

        public void WeaponReady(Client client, bool isReady)
        {
            client.Player.WeaponReady = isReady;
            client.CallMethod(client.Player.EntityId, new WeaponReadyPacket(isReady));
        }

        internal static double GetLogosBonusPercent(int level, int mind) =>
            Math.Max(0d, ((double)mind - (2 * (level - 1) + 10)) * 0.375);

        public void WeaponReload(ActionData action)
        {
            var client = action?.WeaponClient;
            if (client == null)
                return;
            lock (client.SyncRoot)
            {
                if (!TakeWeaponAction(action))
                    return;
                var weapon = action.Weapon;
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    if (!InventoryManager.CommitWeaponReload(client, weapon, EntityClassManager.Instance.GetWeaponClassInfo(weapon), unitOfWork))
                        return;
                }
                catch (Exception exception) when (GameplayRejectionException.IsExpected(exception))
                {
                    Logger.WriteLog(LogType.Error, $"Weapon reload save failed for item {weapon.Id}: {exception}");
                    return;
                }
                client.Player.CurrentAction = 0;
                client.CellCallMethod(client, client.Player.EntityId, new PerformRecoveryPacket(PerformType.ThreeArgs, action.ActionId, action.ActionArgId, weapon.CurrentAmmo));
            }
        }

        #endregion
    }
}
