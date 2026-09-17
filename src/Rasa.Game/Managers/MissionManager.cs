using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.Mission.Server;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;

    public class MissionManager
    {
        private const int MissionLogCapacity = 30;
        private static MissionManager _instance;
        private static readonly object InstanceLock = new();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly Dictionary<uint, Mission> _loadedMissions;

        public IReadOnlyDictionary<uint, Mission> LoadedMissions => _loadedMissions;

        public static MissionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new MissionManager(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        private MissionManager(IGameUnitOfWorkFactory gameUnitOfWorkFactory)
            : this(gameUnitOfWorkFactory, new Dictionary<uint, Mission>())
        {
        }

        public MissionManager(
            IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            IReadOnlyDictionary<uint, Mission> definitions)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _loadedMissions = definitions.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        public void LoadMissions()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            foreach (var mission in unitOfWork.NpcMissions.Get())
                _loadedMissions[mission.Id] = new Mission(mission);
        }

        internal void Hydrate(Manifestation player, IReadOnlyList<CharacterMissionEntry> rows)
        {
            var hydrated = new Dictionary<uint, MissionLog>();
            foreach (var row in rows)
            {
                if (!_loadedMissions.ContainsKey(row.MissionId))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Skipped mission {row.MissionId} for character {player.Id}: definition is not loaded.");
                    continue;
                }

                var state = (MissionState)row.MissionState;
                if (!IsPublishedState(state))
                {
                    Logger.WriteLog(LogType.Error,
                        $"Skipped mission {row.MissionId} for character {player.Id}: unsupported state {row.MissionState}.");
                    continue;
                }

                hydrated[row.MissionId] = new MissionLog(
                    row.MissionId,
                    state,
                    state == MissionState.Active && row.Completeable);
            }

            player.Missions = hydrated;
        }

        public IReadOnlyDictionary<uint, MissionInfo> BuildStatusSnapshot(Manifestation player)
        {
            var snapshot = new Dictionary<uint, MissionInfo>();
            foreach (var entry in player.Missions)
            {
                if (!_loadedMissions.TryGetValue(entry.Key, out var definition) ||
                    !IsPublishedState(entry.Value.State))
                    continue;

                snapshot.Add(entry.Key, definition.CreateInfo(entry.Value.State, entry.Value.Completeable));
            }

            return snapshot;
        }

        public void PublishInitialState(Client client)
        {
            client.CallMethod(
                client.Player.EntityId,
                new MissionStatusInfoPacket(BuildStatusSnapshot(client.Player)));
        }

        public bool TryAcceptNpcMission(Client client, ulong npcEntityId, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client))
                    return Reject($"Rejected mission {missionId}: character is not active in the world.");
                if (!_loadedMissions.TryGetValue(missionId, out var definition))
                    return Reject($"Rejected mission {missionId}: definition is not loaded.");
                if (!TryGetNpcOnPlayerMap(client.Player, npcEntityId, out var npc))
                    return Reject($"Rejected mission {missionId}: NPC entity {npcEntityId} is not in the current map instance.");
                if (npc.Npc == null || npc.DbId != definition.MissionGiver)
                    return Reject($"Rejected mission {missionId}: NPC {npc.DbId} is not its authoritative giver.");
                if (client.Player.Missions.Count >= MissionLogCapacity)
                {
                    CommunicatorManager.Instance.SystemMessage(client, "Mission log is full.");
                    return false;
                }
                if (client.Player.Missions.ContainsKey(missionId))
                    return Reject($"Rejected mission {missionId}: character {client.Player.Id} already has it.");

                var log = new MissionLog(missionId, MissionState.Active, false);
                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                        unitOfWork.CharacterMissions.Add(new CharacterMissionEntry(
                            client.Player.Id,
                            missionId,
                            (uint)MissionState.Active)));
                }
                catch (Exception error) when (error is DbUpdateException || error is DbException)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to accept mission {missionId} for character {client.Player.Id}: {error.Message}");
                    return false;
                }

                client.Player.Missions.Add(missionId, log);
                client.CallMethod(
                    client.Player.EntityId,
                    new MissionGainedPacket(missionId, definition.CreateInfo(log.State, log.Completeable)));
                return true;
            }
        }

        internal bool TryAbandon(Client client, uint missionId)
        {
            if (client == null)
                return false;

            lock (client.SyncRoot)
            {
                if (!IsActivePlayer(client) ||
                    !client.Player.Missions.TryGetValue(missionId, out var log) ||
                    log.State != MissionState.Active)
                    return false;

                try
                {
                    using var unitOfWork = _gameUnitOfWorkFactory.CreateChar();
                    unitOfWork.ExecuteTransaction(() =>
                        unitOfWork.CharacterMissions.Remove(client.Player.Id, missionId));
                }
                catch (Exception error) when (error is DbUpdateException || error is DbException)
                {
                    Logger.WriteLog(LogType.Error,
                        $"Unable to abandon mission {missionId} for character {client.Player.Id}: {error.Message}");
                    return false;
                }

                client.Player.Missions.Remove(missionId);
                client.CallMethod(client.Player.EntityId, new MissionDiscardedPacket(missionId));
                return true;
            }
        }

        private static bool IsPublishedState(MissionState state) =>
            state == MissionState.Active ||
            state == MissionState.Failded ||
            state == MissionState.Completed;

        private static bool IsActivePlayer(Client client)
        {
            var player = client.Player;
            return client.State == ClientState.Ingame &&
                client.PendingTransfer == null &&
                player != null &&
                player.Id != 0 &&
                player.MapChannel != null &&
                !player.Disconected &&
                !player.RemoveFromMap &&
                CellManager.Instance.IsInWorld(client);
        }

        private static bool TryGetNpcOnPlayerMap(
            Manifestation player,
            ulong npcEntityId,
            out Creature npc)
        {
            npc = null;
            if (!EntityManager.Instance.RegisteredEntities.TryGetValue(npcEntityId, out var entityType) ||
                entityType != EntityType.Creature ||
                !EntityManager.Instance.Creatures.TryGetValue(npcEntityId, out var candidate) ||
                candidate.MapContextId != player.MapChannel.MapInfo.MapContextId ||
                !player.MapChannel.MapCellInfo.Cells.Values.Any(
                    cell => cell.CreatureList.Any(creature => ReferenceEquals(creature, candidate))))
                return false;

            npc = candidate;
            return true;
        }

        private static bool Reject(string message)
        {
            Logger.WriteLog(LogType.Network, message);
            return false;
        }
    }
}
