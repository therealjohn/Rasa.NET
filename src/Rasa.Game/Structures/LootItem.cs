using Rasa.Managers;

namespace Rasa.Structures
{
    public class LootItem
    {
        public LootItem()
        {
            EntityId = EntityManager.Instance.GetEntityId;
        }
        public ulong EntityId { get; set; }
        public uint ItemTemplateId { get; set; }
        public uint ItemClassId { get; set; }
        public uint ItemQuantity { get; set; }
        public ulong ActorId { get; set; }
        public uint PartyId { get; set; }

        internal LootItem(LootItem source)
        {
            EntityId = source.EntityId;
            ItemTemplateId = source.ItemTemplateId;
            ItemClassId = source.ItemClassId;
            ItemQuantity = source.ItemQuantity;
            ActorId = source.ActorId;
            PartyId = source.PartyId;
        }

        public LootItem(uint itemTemplateId, uint itemClassId, uint itemQuantity, ulong actorId, uint partyId)
        {
            EntityId = EntityManager.Instance.GetEntityId;
            ItemTemplateId = itemTemplateId;
            ItemClassId = itemClassId;
            ItemQuantity = itemQuantity;
            ActorId = actorId;
            PartyId = partyId;
        }
    }
}
