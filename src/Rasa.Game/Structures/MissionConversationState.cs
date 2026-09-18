using System.Collections.Generic;
using System.Linq;

namespace Rasa.Structures
{
    using Data;

    internal sealed class MissionConversationState
    {
        private readonly IReadOnlyDictionary<uint, MissionInfo> _dispensable;
        private readonly IReadOnlyList<CompleteableObjectives> _objectives;
        private readonly IReadOnlyDictionary<uint, RewardInfo> _completeable;
        private readonly IReadOnlyList<RewardableMissions> _rewardable;

        internal MissionConversationState(
            IReadOnlyDictionary<uint, MissionInfo> dispensable,
            IReadOnlyList<CompleteableObjectives> objectives,
            IReadOnlyDictionary<uint, RewardInfo> completeable,
            IReadOnlyList<RewardableMissions> rewardable)
        {
            _dispensable = dispensable;
            _objectives = objectives;
            _completeable = completeable;
            _rewardable = rewardable;
        }

        internal Dictionary<ConversationType, object> CreateConversationData()
        {
            var data = new Dictionary<ConversationType, object>();
            if (_dispensable.Count > 0)
                data.Add(ConversationType.MissionDispense, new Dictionary<uint, MissionInfo>(_dispensable));
            if (_objectives.Count > 0)
                data.Add(ConversationType.ObjectiveComplete, _objectives.ToList());
            if (_completeable.Count > 0)
                data.Add(ConversationType.MissionComplete, new Dictionary<uint, RewardInfo>(_completeable));
            if (_rewardable.Count > 0)
                data.Add(ConversationType.MissionReward, _rewardable.ToList());
            return data;
        }

        internal bool TryGetStatus(
            out ConversationStatus status,
            out List<uint> missionIds)
        {
            if (_rewardable.Count > 0)
            {
                status = ConversationStatus.Reward;
                missionIds = _rewardable.Select(mission => (uint)mission.MissionId).ToList();
                return true;
            }
            if (_completeable.Count > 0)
            {
                status = ConversationStatus.MissionComplete;
                missionIds = _completeable.Keys.ToList();
                return true;
            }
            if (_objectives.Count > 0)
            {
                status = ConversationStatus.ObjectivComplete;
                missionIds = _objectives
                    .Select(objective => (uint)objective.MissionId)
                    .Distinct()
                    .ToList();
                return true;
            }
            if (_dispensable.Count > 0)
            {
                status = ConversationStatus.Available;
                missionIds = _dispensable.Keys.ToList();
                return true;
            }

            status = ConversationStatus.None;
            missionIds = new List<uint>();
            return false;
        }
    }
}
