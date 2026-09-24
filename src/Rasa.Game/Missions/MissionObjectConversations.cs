using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Rasa.Game.Missions
{
    using Data;
    using Managers;
    using Packets.MapChannel.Server;
    using Repositories.Char;
    using Repositories.UnitOfWork;
    using Structures;

    internal sealed class MissionObjectConversations
    {
        private readonly IGameUnitOfWorkFactory _factory;
        private readonly MissionApplication _missions;

        internal MissionObjectConversations(IGameUnitOfWorkFactory factory, MissionApplication missions)
        {
            _factory = factory;
            _missions = missions;
        }

        internal bool IsAvailable(Client client, DynamicObject obj) =>
            obj?.MissionConversation is { } binding && obj.IsEnabled &&
            (obj.SceneOwnerCharacterId == 0 || obj.SceneOwnerCharacterId == client?.Player?.Id) &&
            client?.Player?.Missions.TryGetValue(binding.MissionId, out var mission) == true &&
            mission.State == MissionState.Active &&
            mission.Objectives.TryGetValue(binding.ObjectiveId, out var objective) &&
            objective.State == MissionObjectiveState.Incomplete;

        internal NPCConversationStatusPacket Status(Client client, DynamicObject obj) =>
            new(IsAvailable(client, obj) ? ConversationStatus.ObjectivComplete : ConversationStatus.None,
                IsAvailable(client, obj) ? new List<uint> { obj.MissionConversation.MissionId } : new List<uint>());

        internal bool Open(Client client, ulong entityId)
        {
            if (client == null)
                return false;
            lock (client.SyncRoot)
            {
                client.PendingObjectConversation = null;
                if (!TryResolve(client, entityId, out var obj))
                    return Reject("Conversation object is unavailable, out of range or belongs to another character.");
                var binding = obj.MissionConversation;
                try
                {
                    using var unit = _factory.CreateChar();
                    var assignment = unit.CharacterMissions.GetByCharacterAndMission(client.Player.Id, binding.MissionId);
                    if (!DurableAvailable(client, obj, unit) || assignment == null)
                        return Reject("The conversation objective is not available.");
                    client.PendingObjectConversation = (obj, assignment.AssignmentId);
                    client.CallMethod(obj.EntityId, new ConversePacket(new Dictionary<ConversationType, object>
                    {
                        [ConversationType.ObjectiveComplete] = new List<CompleteableObjectives>
                        {
                            new((int)binding.MissionId, (int)binding.DialogObjectiveId, (int)binding.PlayerFlagId)
                        }
                    }));
                    return true;
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error, $"Unable to open mission object conversation {entityId}: {error}");
                    return false;
                }
            }
        }

        internal bool Complete(Client client, ulong entityId, uint missionId, uint dialogObjectiveId, uint playerFlagId)
        {
            lock (client.SyncRoot)
            {
                if (!TryResolve(client, entityId, out var obj) ||
                    client.PendingObjectConversation is not { } pending || !ReferenceEquals(pending.Object, obj))
                    return Reject("The object conversation was not opened or is no longer available.");
                var binding = obj.MissionConversation;
                if (binding.MissionId != missionId || binding.DialogObjectiveId != dialogObjectiveId ||
                    binding.PlayerFlagId != playerFlagId)
                    return Reject("The object conversation completion does not match its authored binding.");
                var plan = MissionProgressPublicationPlan.Empty;
                try
                {
                    using var unit = _factory.CreateChar();
                    unit.ExecuteTransaction(() =>
                    {
                        if (!TryResolve(client, entityId, out var current) || !ReferenceEquals(current, obj) ||
                            unit.CharacterMissions.GetByCharacterAndMission(client.Player.Id, missionId)?.AssignmentId != pending.AssignmentId ||
                            !DurableAvailable(client, obj, unit))
                            throw new GameplayRejectionException("The object conversation changed before completion.");
                        plan = _missions.PlanProgress(client,
                            new[] { MissionProgressEvent.Interaction((uint)obj.EntityClassId) }, unit,
                            new HashSet<(uint MissionId, uint ObjectiveId)> { (missionId, binding.ObjectiveId) });
                        if (!plan.HasChanges)
                            throw new GameplayRejectionException("The object conversation has no eligible progress transition.");
                    });
                }
                catch (Exception error) when (GameplayRejectionException.IsExpected(error))
                {
                    Logger.WriteLog(LogType.Error, $"Unable to complete mission object conversation {entityId}: {error}");
                    return false;
                }
                client.PendingObjectConversation = null;
                plan.Publish(client);
                MissionApplication.TryPublish(() => client.CallMethod(obj.EntityId, Status(client, obj)),
                    $"mission {missionId} object conversation closure");
                return true;
            }
        }

        private bool DurableAvailable(Client client, DynamicObject obj, ICharUnitOfWork unit)
        {
            var binding = obj.MissionConversation;
            return unit.CharacterMissions.GetByCharacterAndMission(client.Player.Id, binding.MissionId)?.MissionState ==
                    (uint)MissionState.Active &&
                unit.CharacterMissionProgress.GetTracked(client.Player.Id, binding.MissionId)
                    .TryGetValue(binding.ObjectiveId, out var objective) &&
                objective.ObjectiveState == (byte)MissionObjectiveState.Incomplete &&
                _missions.IsObjectiveEligibleAtEvent(client, binding.MissionId, binding.ObjectiveId, unit);
        }

        private bool TryResolve(Client client, ulong entityId, out DynamicObject obj)
        {
            EntityManager.Instance.TryGetObject(entityId, out obj);
            var player = client?.Player;
            return client?.State == ClientState.Ingame && client.PendingTransfer == null &&
                player?.MapChannel != null && player.Id != 0 && !player.Disconected && !player.RemoveFromMap &&
                player.State != CharacterState.Dead && CellManager.Instance.IsInWorld(client) &&
                obj?.IsInWorld == true && MapInstanceScope.Contains(player.MapChannel, obj) &&
                IsAvailable(client, obj) && Vector3.Distance(player.Position, obj.Position) <= DynamicObjectManager.MaxUseDistance;
        }

        private static bool Reject(string reason)
        {
            Logger.WriteLog(LogType.Debug, $"Rejected mission object conversation: {reason}");
            return false;
        }
    }
}
