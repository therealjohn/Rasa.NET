using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Packets.MapChannel.Server;
    using Timer;
    using Structures;

    public class ActorActionManager
    {
        private static ActorActionManager _instance;
        private static readonly object InstanceLock = new object();
        public readonly Timer Timer = new Timer();
        private readonly ManifestationManager _manifestation;
        public static ActorActionManager Instance
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new ActorActionManager();
                    }
                }

                return _instance;
            }
        }

        private ActorActionManager()
            : this(ManifestationManager.Instance)
        {
        }

        internal ActorActionManager(ManifestationManager manifestation)
        {
            _manifestation = manifestation;
        }

        public bool HasActiveAction(Actor actor)
        {
            if (actor.CurrentAction == 0)
                return false;
            
            return true;
        }

        public void DoWork(MapChannel mapChannel, long delta)
        {
            if (mapChannel.PerformRecovery.Count > 0)
            {
                if (mapChannel.PerformRecovery.Count > 1)
                    Logger.WriteLog(LogType.Debug, $"PerformRecovery count = { mapChannel.PerformRecovery.Count}");

                foreach (var action in mapChannel.PerformRecovery.ToArray().Reverse())
                {
                    if (!mapChannel.PerformRecovery.Contains(action))
                        continue;
                    if (action.Ability != null)
                    {
                        _manifestation.Abilities.Recover(mapChannel, action);
                        continue;
                    }

                    // skip if client is busy
                    if (HasActiveAction(action.Actor) && !(action.WeaponClient != null && action.IsInrerrupted))
                        continue;

                    // if action is interrupted recover immediately
                    if (action.IsInrerrupted)
                        action.PassedTime = action.WaitTime;

                    action.PassedTime += delta;

                    if (action.WaitTime <= action.PassedTime)
                    {
                        // perform action
                        PerformRecovery(mapChannel, action);
                        // remove action
                        mapChannel.PerformRecovery.Remove(action);
                    }
                }
            }
        }

        public void PerformRecovery(MapChannel mapChannel, ActionData action)
        {
            if (action.WeaponClient != null && !ReferenceEquals(action.WeaponMap, mapChannel))
                return;
            switch (action.ActionId)
            {
                case ActionId.AaRecruitLightning:
                case ActionId.AaRecruitSprint:
                    _manifestation.Abilities.Recover(mapChannel, action);
                    break;
                case ActionId.UseObject:
                    CellManager.Instance.CellCallMethod(mapChannel, action.Actor, new PerformRecoveryPacket(PerformType.TwoArgs, action.ActionId, action.ActionArgId));
                    switch (action.ActionArgId)
                    {
                        case 1:
                            DynamicObjectManager.Instance.FootlockerRecovery(mapChannel, action);
                            break;
                        case 6:
                            DynamicObjectManager.Instance.LogosRecovery(mapChannel, action);
                            break;
                        case 7:
                            DynamicObjectManager.Instance.CaptureControlPointRecovery(mapChannel, action);
                            break;
                        default:
                            Logger.WriteLog(LogType.Debug, $"PerformRecovery.UseObject: unsuported actionArgId {action.ActionArgId}");
                            break;
                    }
                    break;
                case ActionId.WeaponAttack:
                    Logger.WriteLog(LogType.Debug, $"PerformRecovery {action.ActionArgId} {action.ActionId} {action.Args}");
                    /*
                    PlayerManager.Instance.StartAutoFire(action.Client, 0D);
                    action.Client.CellCallMethod(action.Client, action.Client.MapClient.Player.Actor.EntityId, new PerformRecoveryPacket(action.ActionId, action.ActionArgId, new List<int> { 1 }));
                    */
                    break;
                case ActionId.WeaponDraw:
                    _manifestation.RecoverWeaponReady(action);
                    break;
                case ActionId.WeaponReload:
                    _manifestation.WeaponReload(action);
                    break;
                case ActionId.WeaponStow:
                    _manifestation.RecoverWeaponReady(action);
                    break;
                default:
                    Logger.WriteLog(LogType.Error, $"PerformAction: unsuported {action.ActionId}");
                    break;
            };
        }
    }
}
