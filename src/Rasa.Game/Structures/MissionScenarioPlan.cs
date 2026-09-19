using System;
using System.Collections.Generic;

namespace Rasa.Structures
{
    using Game;
    using Managers;

    internal sealed class MissionScenarioPlan : IDisposable
    {
        private readonly List<Action> _runtimeConvergence = new();
        private readonly List<Action> _publications = new();
        private readonly List<Action> _postCommit = new();
        private readonly List<MissionManager.MissionProgressPublicationPlan> _progressPlans = new();
        private readonly List<MissionManager.MissionFailurePublicationPlan> _failurePlans = new();
        private readonly List<MissionRewardGrant> _rewardGrants = new();

        internal List<string> StepKeysToAdd { get; } = new();
        internal List<string> StepKeyPrefixesToRemove { get; } = new();
        internal List<string> ExactStepKeysToRemove { get; } = new();
        internal List<string> DurableKeyPrefixesToRemove { get; } = new();
        internal bool HasChanges =>
            StepKeysToAdd.Count > 0 ||
            StepKeyPrefixesToRemove.Count > 0 ||
            ExactStepKeysToRemove.Count > 0 ||
            DurableKeyPrefixesToRemove.Count > 0 ||
            _runtimeConvergence.Count > 0 ||
            _progressPlans.Count > 0 ||
            _failurePlans.Count > 0 ||
            _postCommit.Count > 0;

        internal void AddRewardGrant(MissionRewardGrant grant)
        {
            if (grant != null)
                _rewardGrants.Add(grant);
        }

        internal void AddRuntimeConvergence(Action convergence)
        {
            if (convergence != null)
                _runtimeConvergence.Add(convergence);
        }

        internal void AddPublication(Action publication)
        {
            if (publication != null)
                _publications.Add(publication);
        }

        internal void AddPostCommit(Action action)
        {
            if (action != null)
                _postCommit.Add(action);
        }

        internal void AddProgressPlan(MissionManager.MissionProgressPublicationPlan publicationPlan)
        {
            if (publicationPlan.HasChanges)
                _progressPlans.Add(publicationPlan);
        }

        internal void AddFailurePlan(MissionManager.MissionFailurePublicationPlan failurePlan)
        {
            if (!ReferenceEquals(failurePlan, MissionManager.MissionFailurePublicationPlan.Empty))
                _failurePlans.Add(failurePlan);
        }

        internal void ApplyRuntime(Client client, ManifestationManager manifestationManager, MissionManager missionManager)
        {
            foreach (var rewardGrant in _rewardGrants)
                rewardGrant.ConvergeRuntime(client);
            foreach (var convergence in _runtimeConvergence)
                convergence();
            foreach (var failurePlan in _failurePlans)
                failurePlan.Publish(client, missionManager);
            foreach (var progressPlan in _progressPlans)
                progressPlan.Publish(client);
            foreach (var rewardGrant in _rewardGrants)
                rewardGrant.Publish(client, manifestationManager);
            foreach (var publication in _publications)
                publication();
            foreach (var action in _postCommit)
                action();
        }

        public void Dispose()
        {
            foreach (var rewardGrant in _rewardGrants)
                rewardGrant.Dispose();
        }
    }
}
