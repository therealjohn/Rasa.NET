using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Managers
{
    using Data;
    using Extensions;
    using Game;
    using Packets;
    using Packets.ClientMethod.Server;
    using Packets.Game.Server;
    using Packets.MapChannel.Client;
    using Packets.MapChannel.Server;
    using Packets.Protocol;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;
    using System;

    public class DynamicObjectManager
    {
        private static DynamicObjectManager _instance;
        private static readonly object InstanceLock = new object();
        private readonly IGameUnitOfWorkFactory _gameUnitOfWorkFactory;
        private readonly MapChannelManager _maps;
        private readonly Func<long> _clock;
        private readonly Action<Client, CharacterUpdate, object> _updateCharacter;
        private readonly Action<Client> _disconnect;
        private readonly MissionManager _missionManager;
        private MapChannelManager Maps => _maps ?? MapChannelManager.Instance;

        public readonly Dictionary<ulong, Dropship> Dropships = new Dictionary<ulong, Dropship>();
        public readonly Dictionary<ulong, DynamicObject> Teleporters = new Dictionary<ulong, DynamicObject>();

        /// <summary>
        /// The UseObject arg id each kind of object is used with, which is what picks the recovery
        /// in ActorActionManager. The client reads it off the object's own usabledata row
        /// (client/augmentations/usable.py, defaulting to 1 for a class with no row), and it
        /// matches the object here: all 35 footlocker classes carry 1, all 39 station classes 5,
        /// 163 of the 166 logos classes 6, and the control point 7.
        /// </summary>
        public const uint FootlockerUseArgId = 1;

        /// <inheritdoc cref="FootlockerUseArgId"/>
        public const uint LogosUseArgId = 6;

        /// <inheritdoc cref="FootlockerUseArgId"/>
        public const uint ControlPointUseArgId = 7;

        /// <summary>
        /// How far from an object a player may be and still use it.
        ///
        /// The client's own radius is the larger of the player's use range and the object class's
        /// own: an actor's use range is 3 and a manifestation doubles it
        /// (client/augmentations/manifestation.py GetUseRange), and of every usable class only one
        /// - 26232, which is none of the objects placed here - sets a range of its own. So an
        /// honest client asks from within 6 units, and measures them from itself to the object's
        /// DAMAGE1 connection point rather than to where the object stands, which is what the rest
        /// of this allowance is for: the server knows only where it stands, and the position it
        /// has for the player is a tick behind the one the client checked. Twenty units is
        /// generous about all of that and still refuses what is worth refusing, which is a request
        /// from across the map.
        /// </summary>
        public const float MaxUseDistance = 20f;

        /// <summary>Whether an actor is on the object's map and near enough to use it.</summary>
        private static bool IsInUseRange(Actor actor, DynamicObject obj)
        {
            return obj.MapContextId == actor.MapContextId
                   && Vector3.Distance(actor.Position, obj.Position) <= MaxUseDistance;
        }

        public static DynamicObjectManager Instance
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = new DynamicObjectManager(Server.GameUnitOfWorkFactory);
                    }
                }

                return _instance;
            }
        }

        internal DynamicObjectManager(IGameUnitOfWorkFactory gameUnitOfWorkFactory,
            MapChannelManager maps = null, Func<long> clock = null,
            Action<Client, CharacterUpdate, object> updateCharacter = null,
            Action<Client> disconnect = null,
            MissionManager missionManager = null)
        {
            _gameUnitOfWorkFactory = gameUnitOfWorkFactory;
            _maps = maps;
            _clock = clock ?? (() => Environment.TickCount64);
            _updateCharacter = updateCharacter ?? ((client, update, value) =>
                CharacterManager.Instance.UpdateCharacter(client, update, value));
            _disconnect = disconnect ?? (client => client.Close(false));
            _missionManager = missionManager;
        }

        internal void InitDynamicObjects()
        {
            InitControlPoints();
            InitFootlockers();
            InitTeleporters();
            LogosManager.Instance.LogosInit();
            KraftwerksManager.Instance.KraftwerksInit();
        }

        internal void ForceState(DynamicObject obj, UseObjectState state, int delta)
        {
            CellManager.Instance.CellCallMethod(obj, new ForceStatePacket(state, delta));
        }

        internal void RequestUseObjectPacket(Client client, RequestUseObjectPacket packet)
        {
            // Teleporting counts as being in the world for the packet gate - a dropship ride keeps
            // the manifestation and everything registered with it - but the rider is between maps,
            // standing where they left, with no cells. There is nothing there for them to use.
            if (client.State != ClientState.Ingame)
                return;

            // The id names an object, or it names nothing this can answer. GetObject is the
            // throwing indexer, so any item, creature or player id - or an object id that is no
            // longer registered - closed the connection of whoever sent it.
            if (!EntityManager.Instance.TryGetObject(packet.EntityId, out var obj))
            {
                Logger.WriteLog(LogType.Debug, $"{client.Player.FamilyName} asked to use {packet.EntityId}, which is not an object.");
                return;
            }

            // Where the player is standing decides what they can reach. Object ids are handed out
            // in order and are the same for every client, so a client could walk the id space and
            // use every footlocker, station and control point on the map without leaving the spot
            // it was standing on - and collect every logos tablet on it, since the recovery asks
            // only that the player be in the object's TriggeredByPlayers list. An object on
            // another map left them in that list for good, which holds their connection open in
            // the server's memory long after they have gone.
            if (obj.MapContextId != client.Player.MapContextId)
            {
                Logger.WriteLog(LogType.Security,
                    $"{client.Player.FamilyName} asked to use object {packet.EntityId}, which is on map {obj.MapContextId} and not on {client.Player.MapContextId}. Ignored.");
                return;
            }

            var distance = Vector3.Distance(client.Player.Position, obj.Position);

            if (distance > MaxUseDistance)
            {
                Logger.WriteLog(LogType.Security,
                    $"{client.Player.FamilyName} asked to use object {packet.EntityId} from {distance:F0} units away. Ignored.");
                return;
            }

            // Using an object is the only thing a use request may ask for. The queued action
            // carried the packet's own action id to ActorActionManager.PerformRecovery, which
            // performs whatever that id names, on the player, as soon as the windup has run - so
            // any footlocker or crafting station was a hundred milliseconds of free choice over
            // the whole action table. WeaponReload filled the clip and cleared a jam with no
            // reload asked for and none of its time served, and anything AbilityManager can
            // resolve landed as an ability: its recovery is the half that takes the skill, the
            // cost, the cooldown and the range as read, because the request half checked them.
            //
            // The arg id is left as it arrived. It only picks which of the four use-object
            // recoveries runs, and each of those acts on an object that holds this player in its
            // TriggeredByPlayers - the list this request adds them to - so one that does not
            // match the object it was sent to finds nothing to do. It also has to go back
            // unchanged: the client files its pending action under (actionId, actionArgId) and
            // does not recognise its own windup or recovery under any other arg.
            if (packet.ActionId != ActionId.UseObject)
            {
                Logger.WriteLog(LogType.Security,
                    $"{client.Player.FamilyName} sent {packet.ActionId}/{packet.ActionArgId} to use object {packet.EntityId}; an object is used with {ActionId.UseObject}. Ignored.");
                return;
            }

            switch (obj.DynamicObjectType)
            {
                case DynamicObjectType.ControlPoint:
                    {
                        client.CallMethod(client.Player.EntityId, new PerformWindupPacket(PerformType.TwoArgs, packet.ActionId, packet.ActionArgId));
                        client.CallMethod(packet.EntityId, new UsePacket(client.Player.EntityId, obj.StateId, 10000));
                        client.Player.MapChannel.PerformRecovery.Add(new ActionData(client.Player, packet.ActionId, packet.ActionArgId, 10000));

                        obj.TriggeredByPlayers.Add(client);
                        break;
                    }
                case DynamicObjectType.Lockbox:
                    {
                        client.CallMethod(client.Player.EntityId, new PerformWindupPacket(PerformType.TwoArgs, packet.ActionId, packet.ActionArgId));
                        client.CallMethod(packet.EntityId, new UsePacket(client.Player.EntityId, obj.StateId, 100));
                        client.Player.MapChannel.PerformRecovery.Add(new ActionData(client.Player, packet.ActionId, packet.ActionArgId, 100));

                        obj.TriggeredByPlayers.Add(client);
                        break;
                    }
                case DynamicObjectType.Logos:
                    {
                        var actionData = new ActionData(client.Player, packet.ActionId, packet.ActionArgId, 10000);
                        actionData.SourceId = obj.EntityId;

                        client.CallMethod(client.Player.EntityId, new PerformWindupPacket(PerformType.TwoArgs, packet.ActionId, packet.ActionArgId));
                        client.CallMethod(packet.EntityId, new UsePacket(client.Player.EntityId, obj.StateId, 10000));
                        client.Player.MapChannel.PerformRecovery.Add(actionData);

                        obj.TriggeredByPlayers.Add(client);
                        break;
                    }
                case DynamicObjectType.Kraftwerks:
                    KraftwerksManager.Instance.Use(client, obj, packet.ActionArgId);
                    break;
                default:
                    Logger.WriteLog(LogType.Debug, $"ToDo: RequestUseObjectPacket: unsuported object type {obj.DynamicObjectType}");
                    break;
            }
        }

        internal void DynamicObjectWorker(MapChannel mapChannel, long delta)
        {
            // dynamicObjects
            // dropShips
            // etc...

            // controlPoints
            foreach (var entry in mapChannel.ControlPoints)
            {
                var controlPoint = entry.Value;
                // spawn object
                if (!controlPoint.IsInWorld)
                {
                    controlPoint.RespawnTime -= delta;

                    if (controlPoint.RespawnTime <= 0)
                    {
                        CellManager.Instance.AddToWorld(mapChannel, controlPoint);
                        controlPoint.IsInWorld = true;
                        controlPoint.StateId = UseObjectState.CpointStateUnclaimed;
                        controlPoint.WindupTime = 10000;
                    }
                }

                // check for players neer object
                DynamicObjectProximityWorker(mapChannel, controlPoint, delta);
            }

            // footlocker
            foreach (var entry in mapChannel.FootLockers)
            {
                var footlocker = entry.Value;
                // spawn object
                if (!footlocker.IsInWorld)
                {
                    footlocker.RespawnTime -= delta;

                    if (footlocker.RespawnTime <= 0)
                    {
                        CellManager.Instance.AddToWorld(mapChannel, footlocker);
                        footlocker.IsInWorld = true;
                        footlocker.StateId = UseObjectState.CpointStateUnclaimed;
                        footlocker.WindupTime = 10000;
                    }
                }
            }

            // crafting stations
            KraftwerksManager.Instance.Worker(mapChannel);

            // teleporters
            foreach (var entry in mapChannel.Teleporters)
            {
                var teleporter = entry.Value;
                // spawn object
                if (!teleporter.IsInWorld)
                {
                    teleporter.RespawnTime -= delta;

                    if (teleporter.RespawnTime <= 0)
                    {
                        CellManager.Instance.AddToWorld(mapChannel, teleporter);
                        teleporter.IsInWorld = true;
                        teleporter.StateId = UseObjectState.TsState1;
                    }
                }

                // check for players neer object
                DynamicObjectProximityWorker(mapChannel, teleporter, delta);
            }

            // dynamicObjects
            foreach (var dynamicObject in mapChannel.DynamicObjects)
            {
                // spawn object
                if (!dynamicObject.IsInWorld)
                {
                    dynamicObject.RespawnTime -= delta;

                    if (dynamicObject.RespawnTime <= 0)
                    {
                        CellManager.Instance.AddToWorld(mapChannel, dynamicObject);
                        dynamicObject.IsInWorld = true;
                        dynamicObject.StateId = UseObjectState.IdStateActive;
                        dynamicObject.WindupTime = 10000;
                    }
                }
            }
        }

        internal void DynamicObjectProximityWorker(MapChannel mapChannel, DynamicObject obj, long delta)
        {
            switch (obj.DynamicObjectType)
            {
                // teleporters
                case DynamicObjectType.Waypoint:
                case DynamicObjectType.Wormhole:
                case DynamicObjectType.DropshipTeleporter:
                    {
                        // check for players that enter range
                        PlayerEnterWaypoint(obj);

                        // check for players that leave range
                        PlayerExitWaypoint(obj);

                        break;
                    }
                // Control point
                case DynamicObjectType.ControlPoint:
                default:
                    break;
            }
        }

        // 1 object to n client's
        internal void CellIntroduceDynamicObjectToClients(DynamicObject dynamicObject, List<Client> listOfClients)
        {
            foreach (var client in listOfClients)
                CreateDynamicObjectOnClient(client, dynamicObject);
        }

        // n objects to 1 client
        internal void CellIntroduceDynamicObjectsToClient(Client client, List<DynamicObject> listOfObjects)
        {
            foreach (var dynamicObject in listOfObjects)
                CreateDynamicObjectOnClient(client, dynamicObject);
        }

        internal void CreateDynamicObjectOnClient(Client client, DynamicObject dynamicObject)
        {
            if (dynamicObject == null)
                return;

            if (dynamicObject.EntityClassId == 0)
                return;
				
            var classInfo = EntityClassManager.Instance.GetClassInfo(EntityManager.Instance.GetEntityClassId(dynamicObject.EntityId));

            if (classInfo == null)
                return;

            var entityData = new List<PythonPacket>
            {
                // PhysicalEntity
                new IsTargetablePacket(classInfo.TargetFlag),
                new WorldLocationDescriptorPacket(dynamicObject.Position, dynamicObject.Rotation),
                // set state
                new UsableInfoPacket(dynamicObject.IsEnabled, dynamicObject.StateId, 0, dynamicObject.WindupTime, dynamicObject.ActivateMission)
        };

            // Only for an object that actually has a lock. An unlocked usable is the default the
            // client already assumes, and sending a lock of zeroes would tell it the same thing
            // at the cost of a packet per object per client.
            if (dynamicObject.Lock != null)
                entityData.Add(new LockInfoPacket(dynamicObject.Lock));

            client.CallMethod(SysEntity.ClientMethodId, new CreatePhysicalEntityPacket(dynamicObject.EntityId, dynamicObject.EntityClassId, entityData));
        }

        internal void CellDiscardDynamicObjectToClients(ulong entityId, List<Client> clients)
        {
            if (entityId == 0)
                return;

            foreach (var client in clients)
                EntityManager.Instance.DestroyPhysicalEntity(client, entityId, EntityType.Object);
        }

        internal void CellDiscardDynamicObjectsToClient(Client client, List<DynamicObject> discardObjects)
        {
            foreach (var dynamicObject in discardObjects)
                client.CallMethod(SysEntity.ClientMethodId, new DestroyPhysicalEntityPacket(dynamicObject.EntityId));
        }

        /* Destroys an object on client and serverside
         * Frees the memory and informs clients about removal
         */
        internal void DynamicObjectDestroy(MapChannel mapChannel, DynamicObject dynObject)
        {
            // TODO, check timers
            // remove from world
            EntityManager.Instance.UnregisterEntity(dynObject.EntityId);
            CellManager.Instance.RemoveFromWorld(mapChannel, dynObject);

            // destroy callback
            Logger.WriteLog(LogType.Debug, "ToDO remove dynamic object from server");
        }

        #region ControlPoint

        internal void InitControlPoints()
        {
            //var contolPoints = ControlPointTable.GetControlPoints();
            var mapChannel = MapChannelManager.Instance.FindByContextId(1220);

            var newControlPoint = new DynamicObject
            {
                Position = new Vector3(197.66f, 162.27f, -54.08f),
                Rotation = 3.05f,
                MapContextId = 1220,
                EntityClassId = (EntityClasses)3814,
                DynamicObjectType = DynamicObjectType.ControlPoint,
                ObjectData = new ControlPointStatus(215, 1, 1, 30000)
            };

            newControlPoint.DynamicObjectType = DynamicObjectType.ControlPoint;

            mapChannel.ControlPoints.Add(1, newControlPoint);
        }

        internal void CaptureControlPointRecovery(MapChannel mapChannel, ActionData action)
        {
            foreach (var entry in mapChannel.ControlPoints)
            {
                var controlpoint = entry.Value;

                foreach (var client in controlpoint.TriggeredByPlayers)
                    if (client.Player == action.Actor)
                    {
                        if (action.IsInrerrupted)
                        {
                            Logger.WriteLog(LogType.Debug, $"Action is interupted");
                            controlpoint.TriggeredByPlayers.Remove(client);
                            break;
                        }

                        // As with a logos: a capture belongs to whoever is still standing at the
                        // point when its ten seconds are up.
                        if (!IsInUseRange(action.Actor, controlpoint))
                        {
                            Logger.WriteLog(LogType.Security,
                                $"{client.Player.FamilyName} was no longer at control point {controlpoint.EntityId} when the use finished; not captured.");
                            controlpoint.TriggeredByPlayers.Remove(client);
                            break;
                        }

                        Logger.WriteLog(LogType.Debug, $"Action Exicuted");
                        controlpoint.TriggeredByPlayers.Remove(client);
                        controlpoint.Faction = controlpoint.Faction == Factions.AFS ? Factions.Bane : Factions.AFS;
                        controlpoint.StateId = controlpoint.StateId == UseObjectState.CpointStateFactionAOwned ? UseObjectState.CpointStateFactionBOwned : UseObjectState.CpointStateFactionAOwned;

                        CellManager.Instance.CellCallMethod(
                            mapChannel,
                            controlpoint,
                            new ForceStatePacket(controlpoint.StateId, 100));
                        CellManager.Instance.CellCallMethod(
                            mapChannel,
                            controlpoint,
                            new UsableInfoPacket(
                                true,
                                controlpoint.StateId,
                                0,
                                10000,
                                0));
                        (_missionManager ?? MissionManager.Instance).RecordProgress(
                            client,
                            MissionProgressEvent.Interaction(
                                (uint)controlpoint.EntityClassId));
                        break;
                    }
            }
        }

        #endregion

        #region Dropship
        public void DropshipsWorker(MapChannel mapChannel, long timePassed)
        {
            foreach (var entry in Dropships.ToArray())
            {
                var dropship = entry.Value;
                if (dropship.MapContextId != mapChannel.MapInfo.MapContextId)
                    continue;

                if (dropship.DropshipType != DropshipType.Spawner && dropship.DropshipType != DropshipType.Teleporter)
                {
                    Logger.WriteLog(LogType.Debug, $"error dropshiptype {dropship.DropshipType}");
                    return;
                }

                dropship.PhaseTimeleft -= timePassed;

                if (dropship.PhaseTimeleft > 0)
                    continue;

                if (dropship.Phase == 0 || dropship.Phase == 1 || dropship.Phase == 4)
                    CellManager.Instance.CellCallMethod(mapChannel, dropship, new ForceStatePacket(dropship.StateId, 0));

                switch (dropship.Phase)
                {
                    case 0:
                        dropship.Phase = 1;
                        dropship.StateId = UseObjectState.CsStateSpawn;
                        break;
                    case 1:
                        dropship.Phase = 2;
                        dropship.PhaseTimeleft = 2000;
                        break;
                    case 2:
                        dropship.Phase = 3;

                        if (dropship.DropshipType == DropshipType.Teleporter)
                        {
                            if (dropship.Client.State == ClientState.Ingame)
                            {
                                CellManager.Instance.CellCallMethod(dropship.Client.Player.MapChannel, dropship.Client.Player, new PreTeleportPacket(TeleportType.Default));
                                dropship.Client.CallMethod(SysEntity.ClientMethodId, new BeginTeleportPacket());
                            }
                        }

                        if (dropship.DropshipType == DropshipType.Spawner)
                        {
                            // create list of creatures to spawn
                            var creatureList = SpawnPoolManager.Instance.CreateListOfCreatures(dropship.SpawnPool);

                            // spawn creatures
                            try
                            {
                                SpawnPoolManager.Instance.SpawnCreatures(dropship.SpawnPool, creatureList);
                            }
                            finally
                            {
                                SpawnPoolManager.Instance.DecreaseQueuedCreatureCount(dropship.SpawnPool, creatureList.Count);
                            }
                        }

                        break;
                    case 3:
                        dropship.PhaseTimeleft = 3000;
                        dropship.Phase = 4;
                        dropship.StateId = UseObjectState.CsStateEnd;
                        break;
                    case 4:
                        dropship.Phase = 5;
                        dropship.PhaseTimeleft = 5000;

                        if (dropship.DropshipType == DropshipType.Teleporter)
                            if (dropship.Client.State == ClientState.Teleporting)
                                dropship.Client.CallMethod(SysEntity.ClientMethodId, new UnrequestMovementBlockPacket());
                        break;
                    case 5:
                        if (dropship.DropshipType == DropshipType.Teleporter)
                        {
                            switch (dropship.Client.State)
                            {
                                case ClientState.Ingame:
                                    DepartDropship(dropship.Client, dropship);
                                    break;
                                case ClientState.Teleporting:
                                    if (dropship.Client.PendingTransfer == null &&
                                        dropship.Client.Player.MapContextId == dropship.MapContextId)
                                    {
                                        dropship.Client.State = ClientState.Ingame;
                                        ManifestationManager.Instance.ResetInactivity(dropship.Client);
                                    }
                                    break;
                                default:
                                    Logger.WriteLog(LogType.Error, $"Unsupported CLientState {dropship.Client.State}");
                                    break;
                            }
                        }

                        if (dropship.DropshipType == DropshipType.Spawner)
                            SpawnPoolManager.Instance.DecreaseQueueCount(dropship.SpawnPool);

                        // remove object
                        CellManager.Instance.RemoveFromWorld(mapChannel, dropship);

                        Dropships.Remove(dropship.EntityId);
                        break;
                    default:
                        Logger.WriteLog(LogType.Error, $"Unsupported phase {dropship.Phase}");
                        break;
                }
            }
        }
        #endregion

        #region Footlocker

        internal void FootlockerRecovery(MapChannel mapChannel, ActionData action)
        {
            Logger.WriteLog(LogType.Debug, $"ToDo: FootlockerRecovery, ActionId = {action.ActionId} ActionArgId = {action.ActionArgId}");
        }

        internal void InitFootlockers()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            var footlockers = unitOfWork.Footlockers.GetFootlockers();

            foreach (var footlocker in footlockers)
            {
                var mapChannel = MapChannelManager.Instance.FindByContextId(footlocker.MapContextId);

                var newFootlocker = new DynamicObject
                {
                    Position = footlocker.Position,
                    Rotation = footlocker.Rotation,
                    MapContextId = footlocker.MapContextId,
                    EntityClassId = (EntityClasses)footlocker.ClassId,
                    DynamicObjectType = DynamicObjectType.Lockbox,
                    Comment = footlocker.Comment
                };

                mapChannel.FootLockers.Add(footlocker.Id, newFootlocker);
            }
        }

        #endregion

        #region Logos
        internal void LogosRecovery(MapChannel mapChannel, ActionData action)
        {
            foreach (var obj in mapChannel.DynamicObjects)
            {
                foreach (var client in obj.TriggeredByPlayers)
                    if (client.Player == action.Actor)
                    {
                        if (action.IsInrerrupted)
                        {
                            Logger.WriteLog(LogType.Debug, $"Action is interupted");
                            obj.TriggeredByPlayers.Remove(client);
                            //CellManager.Instance.CellCallMethod(mapChannel, action.Actor, new PerformWindupPacket(PerformType.TwoArgs, action.ActionId, action.ActionArgId));
                            break;
                        }

                        // Still at it when the ten seconds are up, not only when they started.
                        // The client interrupts a use the moment the player moves (useobject.py
                        // sets moveInterrupts), so the only client this refuses is one that did
                        // not - and a tablet is a permanent thing to be given for a use that was
                        // walked away from.
                        if (!IsInUseRange(action.Actor, obj))
                        {
                            Logger.WriteLog(LogType.Security,
                                $"{client.Player.FamilyName} was no longer at logos object {obj.EntityId} when the use finished; nothing given.");
                            obj.TriggeredByPlayers.Remove(client);
                            break;
                        }

                        Logger.WriteLog(LogType.Debug, $"Action Exicuted");
                        obj.TriggeredByPlayers.Remove(client);
                        CellManager.Instance.CellCallMethod(obj, new UsableInfoPacket(true, obj.StateId, 0, 10000, 0));

                        var logosId = 0u;
                        foreach (var entry in mapChannel.DynamicObjects)
                        {
                            var logos = entry as Logos;
                            if (action.SourceId == logos.EntityId)
                            {
                                logosId = logos.Id;
                                break;
                            }
                        }

                        var haveLogos = false;
                        foreach (var logos in client.Player.Logos)
                        {
                            if (logos == logosId)
                            {
                                haveLogos = true;
                                break;
                            }
                        }

                        if (!haveLogos)
                            CharacterManager.Instance.UpdateCharacter(client, CharacterUpdate.Logos, logosId);
                        (_missionManager ?? MissionManager.Instance).RecordProgress(
                            client,
                            MissionProgressEvent.Interaction((uint)obj.EntityClassId));

                        break;
                    }
            }
        }
        #endregion

        #region Waypoint

        internal void InitTeleporters()
        {
            using var unitOfWork = _gameUnitOfWorkFactory.CreateWorld();
            var teleporters = unitOfWork.Teleporters.GetTeleporters();

            foreach (var teleporter in teleporters)
            {
                if (teleporter.MapContextId == 0)
                    continue;

                var mapChannel = MapChannelManager.Instance.FindByContextId(teleporter.MapContextId);

                var newTeleporter = new DynamicObject
                {
                    Position = teleporter.Position,
                    Rotation = teleporter.Rotation,
                    MapContextId = teleporter.MapContextId,
                    EntityClassId = (EntityClasses)teleporter.ClassId,
                    Comment = teleporter.Description,
                    ObjectData = new WaypointInfo(teleporter.Id, false, (WaypointType)teleporter.Type)
                };

                switch (teleporter.Type)
                {
                    case 1:
                        newTeleporter.DynamicObjectType = DynamicObjectType.LocalTeleporter;
                        break;
                    case 2:
                        newTeleporter.DynamicObjectType = DynamicObjectType.Waypoint;
                        break;
                    case 3:
                        newTeleporter.DynamicObjectType = DynamicObjectType.Wormhole;
                        break;
                    case 4:
                        CellManager.Instance.AddToWorld(mapChannel, new MapTrigger(teleporter.Id, teleporter.Description, teleporter.Position, teleporter.Rotation, teleporter.MapContextId));
                        break;
                    case 5:
                        break;
                    default:
                        Logger.WriteLog(LogType.Error, $"InitTeleporters: unsuported teleporter type {teleporter.Type}");

                        MapErrorManager.Instance.Record(teleporter.MapContextId,
                            $"Teleporter {teleporter.Id} ({teleporter.Description}) is type {teleporter.Type}, which nothing handles.");

                        break;
                }

                mapChannel.Teleporters.Add(teleporter.Id, newTeleporter);
                Teleporters.Add(teleporter.Id, newTeleporter);
            }
        }

        internal void CheckPlayerWaypoint(Client client, WaypointInfo objectData)
        {
            lock (client.SyncRoot)
            {
                foreach (var waypoint in client.Player.GainedWaypoints)
                    if (waypoint.WaypointId == objectData.WaypointId)
                        return;

                var newWaypoint = new CharacterTeleporterEntry(
                    client.Player.Id,
                    objectData.WaypointId,
                    (byte)objectData.WaypointType);
                _updateCharacter(client, CharacterUpdate.Teleporter, newWaypoint);
                client.Player.GainedWaypoints.Add(newWaypoint);
                client.CallMethod(
                    client.Player.EntityId,
                    new WaypointGainedPacket(
                        objectData.WaypointId,
                        objectData.WaypointType));
                MapMarkerManager.Instance.WaypointDiscovered(
                    client, objectData.WaypointId);
                (_missionManager ?? MissionManager.Instance).RecordProgress(
                    client,
                    MissionProgressEvent.Waypoint(objectData.WaypointId));
            }
        }

        internal Dictionary<uint, MapWaypointInfoList> CreateListOfWaypoints(Client client, WaypointType waypointType)
        {
            var listOfWaypoints = new Dictionary<uint, MapWaypointInfoList>();
            var listOfMapInstances = new List<MapInstanceInfo>();
            var waypointInfo = new List<WaypointInfo>();
            var mapChannel = client.Player.MapChannel;

            // create waypoint list for player
            foreach (var waypoint in client.Player.GainedWaypoints)
            {
                if ((WaypointType)waypoint.WaypointType != waypointType)
                    continue;

                if (!Teleporters.TryGetValue(waypoint.WaypointId, out var teleporter))
                {
                    Logger.WriteLog(LogType.Error, $"Discovered waypoint {waypoint.WaypointId} has no world definition.");
                    continue;
                }
                var teleporterData = teleporter.ObjectData as WaypointInfo;
                if (teleporterData == null || teleporterData.Contested)
                    continue;

                if (teleporter.MapContextId != mapChannel.MapInfo.MapContextId)
                    continue;

                if (teleporterData.WaypointType != waypointType)
                    continue;

                if (waypoint.WaypointId == teleporterData.WaypointId)
                {
                    waypointInfo.Add(new WaypointInfo(teleporterData.WaypointId, teleporterData.Contested, teleporterData.WaypointType)
                    {
                        Position = teleporter.Position
                    });

                }
            }
            listOfMapInstances.Add(new MapInstanceInfo(mapChannel.InstanceId, mapChannel.MapInfo.MapContextId, MapInstanceStatus.Low));

            listOfWaypoints.Add(mapChannel.MapInfo.MapContextId, new MapWaypointInfoList(mapChannel.MapInfo.MapContextId, listOfMapInstances, waypointInfo));

            return listOfWaypoints;
        }

        internal void SelectWaypoint(Client client, SelectWaypointPacket packet)
        {
            lock (client.SyncRoot)
            {
                if (client.PendingTransfer != null)
                {
                    Logger.WriteLog(LogType.Network, "Ignored a duplicate waypoint selection during transfer.");
                    return;
                }
                if (client.State != ClientState.Ingame || client.Player?.MapChannel == null || client.Player.Id == 0 ||
                    client.Player.Disconected || client.Player.RemoveFromMap || client.Player.LogoutActive ||
                    !CellManager.Instance.IsInWorld(client) ||
                    !Teleporters.TryGetValue(packet.WaypointId, out var teleporter) ||
                    teleporter.ObjectData is not WaypointInfo info ||
                    !Maps.MapChannelArray.TryGetValue(teleporter.MapContextId, out var destinationMap))
                {
                    RejectTravel(client, "Invalid waypoint or player state.");
                    return;
                }

                var origin = client.Player.MapChannel;
                var isDropship = info.WaypointType == WaypointType.Dropship;
                if ((packet.MapInstanceId != 0 && packet.MapInstanceId != destinationMap.InstanceId) ||
                    info.Contested ||
                    !client.Player.GainedWaypoints.Any(waypoint => waypoint.WaypointId == packet.WaypointId &&
                        waypoint.WaypointType == (byte)info.WaypointType) ||
                    (!isDropship && info.WaypointType != WaypointType.Waypoint && info.WaypointType != WaypointType.LocalTeleporter) ||
                    (!isDropship && destinationMap != origin))
                {
                    RejectTravel(client, "Waypoint is not discovered, available, or in the selected instance.");
                    return;
                }

                var nearbySource = Teleporters.Values.Any(source =>
                    source.MapContextId == origin.MapInfo.MapContextId &&
                    source.ObjectData is WaypointInfo sourceInfo && sourceInfo.WaypointType == info.WaypointType &&
                    (isDropship ? client.Player.IsNear5m(source) : client.Player.IsNear2m(source)));
                var destination = isDropship ? teleporter.Position : teleporter.Position + new Vector3(0, 1, 0);
                if (!nearbySource || !CellManager.TryGetCellCoordinates(destination, out _, out _) ||
                    !double.IsFinite(teleporter.Rotation) || !float.IsFinite((float)teleporter.Rotation))
                {
                    RejectTravel(client, "No nearby departure station or invalid destination position.");
                    return;
                }

                var timeout = client.Server?.Config.GameConfig.TransferTimeoutSeconds ??
                    Config.GameConfig.DefaultTransferTimeoutSeconds;
                if (timeout <= 0)
                {
                    Logger.WriteLog(LogType.Error, "TransferTimeoutSeconds must be positive.");
                    RejectTravel(client, "Travel timeout configuration is invalid.");
                    return;
                }

                var transfer = new PlayerTransfer
                {
                    OriginMap = origin,
                    OriginPosition = client.Player.Position,
                    OriginRotation = client.Player.Rotation,
                    DestinationMap = destinationMap,
                    DestinationPosition = destination,
                    DestinationRotation = teleporter.Rotation,
                    Deadline = checked(_clock() + timeout * 1000L),
                    IsDropship = isDropship
                };
                client.PendingTransfer = transfer;
                client.CallMethod(SysEntity.ClientMethodId, new RequestMovementBlockPacket());

                if (isDropship)
                {
                    var dropship = new Dropship(Factions.AFS, DropshipType.Teleporter, client,
                        destination, destinationMap.MapInfo.MapContextId);
                    transfer.DropshipId = dropship.EntityId;
                    CellManager.Instance.AddToWorld(origin, dropship);
                    Dropships.Add(dropship.EntityId, dropship);
                    client.LoadingMap = destinationMap.MapInfo.MapContextId;
                    return;
                }

                client.CellCallMethod(client, client.Player.EntityId, new PreTeleportPacket(TeleportType.Default));
                client.State = ClientState.Teleporting;
                client.SetWorldPosition(destination, teleporter.Rotation);
                CellManager.Instance.UpdateVisibility(client);
                client.CallMethod(client.Player.EntityId,
                    new TeleportPacket(destination, teleporter.Rotation, TeleportType.Default, 5));
                client.CallMethod(SysEntity.ClientMethodId, new BeginTeleportPacket());
                client.CellMoveObject(client, new MoveObjectMessage(client.Player.EntityId, client.Movement), false);
            }
        }

        /// <summary>
        /// Whether the player currently has a waypoint window open on the server's side: the
        /// proximity workers add a client to a teleporter's TriggeredByPlayers or a dropship
        /// pad's TriggeredBy while it is within range, and take it out again when it leaves.
        /// </summary>
        private static bool IsAtWaypoint(Client client)
        {
            var mapChannel = client.Player.MapChannel;

            if (mapChannel == null)
                return false;

            foreach (var teleporter in mapChannel.Teleporters.Values)
                if (teleporter.TriggeredByPlayers.Contains(client))
                    return true;

            if (mapChannel.MapCellInfo.Cells.TryGetValue(client.Player.Cells[2, 2], out var cell))
                foreach (var trigger in cell.MapTriggers)
                    if (trigger.TriggeredBy.Contains(client))
                        return true;

            return false;
        }

        internal void TeleportAcknowledge(Client client)
        {
            lock (client.SyncRoot)
            {
                if (client.State != ClientState.Teleporting || client.PendingTransfer == null ||
                    client.PendingTransfer.IsDropship)
                {
                    Logger.WriteLog(LogType.Network, "Ignored an unexpected teleport acknowledgement.");
                    return;
                }
                if (CheckTransferTimeout(client) || !PersistTransfer(client))
                    return;

                client.PendingTransfer = null;
                client.State = ClientState.Ingame;
                client.CallMethod(client.Player.EntityId, new TeleportArrivalPacket());
                client.CallMethod(SysEntity.ClientMethodId, new UnrequestMovementBlockPacket());
            }
        }

        private static void RejectTravel(Client client, string reason)
        {
            Logger.WriteLog(LogType.Network, $"Rejected travel: {reason}");
            if (client.Player != null && client.State != ClientState.Disconnected)
                client.CallMethod(client.Player.EntityId, new TeleportFailedPacket());
        }

        internal bool CheckTransferTimeout(Client client)
        {
            lock (client.SyncRoot)
            {
                var transfer = client.PendingTransfer;
                if (transfer == null || _clock() < transfer.Deadline)
                    return false;

                Logger.WriteLog(LogType.Network,
                    $"Transfer timed out for entity {client.Player.EntityId}; restoring its origin.");
                if (transfer.HasDeparted && CellManager.Instance.IsInWorld(client))
                    CellManager.Instance.RemoveFromWorld(client);
                client.RestoreTransferOrigin();
                CleanupClientDropships(client);
                _disconnect(client);
                return true;
            }
        }

        private void DepartDropship(Client client, Dropship dropship)
        {
            lock (client.SyncRoot)
            {
                var transfer = client.PendingTransfer;
                if (transfer == null || !transfer.IsDropship || transfer.HasDeparted ||
                    transfer.DropshipId != dropship.EntityId || CheckTransferTimeout(client))
                    return;

                CommunicatorManager.Instance.LeaveMapChannels(client);
                LootDispenserManager.Instance.RemoveForOwner(transfer.OriginMap, client);
                CellManager.Instance.RemoveFromWorld(client);
                transfer.OriginMap.ClientList.RemoveAll(member => member == client);
                transfer.HasDeparted = true;
                client.Player.MapChannel = transfer.DestinationMap;
                client.Player.MapContextId = transfer.DestinationMap.MapInfo.MapContextId;
                client.SetWorldPosition(transfer.DestinationPosition, transfer.DestinationRotation);
                client.LoadingMap = transfer.DestinationMap.MapInfo.MapContextId;
                client.State = ClientState.Teleporting;
                client.CallMethod(SysEntity.ClientMethodId, new UnrequestMovementBlockPacket());
                client.CallMethod(SysEntity.ClientMethodId, new PreWonkavatePacket());
                client.CallMethod(SysEntity.CurrentInputStateId, new WonkavatePacket(
                    transfer.DestinationMap.MapInfo.MapContextId, transfer.DestinationMap.InstanceId,
                    transfer.DestinationMap.MapInfo.MapVersion, transfer.DestinationPosition,
                    (float)transfer.DestinationRotation));
                client.AwaitingMapLoaded = true;
            }
        }

        internal bool IsExpectedMapLoad(Client client)
        {
            var transfer = client.PendingTransfer;
            return client.State == ClientState.Teleporting &&
                transfer?.HasDeparted == true && client.LoadingMap == transfer.DestinationMap.MapInfo.MapContextId &&
                client.Player.MapChannel == transfer.DestinationMap;
        }

        internal bool CompleteMapLoadTransfer(Client client)
        {
            lock (client.SyncRoot)
            {
                if (!IsExpectedMapLoad(client))
                    throw new InvalidOperationException("No matching map transfer to complete.");
                if (!PersistTransfer(client))
                    return false;
                client.PendingTransfer = null;
                return true;
            }
        }

        private bool PersistTransfer(Client client)
        {
            try
            {
                _updateCharacter(client, CharacterUpdate.Position, null);
                return true;
            }
            catch (Exception error) when (error is DbUpdateException || error is DbException)
            {
                Logger.WriteLog(LogType.Error, $"Unable to persist player transfer: {error.Message}");
                client.RestoreTransferOrigin();
                CleanupClientDropships(client);
                _disconnect(client);
                return false;
            }
        }

        internal void CleanupClientDropships(Client client)
        {
            foreach (var dropship in Dropships.Values.Where(ship => ship.Client == client).ToArray())
            {
                if (Maps.MapChannelArray.TryGetValue(dropship.MapContextId, out var map))
                    CellManager.Instance.RemoveFromWorld(map, dropship);
                Dropships.Remove(dropship.EntityId);
            }
        }

        internal void PlayerEnterWaypoint(DynamicObject obj)
        {
            var mapChannel = Maps.FindByContextId(obj.MapContextId);
            if (!CellManager.TryGetCellCoordinates(obj.Position, out var x, out var z))
            {
                Logger.WriteLog(LogType.Error, $"Invalid waypoint position for {obj.EntityId}.");
                return;
            }
            var cells = CellManager.Instance.CreateCellMatrix(mapChannel, x, z);

            foreach (var client in CellManager.Instance.GetClientsInCells(mapChannel, cells))
            {
                if (client.State != ClientState.Ingame || client.PendingTransfer != null)
                    continue;
                // check if player is near waypoint
                if (!client.Player.IsNear2m(obj))
                {
                    continue;
                }

                // check if already added
                if (obj.TriggeredByPlayers.Any(p => p == client))
                {
                    continue;
                }

                // if not add him and send enter packet
                obj.TriggeredByPlayers.Add(client);

                var objectData = (WaypointInfo)obj.ObjectData;

                CheckPlayerWaypoint(client, objectData);

                var waypointInfoList = CreateListOfWaypoints(client, objectData.WaypointType);

                client.CallMethod(SysEntity.ClientMethodId,
                    new EnteredWaypointPacket(mapChannel.InstanceId, obj.MapContextId,
                        waypointInfoList, objectData.WaypointType, objectData.WaypointId));

                // check if we already added him to the waypoint
            }
        }

        internal void PlayerExitWaypoint(DynamicObject obj)
        {
            for (var i = obj.TriggeredByPlayers.Count - 1; i >= 0; i--)
            {
                var client = obj.TriggeredByPlayers[i];

                if (client.State != ClientState.Ingame ||
                    client.Player?.MapChannel?.MapInfo.MapContextId != obj.MapContextId ||
                    !client.Player.IsNear2m(obj))
                {
                    obj.TriggeredByPlayers.RemoveAt(i);

                    if (client.State != ClientState.Disconnected)
                        client.CallMethod(SysEntity.ClientMethodId, new ExitedWaypointPacket());
                }
            }
        }

        internal Dictionary<uint, MapWaypointInfoList> CreateListOfDropships(Client client)

        {
            var dropships = new Dictionary<uint, MapWaypointInfoList>();

            foreach (var entry in Teleporters)
            {
                var teleporter = entry.Value;
                var teleporterInfo = teleporter.ObjectData as WaypointInfo;

                if (teleporterInfo?.WaypointType == WaypointType.Dropship && !teleporterInfo.Contested &&
                    client.Player.GainedWaypoints.Any(known => known.WaypointId == teleporterInfo.WaypointId &&
                        known.WaypointType == (byte)WaypointType.Dropship) &&
                    Maps.MapChannelArray.TryGetValue(teleporter.MapContextId, out var channel))
                {
                    if (dropships.ContainsKey(teleporter.MapContextId))
                    {
                        var map = dropships[teleporter.MapContextId];
                        var waypoints = map.Waypoints;

                        waypoints.Add(new WaypointInfo(teleporterInfo.WaypointId, teleporterInfo.Contested, teleporter.Position, teleporterInfo.WaypointType));
                    }
                    else
                    {
                        //create new entry
                        var instance = new List<MapInstanceInfo>
                            { new MapInstanceInfo(channel.InstanceId, teleporter.MapContextId, MapInstanceStatus.Low) };
                        var waypoints = new List<WaypointInfo>
                            { new WaypointInfo(teleporterInfo.WaypointId, teleporterInfo.Contested, teleporter.Position, WaypointType.Dropship) };

                        var mapWaypointInfoList = new MapWaypointInfoList(teleporter.MapContextId, instance, waypoints);

                        dropships.Add(teleporter.MapContextId, mapWaypointInfoList);
                    }
                }
            }

            return dropships;
        }
        #endregion
    }
}
