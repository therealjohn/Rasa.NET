using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.Game.Client;
    using Packets.Game.Server;
    using Packets.MapChannel.Server;
    using Misc;
    using Packets.ClientMethod.Server;
    using Packets.Communicator.Client;
    using Packets.Communicator.Server;
    using Packets.Manifestation.Server;
    using Packets;
    using Repositories.Char;
    using Repositories.Char.CharacterMissionProgress;
    using Repositories.UnitOfWork;
    using Repositories.World;
    using Structures;
    using Structures.Char;

    public class CharacterManager
    {
        private static CharacterManager _instance;
        private static readonly object InstanceLock = new object();
        private readonly object _createLock = new();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly MissionManager _missionManager;
        private const string Deployment11StartingExperienceRevision = "deployment_11";
        private const string LegacyStartingExperienceRevision = "legacy";
        internal const uint BootcampPrivateMapContextId = 1985;
        internal const uint BootcampExitPadWaypointId = 60;
        private const uint BootcampInitiationMissionId = 1990;
        private const uint BootcampFinaleMissionId = 1995;
        private const uint BootcampRetryFinaleMissionId = 2005;
        private const uint BootcampParityExperience = 43000;
        private const byte BootcampParityLevel = 5;
        private const uint BootcampSkipAmmoTemplateId = 28;
        private const uint BootcampSkipAmmoQuantity = 20;
        private const uint BootcampAliaWaypointId = 57;
        private const uint BootcampAliaHospitalId = 103;
        private const byte BootcampAbilitySlot = 0;
        private const double BootcampStartCoordX = 357.90054d;
        private const double BootcampStartCoordY = 120.32544d;
        private const double BootcampStartCoordZ = 156.5188d;
        private const double BootcampStartRotation = 0d;
        private const uint BootcampArrivalMapContextId = 1220;
        private const double BootcampArrivalCoordX = 884.11d;
        private const double BootcampArrivalCoordY = 305.8d;
        private const double BootcampArrivalCoordZ = 347.81d;
        private const double BootcampArrivalRotation = 1.5613d;
        private static readonly uint[] BootcampSkipEquipmentTemplateIds =
        {
            13066,
            13096,
            13156,
            13186,
            13713
        };

        public const ulong SelectionPodStartEntityId = 100;
        public const byte MaxSelectionPods = 16;

        public static CharacterManager Instance
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new CharacterManager(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        public CharacterManager(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            MissionManager missionManager = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _missionManager = missionManager;
        }

        public void StartCharacterSelection(Client client)
        {
            if (client.State != ClientState.LoggedIn)
                return;

            client.CallMethod(SysEntity.ClientMethodId, new BeginCharacterSelectionPacket(client.AccountEntry.FamilyName, client.AccountEntry.Characters.Any(), client.AccountEntry.Id, client.AccountEntry.CanSkipBootcamp));

            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
            var charactersBySlot = unitOfWork.Characters.GetByAccountId(client.AccountEntry.Id);

            for (byte i = 1; i <= MaxSelectionPods; ++i)
            {
                CharacterEntry character = null;
                if (charactersBySlot.ContainsKey(i))
                {
                    character = charactersBySlot[i];
                }
                SendCharacterInfoProdCreate(client, i, character);
            }

            client.State = ClientState.CharacterSelection;

            // get userOptions
            var optionsList = unitOfWork.UserOptions.Get(client.AccountEntry.Id);

            foreach (var userOption in optionsList)
                client.UserOptions.Add(new UserOptions((UserOption)userOption.OptionId, userOption.Value));
            
            client.CallMethod(SysEntity.ClientMethodId, new UserOptionsPacket(client.UserOptions));
        }

        public void RequestCharacterName(Client client, int gender)
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            var name = unitOfWork.RandomNames.GetFirstName((Gender)gender);
            client.CallMethod(SysEntity.ClientMethodId, new GeneratedCharacterNamePacket
            {
                Name = name
            });
        }

        public void RequestFamilyName(Client client)
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            var name = unitOfWork.RandomNames.GetLastName();
            client.CallMethod(SysEntity.ClientMethodId, new GeneratedFamilyNamePacket
            {
                Name = name
            });
        }

        /// <summary>
        /// Clone credits: a snapshot of a character in a new pod, so a player can take a second
        /// run at the class tree without levelling again, or refund the skill points they spent.
        ///
        /// What carries over and what does not is the live game's rule, not a guess. Kept:
        /// attributes, the logos tablet, obtained waypoints, the surname, and the place the
        /// source was standing when the clone was made - clone somewhere hostile and the clone
        /// wakes up there. Reset: skills, missions, friends and clan. Level, experience and class
        /// come across too, because a clone taken at 14.99 exists precisely so both Tier 3
        /// branches can be tried from the same progress.
        ///
        /// The clone arrives with nothing. It does not inherit the source's pack, and unlike a
        /// new character it gets no starter kit either.
        /// </summary>
        public void RequestCloneCharacterToSlot(Client client, RequestCloneCharacterToSlotPacket packet)
        {
            // Same rule as creating: the pod screen is the only place this is safe, because the
            // account entry is reloaded underneath whatever is loaded.
            if (client.State != ClientState.CharacterSelection)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry.Id} tried to clone a character while in state {client.State}.");

                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return;
            }

            var result = packet.Validate();
            if (result != CreateCharacterResult.Success)
            {
                SendCharacterCreateFailed(client, result);
                return;
            }

            if (packet.SlotNum < 1 || packet.SlotNum > MaxSelectionPods
                || packet.CloneSlotNum < 1 || packet.CloneSlotNum > MaxSelectionPods
                || packet.SlotNum == packet.CloneSlotNum)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry.Id} tried to clone slot {packet.CloneSlotNum} into slot {packet.SlotNum}.");

                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return;
            }

            if (client.AccountEntry.GetCharacterBySlot(packet.SlotNum) != null)
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.CharacterSlotInUse);
                return;
            }

            var source = client.AccountEntry.GetCharacterBySlot(packet.CloneSlotNum);

            if (source == null)
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.InvalidCharacterToCloneFrom);
                return;
            }

            // The client greys its clone button out at zero, so this only catches a client that
            // did not - but it is the check that stops a credit going negative on a uint.
            if (source.CloneCredits == 0)
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.NotEnoughCloneCredits);
                return;
            }

            uint characterId;
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();

            lock (_createLock)
            {
                if (!TryPersistCharacterCreation(
                        client,
                        unitOfWork,
                        () => InternalClone(client, packet, unitOfWork),
                        out characterId))
                {
                    return;
                }
            }

            CopyProgressToClone(unitOfWork, source, characterId);

            // Spent last, so a clone that failed anywhere above costs nothing.
            //
            // Held in a local rather than re-read off `source` afterwards. UpdateCharacterCloneCredits
            // writes through the tracked entity, and whether that is the same object as `source`
            // depends on which context loaded the account - so reading it back would subtract
            // twice on one path and once on the other.
            var remainingCredits = source.CloneCredits - 1;

            unitOfWork.Characters.UpdateCharacterCloneCredits(source.Id, remainingCredits);

            if (unitOfWork.CharacterLockboxes.Get(client.AccountEntry.Id) == null)
                unitOfWork.CharacterLockboxes.Add(client.AccountEntry.Id);

            unitOfWork.Complete();

            client.CallMethod(SysEntity.ClientMethodId,
                new CharacterCreateSuccessPacket(packet.SlotNum, client.AccountEntry.FamilyName));

            client.ReloadGameAccountEntry();

            SendCharacterInfo(client, packet.SlotNum, unitOfWork.Characters.Get(characterId));

            // The source pod shows a credit count, which just went down by one.
            SendCharacterInfo(client, packet.CloneSlotNum, unitOfWork.Characters.Get(source.Id));

            // And the pod's own method for exactly this, which CharacterInfo does not replace:
            // Recv_CloneCreditsChanged posts UI_UPDATE_CHARACTER_SELECTION_SLOT_CLONE_CREDITS,
            // which repaints the stats panel if that slot is the selected one. CharacterInfo
            // reaches _UpdatePod and _AutoSelectCharacter, and neither of those repaints, so the
            // number would sit stale on screen until the player clicked away and back.
            client.CallMethod(SelectionPodStartEntityId + packet.CloneSlotNum,
                new CloneCreditsChangedPacket(remainingCredits));
        }

        /// <summary>
        /// The row for a clone. Unlike a new character there is no family name to set or check:
        /// cloning needs a character to clone from, so the account already has one, and the
        /// surname is the one thing the live game's rules say always carries over.
        /// </summary>
        private uint? InternalClone(Client client, RequestCloneCharacterToSlotPacket packet, ICharUnitOfWork unitOfWork)
        {
            var characterEntry = unitOfWork.Characters.Create(client.AccountEntry, packet.SlotNum,
                packet.CharacterName,
                (byte)packet.RaceId,
                packet.Scale,
                packet.Gender);

            if (characterEntry == null)
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return null;
            }

            if (!unitOfWork.CharacterAppearances.Add(characterEntry, CreateCharacterAppearanceEntries(packet.AppearanceData)))
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return null;
            }

            unitOfWork.CharacterStartingExperience.Add(new CharacterStartingExperienceEntry(
                characterEntry.Id,
                LegacyStartingExperienceRevision,
                CharacterStartingExperienceState.Legacy));

            return characterEntry.Id;
        }

        /// <summary>
        /// Everything the live game's rules say survives cloning. Skills and the ability drawer
        /// are deliberately absent - resetting them is what makes a clone a respec - and so are
        /// missions, friends and clan.
        /// </summary>
        private static void CopyProgressToClone(ICharUnitOfWork unitOfWork, CharacterEntry source, uint cloneId)
        {
            unitOfWork.Characters.UpdateCharacterLevel(cloneId, source.Level);
            unitOfWork.Characters.UpdateCharacterExpirience(cloneId, source.Experience);
            unitOfWork.Characters.UpdateCharacterClass(cloneId, source.Class);
            unitOfWork.Characters.UpdateCharacterAttributes(cloneId, source.Body, source.Mind, source.Spirit);

            // "Location upon cloning": the clone appears where the source was standing, hostile
            // ground included.
            unitOfWork.Characters.UpdateCharacterPosition(cloneId, source.CoordX, source.CoordY, source.CoordZ,
                source.Rotation, source.MapContextId);

            foreach (var logosId in unitOfWork.CharacterLogoses.GetLogos(source.Id))
                unitOfWork.CharacterLogoses.SetLogos(cloneId, logosId);

            foreach (var teleporter in unitOfWork.CharacterTeleporters.Get(source.Id))
                unitOfWork.CharacterTeleporters.Add(
                    new CharacterTeleporterEntry(cloneId, teleporter.WaypointId, teleporter.WaypointType));
        }

        public void RequestCreateCharacterInSlot(Client client, RequestCreateCharacterInSlotPacket packet)
        {
            // The selection screen is the only place the client sends this from. Nothing else here
            // is safe against a create that arrives while a character is loaded: the new row is
            // written, the account entry is reloaded under a live manifestation, and the caller
            // has no reason to be anywhere but the pod screen.
            if (client.State != ClientState.CharacterSelection)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry.Id} tried to create a character while in state {client.State}.");

                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return;
            }

            var result = packet.Validate();
            if (result != CreateCharacterResult.Success)
            {
                SendCharacterCreateFailed(client, result);
                return;
            }

            // The pods are 1..MaxSelectionPods. The packet used to take any byte, and the row was
            // inserted with whatever it said: slot 0 or 17+ made a character no pod ever shows and
            // no switch can reach, which still counted for the family-name lock and "has
            // characters"; a second character in an occupied slot was worse, because character
            // selection keys the account's characters by slot and threw on the duplicate at every
            // login from then on, locking the account out until someone edited the table.
            if (packet.SlotNum < 1 || packet.SlotNum > MaxSelectionPods)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry.Id} tried to create a character in slot {packet.SlotNum}.");

                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return;
            }

            // AccountEntry.Characters is reloaded after every create and delete, and an account
            // can only be logged in once, so this is current. The unique index on
            // (account_id, slot) is the backstop if it ever is not.
            if (client.AccountEntry.GetCharacterBySlot(packet.SlotNum) != null)
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.CharacterSlotInUse);
                return;
            }

            uint characterId;
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();

            // TODO to remove this lock, the family name check and update must be redesigned to be thread safe
            lock (_createLock)
            {
                if (!TryPersistCharacterCreation(
                        client,
                        unitOfWork,
                        () => InternalCreate(client, packet, unitOfWork),
                        out characterId))
                {
                    return;
                }
            }

            // give first lockbox tab
            if (unitOfWork.CharacterLockboxes.Get(client.AccountEntry.Id) == null)
                unitOfWork.CharacterLockboxes.Add(client.AccountEntry.Id);

			unitOfWork.Complete();
			
            client.CallMethod(SysEntity.ClientMethodId, new CharacterCreateSuccessPacket(packet.SlotNum, packet.FamilyName));

            client.ReloadGameAccountEntry();

            var character = unitOfWork.Characters.Get(characterId);
            SendCharacterInfo(client, packet.SlotNum, character);
        }

        #region Name changes

        /// <summary>
        /// Names the client will accept, from PM_NAME_TOO_SHORT, PM_NAME_TOO_LONG and
        /// PM_NAME_FORMAT_INVALID: "Your name must start with a capital letter, contain only
        /// letters, and must not contain letters repeated more than twice in a row", 3 to 20
        /// characters.
        /// </summary>
        public const int MinNameLength = 3;
        public const int MaxNameLength = 20;

        /// <summary>/changefirstname: renames the character the player is on.</summary>
        internal void ChangeFirstName(Client client, ChangeFirstNamePacket packet)
        {
            if (!IsNameChanger(client))
                return;

            Rename(client, client, packet.Name, false);
        }

        /// <summary>/changelastname: renames the account's family, so every character on it.</summary>
        internal void ChangeLastName(Client client, ChangeLastNamePacket packet)
        {
            if (!IsNameChanger(client))
                return;

            Rename(client, client, packet.Name, true);
        }

        /// <summary>
        /// Renames a character or an account family, telling the player who asked what went
        /// wrong. The target can be another player, for the GM command.
        /// </summary>
        public bool Rename(Client requester, Client target, string newName, bool familyName)
        {
            if (target?.Player == null || target.AccountEntry == null)
                return false;

            var name = newName?.Trim() ?? string.Empty;
            var oldName = familyName ? target.Player.FamilyName : target.Player.Name;

            if (string.Equals(oldName, name, StringComparison.Ordinal))
                return false;

            if (!IsValidName(name, out var formatError))
            {
                NameMessage(requester, formatError);
                return false;
            }

            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();

            if (new Censor(unitOfWork.CensoredWords.GetCensoredWords()).ContainsProfanity(name))
            {
                NameMessage(requester, PlayerMessage.PmNameUnacceptable);
                return false;
            }

            if (familyName)
            {
                if (!unitOfWork.GameAccounts.CanChangeFamilyName(target.AccountEntry.Id, name))
                {
                    NameMessage(requester, PlayerMessage.PmFamilyNameReserved);
                    return false;
                }

                unitOfWork.GameAccounts.UpdateFamilyName(target.AccountEntry.Id, name);
                target.Player.FamilyName = name;
            }
            else
            {
                // Character creation never checked this, so duplicates can exist already; a
                // rename at least does not add more.
                if (unitOfWork.Characters.IsCharacterNameTaken(name, target.Player.Id))
                {
                    NameMessage(requester, PlayerMessage.PmNameInUse);
                    return false;
                }

                unitOfWork.Characters.UpdateCharacterName(target.Player.Id, name);
                target.Player.Name = name;
            }

            // UpdateCharacterName saves as it goes; UpdateFamilyName only changes the tracked
            // row, and the unit of work discards that on dispose unless it is completed. The
            // family name change was lost here, and ReloadGameAccountEntry then read the old
            // name straight back.
            unitOfWork.Complete();

            target.ReloadGameAccountEntry();

            // CharacterName and ActorName are part of the entity data every client gets when it
            // first sees the player (CreatePlayerEntityData); resending them updates the name on
            // screen for everyone nearby without a relog. Characters of this account that are not
            // in the world pick the family name up the next time they log in.
            var mapChannel = target.Player.MapChannel;

            if (mapChannel != null)
                CellManager.Instance.CellCallMethod(mapChannel, target.Player,
                    familyName ? new ActorNamePacket(target.Player.FamilyName) : (PythonPacket)new CharacterNamePacket(target.Player.Name));

            var args = new Dictionary<string, string> { ["oldname"] = oldName ?? string.Empty, ["newname"] = name };
            var changed = familyName ? PlayerMessage.PmLastNameChanged : PlayerMessage.PmFirstNameChanged;

            target.CallMethod(SysEntity.CommunicatorId, new DisplayClientMessagePacket(changed, args, MsgFilterId.GeneralSystemMessages));

            if (requester != target)
                requester.CallMethod(SysEntity.CommunicatorId, new DisplayClientMessagePacket(changed, args, MsgFilterId.GeneralSystemMessages));

            Logger.WriteLog(LogType.Command, $"{requester.AccountEntry.FamilyName} changed {(familyName ? "the family name" : "the character name")} of account {target.AccountEntry.Id} from {oldName} to {name}");

            return true;
        }

        public static bool IsValidName(string name, out PlayerMessage error)
        {
            error = PlayerMessage.PmNameFormatInvalid;

            if (string.IsNullOrEmpty(name) || name.Length < MinNameLength)
            {
                error = PlayerMessage.PmNameTooShort;
                return false;
            }

            if (name.Length > MaxNameLength)
            {
                error = PlayerMessage.PmNameTooLong;
                return false;
            }

            if (!char.IsUpper(name[0]))
                return false;

            for (var i = 0; i < name.Length; i++)
            {
                if (!char.IsLetter(name[i]))
                    return false;

                // No letter three times in a row.
                if (i >= 2 && char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(name[i - 1])
                           && char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(name[i - 2]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Name changes are a GM tool here: the slash commands are open to every player, and a
        /// free rename at any moment is a way to be mistaken for someone else.
        /// </summary>
        /// <summary>
        /// Who may use /changefirstname and /changelastname.
        ///
        /// GameMaster, matching the .rename command. It was "any GM level at all", which let an
        /// Observer - the level that exists to read the world without changing it, and the level
        /// every pre-existing account was left on - rename itself and its whole account family.
        /// </summary>
        private static bool IsNameChanger(Client client)
        {
            if (client?.AccountEntry == null || client.Player == null)
                return false;

            if (client.AccountEntry.Level >= (byte)GmLevel.GameMaster)
                return true;

            Logger.WriteLog(LogType.Security, $"AccountId = {client.AccountEntry.Id} tried to change a name without being a GM");
            CommunicatorManager.Instance.SystemMessage(client, "Name changes are done by a GM.");

            return false;
        }

        private static void NameMessage(Client client, PlayerMessage message)
        {
            client.CallMethod(SysEntity.CommunicatorId, new DisplayClientMessagePacket(message, new Dictionary<string, string>(), MsgFilterId.GeneralSystemMessages));
        }

        #endregion

        private uint? InternalCreate(Client client, RequestCreateCharacterInSlotPacket packet, ICharUnitOfWork unitOfWork)
        {
            var changeFamilyName = false;
            if (!string.IsNullOrWhiteSpace(client.AccountEntry.FamilyName) && packet.FamilyName != client.AccountEntry.FamilyName)
            {
                if (!client.AccountEntry.Characters.Any())
                {
                    changeFamilyName = true;
                }
                else
                {
                    SendCharacterCreateFailed(client, CreateCharacterResult.InvalidCharacterName);
                    return null;
                }
            }

            if ((string.IsNullOrWhiteSpace(client.AccountEntry.FamilyName) || packet.FamilyName != client.AccountEntry.FamilyName))
            {
                if (!unitOfWork.GameAccounts.CanChangeFamilyName(client.AccountEntry.Id, packet.FamilyName))
                {
                    SendCharacterCreateFailed(client, CreateCharacterResult.FamilyNameReserved);
                    return null;
                }
            }

            var characterEntry = unitOfWork.Characters.Create(client.AccountEntry, packet.SlotNum,
                packet.CharacterName,
                (byte)packet.RaceId,
                packet.Scale,
                packet.Gender);
            if (characterEntry == null)
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return null;
            }

            var appearances = CreateCharacterAppearanceEntries(packet.AppearanceData);
            if (!unitOfWork.CharacterAppearances.Add(characterEntry, appearances))
            {
                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return null;
            }

            unitOfWork.CharacterStartingExperience.Add(new CharacterStartingExperienceEntry(
                characterEntry.Id,
                Deployment11StartingExperienceRevision,
                CharacterStartingExperienceState.Pending));

            if (string.IsNullOrWhiteSpace(client.AccountEntry.FamilyName) || changeFamilyName)
            {
                unitOfWork.GameAccounts.UpdateFamilyName(client.AccountEntry.Id, packet.FamilyName);
            }

            return characterEntry.Id;
        }

        private bool TryPersistCharacterCreation(
            Client client,
            ICharUnitOfWork unitOfWork,
            Func<uint?> createOperation,
            out uint characterId)
        {
            characterId = 0;
            try
            {
                uint? createdCharacterId = null;
                unitOfWork.ExecuteTransaction(() => createdCharacterId = createOperation());

                if (createdCharacterId == null)
                    return false;

                characterId = createdCharacterId.Value;
                return true;
            }
            catch (Exception error) when (error is DbUpdateException or DbException)
            {
                Logger.WriteLog(LogType.Error, $"Character creation failed: {error}");
                SendCharacterCreateFailed(client, CreateCharacterResult.TechnicalDifficulty);
                return false;
            }
        }

        private IEnumerable<CharacterAppearanceEntry> CreateCharacterAppearanceEntries(
            IDictionary<EquipmentData, AppearanceData> appearanceData)
        {
            yield return new CharacterAppearanceEntry((uint)EquipmentData.Shoes, (uint)EntityClasses.ArmorRecruitV01CMNBoots, 2139062144);
            yield return new CharacterAppearanceEntry((uint)EquipmentData.Torso, (uint)EntityClasses.ArmorRecruitV01CMNVest, 2139062144);
            yield return new CharacterAppearanceEntry((uint)EquipmentData.Legs, (uint)EntityClasses.ArmorRecruitV01CMNLegs, 2139062144);

            using var worldUnitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            var appearancesFromPacket = appearanceData
                .Select(appearance => CreateCharacterAppearanceEntry(appearance.Value, worldUnitOfWork))
                .ToList();

            foreach (var characterAppearanceEntry in appearancesFromPacket)
            {
                yield return characterAppearanceEntry;
            }
        }

        private static CharacterAppearanceEntry CreateCharacterAppearanceEntry(AppearanceData appearanceData, IWorldUnitOfWork unitOfWork)
        {
            var databaseEntry = appearanceData.GetDatabaseEntry();
            databaseEntry.Class = unitOfWork.Equipment.GetItemClass(appearanceData.Class);
            return databaseEntry;
        }

        /// <summary>
        /// Deleting is something the character selection screen asks for, and the shipped client
        /// only offers it there. Nothing refused the packet from a client that was in the world,
        /// though, so a modified one could delete the character its own player was standing in -
        /// leaving the session running against a row that no longer exists.
        ///
        /// The test is the connection's state rather than Player.MapChannel: RemovePlayer takes
        /// the client out of the map's client list but leaves the channel reference on the
        /// Manifestation, so a player who reached selection through /logout still has one, and
        /// gating on it would refuse a delete that is perfectly legitimate.
        ///
        /// Any delete from in the world is refused, not just of the character being played. A
        /// real client cannot ask for either, and deleting one of your other characters
        /// mid-session is no more a thing the selection screen can do.
        /// </summary>
        public void RequestDeleteCharacterInSlot(Client client, RequestDeleteCharacterInSlotPacket packet)
        {
            if (client.State == ClientState.Ingame
                || client.State == ClientState.Loading
                || client.State == ClientState.Teleporting)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry.Id} tried to delete the character in slot {packet.Slot} "
                    + $"while in the world (state {client.State}).");

                client.CallMethod(SysEntity.ClientMethodId, new DeleteCharacterFailedPacket());
                return;
            }

            try
            {
                var charactersBySlot = client.AccountEntry.GetCharacterBySlot(packet.Slot);
                if (charactersBySlot == null)
                {
                    return;
                }

                int listings;

                using (var unitOfWork = _gameUnitOfWorkFactory.CreateChar())
                {
                    listings = 0;
                    unitOfWork.ExecuteTransaction(() =>
                    {
                        unitOfWork.CharacterAppearances.DeleteForChar(charactersBySlot.Id);
                        unitOfWork.CharacterMissions.RemoveAll(charactersBySlot.Id);

                        // An auction row names its seller by id and carries no foreign key, so a
                        // character deleted with listings running used to leave them standing.
                        listings = unitOfWork.Auctions?.DeleteAuctionsBySeller(
                            charactersBySlot.Id) ?? 0;

                        // TODO delete ClanMember entry
                        unitOfWork.Characters.Delete(charactersBySlot.Id);
                    });
                }

                if (listings > 0)
                    Logger.WriteLog(LogType.Debug,
                        $"Character {charactersBySlot.Id} was deleted with {listings} auction(s) running; the listings were taken down with it.");

                ReleaseOwnedPrivateStartingExperienceRuntime(charactersBySlot.Id);

                // Client.Player still points at the character that was just deleted - it is left
                // loaded when the player returns to character selection. Client.SaveCharacter
                // skips a player whose Id is 0 and otherwise looks the row up with
                // GetWritableEnsuring, so dropping the connection from here (Alt+F4 at the
                // selection screen) would go looking for a row that no longer exists and throw.
                // Close() catches that, so it only ever cost a misleading "Failed to save
                // character on disconnect" in the log - but there is genuinely nothing left to
                // save, and the log should not say otherwise.
                if (client.Player != null && client.Player.Id == charactersBySlot.Id)
                    client.Player.Id = 0;

                client.ReloadGameAccountEntry();

                client.CallMethod(SysEntity.ClientMethodId, new CharacterDeleteSuccessPacket(client.AccountEntry.Characters.Any()));

                SendCharacterInfo(client, packet.Slot, null);
            }
            catch
            {
                client.CallMethod(SysEntity.ClientMethodId, new DeleteCharacterFailedPacket());
            }
        }

        public void RequestSwitchToCharacterInSlot(Client client, RequestSwitchToCharacterInSlotPacket packet)
        {
            // Only from the pod screen. From the world this replaced the manifestation while
            // the old one was still in its map's cells and every manager's tables - never
            // removed, a frozen copy for everyone else, and the client in two maps at once.
            if (client.State != ClientState.CharacterSelection || client.PendingTransfer != null)
            {
                Logger.WriteLog(LogType.Security,
                    $"AccountId = {client.AccountEntry?.Id} tried to switch to the character in slot {packet.SlotNum} while in state {client.State}.");
                return;
            }

            if (packet.SlotNum < 1 || packet.SlotNum > MaxSelectionPods)
                return;

            CharacterEntry character = null;
            CharacterStartingExperienceState? startingState = null;
            var rejectedSelection = false;
            try
            {
                using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                unitOfWork.ExecuteTransaction(() =>
                {
                    // Look before the selected slot is changed: it used to be written first, so a
                    // switch to an empty pod left the account pointing at nothing.
                    character = unitOfWork.Characters.GetByAccountId(client.AccountEntry.Id, packet.SlotNum);

                    if (character == null)
                    {
                        Logger.WriteLog(LogType.Security,
                            $"AccountId = {client.AccountEntry.Id} tried to switch to slot {packet.SlotNum}, which is empty.");
                        rejectedSelection = true;
                        return;
                    }

                    var account = unitOfWork.GameAccounts.Get(client.AccountEntry.Id);
                    var startingExperience =
                        unitOfWork.CharacterStartingExperience.Get(character.Id);
                    if (startingExperience?.State == CharacterStartingExperienceState.Pending)
                    {
                        if (packet.SkipBootcamp)
                        {
                            if (account?.CanSkipBootcamp != true)
                            {
                                Logger.WriteLog(
                                    LogType.Security,
                                    $"AccountId = {client.AccountEntry.Id} tried to skip bootcamp for character {character.Id} without entitlement.");
                                rejectedSelection = true;
                                return;
                            }

                            if (unitOfWork.CharacterStartingExperience.TrySetState(
                                    character.Id,
                                    CharacterStartingExperienceState.Pending,
                                    CharacterStartingExperienceState.Skipped))
                                ApplyBootcampSkipParity(unitOfWork, client.AccountEntry.Id, character.Id);
                        }
                        else if (unitOfWork.CharacterStartingExperience.TrySetState(
                                     character.Id,
                                     CharacterStartingExperienceState.Pending,
                                     CharacterStartingExperienceState.Bootcamp))
                        {
                            unitOfWork.Characters.UpdateCharacterPosition(
                                character.Id,
                                BootcampStartCoordX,
                                BootcampStartCoordY,
                                BootcampStartCoordZ,
                                BootcampStartRotation,
                                BootcampPrivateMapContextId);
                            EnsureMissionActivated(
                                unitOfWork,
                                character.Id,
                                BootcampInitiationMissionId);
                        }

                        startingExperience =
                            unitOfWork.CharacterStartingExperience.Get(character.Id);
                    }

                    startingState = startingExperience?.State;
                    client.AccountEntry.SelectedSlot = packet.SlotNum;
                    unitOfWork.GameAccounts.UpdateSelectedSlot(
                        client.AccountEntry.Id,
                        packet.SlotNum);
                    unitOfWork.Characters.UpdateLoginData(character.Id);
                    character = unitOfWork.Characters.Get(character.Id);
                });
            }
            catch (Exception error) when (
                error is GameplayRejectionException ||
                error is DbUpdateException ||
                error is DbException)
            {
                Logger.WriteLog(
                    LogType.Error,
                    $"AccountId = {client.AccountEntry.Id} could not switch to slot {packet.SlotNum}: {error.Message}");
                return;
            }

            if (rejectedSelection || character == null)
                return;

            client.ReloadGameAccountEntry();
            client.Player = CreateCharacterManifestation(client, character);
            client.Player.MapChannel = ResolveReconnectMapChannel(character, startingState);
            client.LoadingMap = client.Player.MapContextId;
            MapChannelManager.Instance.PassClientToMapInstance(client);
        }

        internal static void ReleaseOwnedPrivateStartingExperienceRuntime(uint characterId)
        {
            if (characterId == 0)
                return;

            MapChannelManager.Instance.ReleaseOwnedPrivateInstances(characterId);
        }

        private static MapChannel ResolveReconnectMapChannel(
            CharacterEntry character,
            CharacterStartingExperienceState? startingState)
        {
            if (character?.MapContextId == BootcampPrivateMapContextId &&
                startingState == CharacterStartingExperienceState.Bootcamp)
                return MapChannelManager.Instance.GetOrCreatePrivateInstance(
                           BootcampPrivateMapContextId,
                           character.Id) ??
                       MapChannelManager.Instance.FindByContextId(
                           BootcampPrivateMapContextId);

            return MapChannelManager.Instance.FindByContextId(character?.MapContextId ?? 0);
        }

        internal bool TryDepartBootcampFromExitPad(Client client)
        {
            if (client?.Player == null ||
                client.State != ClientState.Ingame ||
                client.PendingTransfer != null ||
                client.Player.MapChannel == null ||
                !client.Player.MapChannel.IsPrivateInstance ||
                client.Player.MapChannel.OwnerCharacterId != client.Player.Id ||
                client.Player.MapContextId != BootcampPrivateMapContextId)
                return false;

            var destinationMap = MapChannelManager.Instance.FindByContextId(
                BootcampArrivalMapContextId);
            if (destinationMap == null)
                return false;

            var departed = false;
            try
            {
                using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                unitOfWork.ExecuteTransaction(() =>
                {
                    var startingExperience =
                        unitOfWork.CharacterStartingExperience.Get(client.Player.Id);
                    if (startingExperience?.State != CharacterStartingExperienceState.Bootcamp ||
                        !unitOfWork.CharacterStartingExperience.TrySetState(
                            client.Player.Id,
                            CharacterStartingExperienceState.Bootcamp,
                            CharacterStartingExperienceState.Completed))
                        return;

                    if (ResolveBootcampDepartureMissionId(
                            unitOfWork,
                            client.Player.Id) == null)
                        throw new GameplayRejectionException(
                            "Bootcamp departure is not ready.");

                    unitOfWork.Characters.ReconcileBootcampCharacter(
                        client.Player.Id,
                        BootcampParityExperience,
                        BootcampParityLevel,
                        (uint)CharacterClass.Recruit,
                        BootcampArrivalCoordX,
                        BootcampArrivalCoordY,
                        BootcampArrivalCoordZ,
                        BootcampArrivalRotation,
                        BootcampArrivalMapContextId);
                    EnsureQualification(
                        unitOfWork,
                        client.Player.Id,
                        CharacterQualificationKey.BootcampComplete);
                    if (!unitOfWork.GameAccounts.TryUpdateCanSkipBootcamp(
                            client.AccountEntry.Id,
                            false,
                            true))
                        unitOfWork.GameAccounts.UpdateCanSkipBootcamp(
                            client.AccountEntry.Id,
                            true);
                    departed = true;
                });
            }
            catch (Exception error) when (
                error is GameplayRejectionException ||
                error is DbUpdateException ||
                error is DbException)
            {
                Logger.WriteLog(
                    LogType.Error,
                    $"Unable to complete bootcamp departure for character {client.Player.Id}: {error.Message}");
                return false;
            }

            if (!departed)
                return false;

            client.Player.Class = (uint)CharacterClass.Recruit;
            client.Player.Experience = BootcampParityExperience;
            client.Player.Level = BootcampParityLevel;
            client.AccountEntry.CanSkipBootcamp = true;

            return MapChannelManager.Instance.ChangeMap(
                client,
                destinationMap,
                new System.Numerics.Vector3(
                    (float)BootcampArrivalCoordX,
                    (float)BootcampArrivalCoordY,
                    (float)BootcampArrivalCoordZ),
                (float)BootcampArrivalRotation,
                releaseOwnedPrivateInstancesForCharacterId: client.Player.Id);
        }

        private void ApplyBootcampSkipParity(
            ICharUnitOfWork unitOfWork,
            uint accountId,
            uint characterId)
        {
            unitOfWork.Characters.ReconcileBootcampCharacter(
                characterId,
                BootcampParityExperience,
                BootcampParityLevel,
                (uint)CharacterClass.Recruit,
                BootcampArrivalCoordX,
                BootcampArrivalCoordY,
                BootcampArrivalCoordZ,
                BootcampArrivalRotation,
                BootcampArrivalMapContextId);
            EnsureQualification(
                unitOfWork,
                characterId,
                CharacterQualificationKey.BootcampComplete);
            unitOfWork.CharacterSkills.AddOrUpdate(
                characterId,
                (uint)SkillId.Lightning,
                (int)ActionId.AaRecruitLightning,
                1);
            unitOfWork.CharacterAbilityDrawers.AddOrUpdate(
                characterId,
                BootcampAbilitySlot,
                (int)ActionId.AaRecruitLightning,
                1);
            EnsureWaypoint(
                unitOfWork,
                characterId,
                BootcampAliaWaypointId,
                WaypointType.Waypoint);
            EnsureWaypoint(
                unitOfWork,
                characterId,
                BootcampAliaHospitalId,
                WaypointType.Hospital);
            GrantSkipInventory(unitOfWork, accountId, characterId);
        }

        private void GrantSkipInventory(
            ICharUnitOfWork unitOfWork,
            uint accountId,
            uint characterId)
        {
            var inventoryRows = unitOfWork.CharacterInventories.GetItems(accountId)
                .Where(entry =>
                    entry.CharacterId == characterId &&
                    entry.InventoryType == (uint)InventoryType.Personal)
                .OrderBy(entry => entry.SlotId)
                .ToArray();
            var usedSlots = inventoryRows.Select(entry => entry.SlotId).ToHashSet();
            var existingTemplateIds = inventoryRows
                .Select(entry => unitOfWork.Items.GetItem(entry.ItemId)?.ItemTemplateId ?? 0)
                .Where(itemTemplateId => itemTemplateId != 0)
                .ToHashSet();

            using var world = _gameUnitOfWorkFactory.CreateWorld();
            var templateIds = BootcampSkipEquipmentTemplateIds
                .Concat(new[] { BootcampSkipAmmoTemplateId })
                .Distinct()
                .ToArray();
            var templates = world.Equipment.GetItemTemplates()
                .Where(entry => templateIds.Contains(entry.Id))
                .ToDictionary(entry => entry.Id);
            var classesByTemplate = world.Equipment.GetItemTemplateClasses()
                .Where(entry => templateIds.Contains(entry.ItemTemplateId))
                .ToDictionary(entry => entry.ItemTemplateId, entry => entry.ItemClass);
            var itemClasses = world.Equipment.GetItemClasses()
                .Where(entry => classesByTemplate.Values.Contains(entry.Id))
                .ToDictionary(entry => entry.Id);

            foreach (var templateId in BootcampSkipEquipmentTemplateIds
                         .Concat(new[] { BootcampSkipAmmoTemplateId }))
            {
                if (existingTemplateIds.Contains(templateId))
                    continue;

                if (!templates.TryGetValue(templateId, out var template) ||
                    !classesByTemplate.TryGetValue(templateId, out var classId) ||
                    !itemClasses.TryGetValue(classId, out var itemClass))
                    throw new GameplayRejectionException(
                        $"Bootcamp skip item template {templateId} is unavailable.");

                var slot = FindNextPersonalSlot(usedSlots, (InventoryCategory)template.InventoryCategory);
                var item = new Item(
                    templateId,
                    templateId == BootcampSkipAmmoTemplateId
                        ? BootcampSkipAmmoQuantity
                        : 1,
                    itemClass.MaxHitPoints,
                    2139062144)
                {
                    OwnerId = characterId,
                    OwnerSlotId = slot,
                    Crafter = string.Empty
                };
                var itemId = unitOfWork.Items.CreateItem(item);
                unitOfWork.CharacterInventories.AddInvItem(
                    accountId,
                    characterId,
                    (uint)InventoryType.Personal,
                    slot,
                    itemId);
                usedSlots.Add(slot);
            }
        }

        private void EnsureMissionActivated(
            ICharUnitOfWork unitOfWork,
            uint characterId,
            uint missionId)
        {
            if (unitOfWork.CharacterMissions.GetByCharacterAndMission(
                    characterId,
                    missionId) != null)
                return;

            var definition = (_missionManager ?? MissionManager.Instance).LoadedMissions
                .GetValueOrDefault(missionId);
            if (definition?.IsOperational != true)
                throw new GameplayRejectionException(
                    $"Bootcamp mission {missionId} is unavailable.");

            var completeable = definition.Objectives.Values
                .Where(objective => objective.IsRequired.Value)
                .All(objective => objective.InitialState.Value == MissionObjectiveState.Completed);
            unitOfWork.CharacterMissions.Add(
                new CharacterMissionEntry(
                    characterId,
                    missionId,
                    (uint)MissionState.Active)
                {
                    Completeable = completeable
                });
            unitOfWork.CharacterMissionProgress.AddObjectives(
                definition.Objectives.Values.Select(objective =>
                {
                    var entry = new CharacterMissionObjectiveEntry(
                        characterId,
                        missionId,
                        objective.ObjectiveId,
                        (byte)objective.InitialState.Value);
                    foreach (var counter in objective.Counters)
                        entry.Counters.Add(
                            new CharacterMissionObjectiveCounterEntry(
                                characterId,
                                missionId,
                                objective.ObjectiveId,
                                counter.Key,
                                counter.Value.InitialValue));
                    foreach (var counter in objective.ItemCounters)
                        entry.ItemCounters.Add(
                            new CharacterMissionObjectiveItemCounterEntry(
                                characterId,
                                missionId,
                                objective.ObjectiveId,
                                counter.Key,
                                counter.Value.InitialValue));
                    return entry;
                }));
        }

        private static uint? ResolveBootcampDepartureMissionId(
            ICharUnitOfWork unitOfWork,
            uint characterId)
        {
            foreach (var missionId in new[]
                     {
                         BootcampRetryFinaleMissionId,
                         BootcampFinaleMissionId
                     })
            {
                var mission = unitOfWork.CharacterMissions.GetByCharacterAndMission(
                    characterId,
                    missionId);
                if (mission?.MissionState == (uint)MissionState.Active &&
                    mission.Completeable)
                    return missionId;
            }

            return null;
        }

        private static void EnsureQualification(
            ICharUnitOfWork unitOfWork,
            uint characterId,
            CharacterQualificationKey qualification)
        {
            if (!unitOfWork.CharacterQualifications.HasQualification(
                    characterId,
                    qualification))
            {
                unitOfWork.CharacterQualifications.Add(
                    new CharacterQualificationEntry(characterId, qualification));
            }
        }

        private static void EnsureWaypoint(
            ICharUnitOfWork unitOfWork,
            uint characterId,
            uint waypointId,
            WaypointType type)
        {
            if (unitOfWork.CharacterTeleporters.Get(characterId).Any(entry =>
                    entry.WaypointId == waypointId &&
                    entry.WaypointType == (byte)type))
                return;

            unitOfWork.CharacterTeleporters.Add(
                new CharacterTeleporterEntry(
                    characterId,
                    waypointId,
                    (byte)type));
        }

        private static uint FindNextPersonalSlot(
            ISet<uint> usedSlots,
            InventoryCategory category)
        {
            var start = ((int)category - 1) * InventoryManager.PersonalCategorySize;
            var end = start + InventoryManager.PersonalCategorySize;
            for (uint slot = (uint)start; slot < end; slot++)
                if (!usedSlots.Contains(slot))
                    return slot;

            throw new GameplayRejectionException(
                $"No personal inventory slot is available for {category}.");
        }

        private void SendCharacterCreateFailed(Client client, CreateCharacterResult result)
        {
            client.CallMethod(SysEntity.ClientMethodId, new UserCreationFailedPacket(result));
        }

        private void SendCharacterInfoProdCreate(Client client, byte slot, [CanBeNull] CharacterEntry data)
        {
            var newEntityPacket = new CreatePhysicalEntityPacket(SelectionPodStartEntityId + slot, EntityClasses.CharacterSelectionPod);

            var characterInfo = CreateCharacterInfoPacket(client, slot, data);

            newEntityPacket.EntityData.Add(characterInfo);

            client.CallMethod(SysEntity.ClientMethodId, newEntityPacket);
        }

        private void SendCharacterInfo(Client client, byte slot, [CanBeNull] CharacterEntry data)
        {
            var characterInfo = CreateCharacterInfoPacket(client, slot, data);

            client.CallMethod(SelectionPodStartEntityId + slot, characterInfo);
        }

        private CharacterInfoPacket CreateCharacterInfoPacket(Client client, byte slot, [CanBeNull] CharacterEntry data)
        {
            var characterInfo = data == null
                ? new CharacterInfoPacket(slot, slot == client.AccountEntry.SelectedSlot, client.AccountEntry.FamilyName)
                : new CharacterInfoPacket(slot, slot == client.AccountEntry.SelectedSlot, client.AccountEntry.FamilyName, data);
            return characterInfo;
        }

        private Manifestation CreateCharacterManifestation(Client client, CharacterEntry character)
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
            var characterAppearances = unitOfWork.CharacterAppearances.GetByCharacterId(character.Id);
            var appearanceData = new Dictionary<EquipmentData, AppearanceData>();
            var lockboxInfo = unitOfWork.CharacterLockboxes.Get(client.AccountEntry.Id);
            var clan = unitOfWork.Clans.GetClanByCharacterId(character.Id);
            var logos = unitOfWork.CharacterLogoses.GetLogos(character.Id);

            foreach (var appearance in characterAppearances)
                appearanceData.Add((EquipmentData)appearance.Slot, new AppearanceData(appearance));

            var newCharacter = new Manifestation(character, appearanceData)
            {
                ClanId = clan?.Id ?? 0,
                ClanName = clan?.Name,
                GainedWaypoints = unitOfWork.CharacterTeleporters.Get(character.Id),
                LockboxCredits = lockboxInfo?.Credits ?? 0,
                // Floored: the free tab is not bought, so a missing or zeroed lockbox row must
                // not cost it. Sending 0 tells the client every tab is locked, including that
                // one - and its own purchase check needs the tab below unlocked, so the player
                // would have had no lockbox at all and no way to buy one.
                LockboxTabs = Math.Max(lockboxInfo?.PurashedTabs ?? 0, LockboxTab.FreeTab),
                Skills = MapChannelManager.Instance.GetPlayerSkills(character.Id),
                Titles = unitOfWork.CharacterTitles.Get(character.Id),
                Abilities = MapChannelManager.Instance.GetPlayerAbilities(character.Id),
                LoginTime = DateTime.Now,
                Logos = logos
            };
            HydrateMissions(newCharacter, unitOfWork);

            return newCharacter;
        }

        internal void HydrateMissions(
            Manifestation player,
            ICharUnitOfWork unitOfWork)
        {
            (_missionManager ?? MissionManager.Instance)
                .HydrateAndClearInvalid(player, unitOfWork);
        }

        /// <summary>
        /// Applies a signed change to one of the player's balances and keeps it inside what the
        /// column can hold: never below zero, never past int.MaxValue. A clamp firing means some
        /// caller charged without checking funds first, so it is logged rather than swallowed.
        /// </summary>
        private static int ClampCurrency(Client client, CurencyType type, int change)
        {
            var balance = (long)client.Player.Credits[type] + change;

            if (balance < 0)
            {
                Logger.WriteLog(LogType.Error, $"{client.Player.FamilyName}: {type} change of {change} would leave {balance}; clamped to 0.");
                return 0;
            }

            if (balance > int.MaxValue)
            {
                Logger.WriteLog(LogType.Error, $"{client.Player.FamilyName}: {type} change of {change} would leave {balance}; clamped to {int.MaxValue}.");
                return int.MaxValue;
            }

            return (int)balance;
        }

        public bool UpdateCharacter(Client client, CharacterUpdate job, object value = null)
        {
            if (job == CharacterUpdate.Logos)
                return TryAddLogos(client, (uint)value);

            using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
            switch (job)
            {
                case CharacterUpdate.Attributes:
                    unitOfWork.Characters.UpdateCharacterAttributes(client.Player.Id, client.Player.SpentBody, client.Player.SpentMind, client.Player.SpentSpirit);
                    break;

                case CharacterUpdate.Class:
                    unitOfWork.Characters.UpdateCharacterClass(client.Player.Id, client.Player.Class);
                    break;

                case CharacterUpdate.CloneCredits:
                    unitOfWork.Characters.UpdateCharacterCloneCredits(client.Player.Id, client.Player.CloneCredits);
                    break;

                case CharacterUpdate.Credits:
                    return PersistCurrency(client, unitOfWork, CurencyType.Credits, (int)value);

                case CharacterUpdate.Expirience:
                    unitOfWork.Characters.UpdateCharacterExpirience(client.Player.Id, client.Player.Experience);
                    break;

                case CharacterUpdate.Level:
                    unitOfWork.Characters.UpdateCharacterLevel(client.Player.Id, client.Player.Level);
                    break;

                case CharacterUpdate.Login:
                    // TotalMinutes, not Minutes: Minutes is the minute hand (0..59), so a
                    // session of an hour and ten minutes used to count as ten. TotalTimePlayed
                    // on the manifestation is the value loaded at login and LoginTime is set
                    // once, so the sum is right however many times this runs in one session.
                    var sessionMinutes = (long)(DateTime.Now - client.Player.LoginTime).TotalMinutes;
                    var totalTimePlayed = (uint)Math.Max(0, sessionMinutes) + client.Player.TotalTimePlayed;

                    unitOfWork.Characters.UpdateCharacterLogin(client.Player.Id, totalTimePlayed, client.Player.NumLogins);
                    break;

                case CharacterUpdate.Position:
                    var data = value as WonkavatePacket;

                    if (data != null)
                    {
                        // The character being moved is the one in the world; no need to go by
                        // the selected slot, which can name an empty pod.
                        unitOfWork.Characters.UpdateCharacterPosition(client.Player.Id, data.Position.X, data.Position.Y, data.Position.Z, data.Orientation, data.MapContextId);
                    }
                    else
                        unitOfWork.Characters.UpdateCharacterPosition(
                            client.Player.Id,
                            client.Player.Position.X,
                            client.Player.Position.Y,
                            client.Player.Position.Z,
                            client.Player.Rotation,
                            client.Player.MapContextId
                            );

                    break;

                case CharacterUpdate.Prestige:
                    return PersistCurrency(client, unitOfWork, CurencyType.Prestige, (int)value);

                case CharacterUpdate.Stats:
                    break;

                case CharacterUpdate.ActiveWeapon:
                    client.Player.ActiveWeapon = (byte)value;
                    unitOfWork.Characters.UpdateCharacterActiveWeapon(client.Player.Id, client.Player.ActiveWeapon);
                    break;
                case CharacterUpdate.Teleporter:
                    var teleporter = (CharacterTeleporterEntry)value;

                    unitOfWork.CharacterTeleporters.Add(teleporter);
                    break;
                default:
                    break;
            }

            return true;
        }

        internal bool TryAddLogos(Client client, uint logosId)
        {
            if (client?.Player == null || logosId == 0)
                return false;

            lock (client.SyncRoot)
            {
                if (client.Player.Logos.Contains(logosId))
                    return false;

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.CharacterLogoses.SetLogos(client.Player.Id, logosId);
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        $"Unable to persist Logos {logosId} for character {client.Player.Id}: {error}");
                    return false;
                }

                client.Player.Logos.Add(logosId);
                client.CallMethod(
                    client.Player.EntityId,
                    new LogosStoneAddedPacket(logosId));
                (_missionManager ?? MissionManager.Instance).RecordProgress(
                    client,
                    MissionProgressEvent.Logos(logosId));
                return true;
            }
        }

        private static bool PersistCurrency(
            Client client,
            ICharUnitOfWork unitOfWork,
            CurencyType type,
            int change)
        {
            if (client?.Player == null || !client.Player.Credits.TryGetValue(type, out var current))
                return false;

            var next = ClampCurrency(client, type, change);

            try
            {
                unitOfWork.ExecuteTransaction(() =>
                {
                    var character = unitOfWork.Characters.Find(client.Player.Id);
                    var durable = type == CurencyType.Credits
                        ? character?.Credit
                        : character?.Prestige;

                    if (character == null || durable != current)
                        throw new GameplayRejectionException(
                            $"Durable {type} balance changed before update.");

                    if (type == CurencyType.Credits)
                        unitOfWork.Characters.UpdateCharacterCredits(client.Player.Id, next);
                    else
                        unitOfWork.Characters.UpdateCharacterPrestige(client.Player.Id, next);
                });
            }
            catch (Exception error) when (
                error is GameplayRejectionException ||
                error is DbUpdateException ||
                error is DbException)
            {
                Logger.WriteLog(LogType.Error,
                    $"Could not persist {type} for character {client.Player.Id}: {error.Message}");
                return false;
            }

            client.Player.Credits[type] = next;
            client.CallMethod(client.Player.EntityId,
                new UpdateCreditsPacket(type, next, 0));
            return true;
        }
    }
}
