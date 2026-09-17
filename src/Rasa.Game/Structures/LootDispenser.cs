using System.Collections.Generic;
using System.Linq;

namespace Rasa.Structures
{
    using Data;
    using Managers;
    public class LootDispenser
    {
        public LootDispenser()
        {
            // Requests carry only this ID, so delayed claims must never target a new corpse.
            EntityId = EntityManager.Instance.AllocateUnrecycledEntityId();
            EntityClassId = (Data.EntityClasses)10000035;
        }

        public ulong EntityId { get; set; }
        public Data.EntityClasses EntityClassId { get; set; }
        public List<LootItem> LootItems = new List<LootItem>();
        public int Credits { get; set; }
        public ulong Owner { get; set; }
        public ulong AttachedTo { get; set; }
        public bool FullyLooted { get; set; }
        public bool IsLootable { get; set; }
        public LootQuality LootQuality { get; set; }
        internal Game.Client OwnerClient { get; set; }
        internal Manifestation Player { get; set; }
        internal MapChannel Map { get; set; }
        internal Creature Corpse { get; set; }
        internal long PlayerLifetime { get; set; }
        internal long CorpseLifetime { get; set; }
        internal uint CharacterId { get; set; }
        internal uint AccountId { get; set; }

        internal LootDispenser(LootDispenser source)
        {
            EntityId = source.EntityId;
            EntityClassId = source.EntityClassId;
            AttachedTo = source.AttachedTo;
            Owner = source.Owner;
            Credits = source.Credits;
            IsLootable = source.IsLootable;
            FullyLooted = source.FullyLooted;
            LootQuality = source.LootQuality;
            LootItems = source.LootItems.Select(item => new LootItem(item)).ToList();
        }
    }
}
