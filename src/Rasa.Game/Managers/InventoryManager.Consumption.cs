using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.Game.Server;
    using Packets.Inventory.Server;
    using Packets.MapChannel.Server;
    using Repositories.Char;
    using Structures;

    public partial class InventoryManager
    {
        internal sealed class InventoryConsumption
        {
            private readonly List<(Item Item, uint Remaining, uint Consumed)> _changes = new();
            private bool _published;
            internal IEnumerable<MissionProgressEvent> ProgressEvents => _changes.Select(change =>
                MissionProgressEvent.ItemConsumed((uint)change.Item.ItemTemplate.Class, change.Consumed));

            internal void PlanAndSave(Client client, IReadOnlyDictionary<ulong, uint> quantities, ICharUnitOfWork unit)
            {
                if (unit.Characters.Get(client.Player.Id).AccountId != client.AccountEntry.Id)
                    throw new GameplayRejectionException("Consumable character ownership changed.");
                var inventory = client.Player.Inventory.PersonalInventory;
                var rows = unit.CharacterInventories.GetItems(client.AccountEntry.Id);
                foreach (var entry in quantities)
                {
                    var item = EntityManager.Instance.GetItem(entry.Key);
                    var slot = inventory.IndexOf(entry.Key);
                    var owners = item == null ? null : rows.Where(row => row.ItemId == item.Id).ToArray();
                    if (entry.Value == 0 || item?.ItemTemplate == null || item.Id == 0 || slot < 0 ||
                        EntityManager.Instance.GetEntityType(entry.Key) != EntityType.Item ||
                        item.OwnerId != client.Player.Id || item.OwnerSlotId != slot ||
                        owners.Length != 1 || owners[0].CharacterId != client.Player.Id ||
                        owners[0].InventoryType != (uint)InventoryType.Personal || owners[0].SlotId != slot ||
                        rows.Count(row => row.CharacterId == client.Player.Id &&
                            row.InventoryType == (uint)InventoryType.Personal && row.SlotId == slot) != 1 ||
                        item.StackSize < entry.Value)
                        throw new GameplayRejectionException("Consumable ownership, slot or quantity changed.");
                    var saved = unit.Items.GetItem(item.Id);
                    if (saved?.ItemTemplateId != item.ItemTemplate.ItemTemplateId || saved.StackSize != item.StackSize)
                        throw new GameplayRejectionException("Consumable inventory no longer matches its saved stack.");
                    _changes.Add((item, item.StackSize - entry.Value, entry.Value));
                }
                foreach (var change in _changes)
                {
                    if (change.Remaining == 0)
                    {
                        unit.CharacterInventories.DeleteInvItemByItemId(change.Item.Id);
                        unit.Items.DeleteItem(change.Item.Id);
                    }
                    else
                        unit.Items.UpdateItemStackSize(new Item(0, change.Remaining, 0, 0) { Id = change.Item.Id });
                }
            }

            internal void Publish(Client client)
            {
                if (_published)
                    return;
                _published = true;
                foreach (var change in _changes)
                {
                    change.Item.StackSize = change.Remaining;
                    if (change.Remaining == 0)
                    {
                        client.Player.Inventory.PersonalInventory[(int)change.Item.OwnerSlotId] = 0;
                        EntityManager.Instance.ReleaseEntity(change.Item.EntityId, EntityType.Item);
                        MissionApplication.TryPublish(() => client.CallMethod(SysEntity.ClientInventoryManagerId,
                            new InventoryRemoveItemPacket(InventoryType.Personal, change.Item.EntityId)),
                            $"consumed item {change.Item.Id} inventory removal");
                        MissionApplication.TryPublish(() => client.CallMethod(SysEntity.ClientMethodId,
                            new DestroyPhysicalEntityPacket(change.Item.EntityId)), $"consumed item {change.Item.Id} removal");
                    }
                    else
                        MissionApplication.TryPublish(() => client.CallMethod(change.Item.EntityId,
                            new SetStackCountPacket(change.Remaining)), $"consumed item {change.Item.Id} count");
                }
            }
        }
    }
}
