using System.Collections.Generic;
using System.Numerics;

namespace Rasa.Structures
{
    using Data;
    using Managers;
    using Interfaces;

    public class Actor : IHasPosition
    {
        public ulong EntityId = EntityManager.Instance.GetEntityId;
        public Vector3 Position { get; set; }
        public double Rotation { get; set; }
        public bool IsCrouching { get; set; }
        public EntityClasses EntityClass { get; set; }
        public string Name { get; set; }
        public string FamilyName { get; set; }
        private uint _mapContextId;
        private CharacterState _state;
        private long _abilityLifetime;
        private long _actionLifetime;
        internal long AbilityLifetime => System.Threading.Interlocked.Read(ref _abilityLifetime);
        internal long ActionLifetime => System.Threading.Interlocked.Read(ref _actionLifetime);
        internal void InvalidateActionLifetime() => System.Threading.Interlocked.Increment(ref _actionLifetime);

        internal void InvalidateAbilityLifetime()
        {
            System.Threading.Interlocked.Increment(ref _abilityLifetime);
            InvalidateActionLifetime();
        }

        public uint MapContextId
        {
            get => _mapContextId;
            set
            {
                if (_mapContextId != value)
                    InvalidateAbilityLifetime();
                _mapContextId = value;
            }
        }
        public bool IsRunning { get; set; }
        public bool InCombatMode { get; set; }
        public CharacterState State
        {
            get => _state;
            set
            {
                if (_state != value && (value == CharacterState.Dead || value == CharacterState.Dying))
                    InvalidateAbilityLifetime();
                _state = value;
            }
        }
        public ulong Target { get; set; }
        public double MovementSpeed { get; set; }
        public bool WeaponReady { get; set; }
        // action data
        public int CurrentAction { get; set; }
        public Dictionary<Attributes, ActorAttributes> Attributes = new Dictionary<Attributes, ActorAttributes>();
        public Dictionary<int, GameEffect> ActiveEffects { get; set; } = new Dictionary<int, GameEffect>();
        public uint[,] Cells = new uint[5 ,5];
        // sometimes we only have access to the actor, the owner variable allows us to access the client anyway (only if actor is a player manifestation)
        //public MapChannelClient Owner { get; set; }
    }
}
