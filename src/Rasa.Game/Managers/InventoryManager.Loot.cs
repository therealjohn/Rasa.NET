using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Managers
{
    using Data;
    using Game;
    using Packets.Inventory.Server;
    using Packets.MapChannel.Server;
    using Repositories.Char;
    using Structures;

    public partial class InventoryManager
    {
        internal sealed class LootGrant
        {
            private sealed class ExistingStack
            {
                internal Item Item;
                internal uint Original;
                internal uint Final;
            }

            private sealed class SourcePlan
            {
                internal LootItem Loot;
                internal Item Item;
                internal uint Remaining;
                internal int? Slot;
            }

            private readonly Dictionary<ulong, ExistingStack> _existing = new();
            private readonly Dictionary<ulong, SourcePlan> _plannedSources = new();
            private readonly List<SourcePlan> _sources = new();

            internal void PlanAndSave(
                Client client,
                IReadOnlyList<LootItem> loot,
                ICharUnitOfWork unitOfWork,
                uint? preferredSlot = null)
            {
                var inventory = client.Player.Inventory.PersonalInventory;

                if (inventory.Count != 250)
                    throw new GameplayRejectionException("Personal inventory is not initialized.");

                var rows = unitOfWork.CharacterInventories.GetItems(client.AccountEntry.Id);
                var personalRows = rows.Where(row =>
                    row.CharacterId == client.Player.Id &&
                    row.InventoryType == (uint)InventoryType.Personal).ToArray();

                if (personalRows.Any(row => row.SlotId >= inventory.Count))
                    throw new GameplayRejectionException(
                        "Persisted personal inventory has an out-of-range slot.");

                if (personalRows.Select(row => row.SlotId).Distinct().Count() != personalRows.Length)
                    throw new GameplayRejectionException("Persisted personal inventory has duplicate slots.");

                var durableBySlot = personalRows.ToDictionary(row => row.SlotId);
                var planned = new Item[250];
                var counts = new uint[250];

                for (var slot = 0; slot < inventory.Count; slot++)
                {
                    var entityId = inventory[slot];

                    if (entityId == 0)
                    {
                        if (durableBySlot.ContainsKey((uint)slot))
                            throw new GameplayRejectionException("Runtime and durable inventory slots differ.");
                        continue;
                    }

                    var item = EntityManager.Instance.GetItem(entityId);
                    if (item?.ItemTemplate == null || item.Id == 0 ||
                        item.OwnerId != client.Player.Id ||
                        !durableBySlot.TryGetValue((uint)slot, out var row) || row.ItemId != item.Id)
                        throw new GameplayRejectionException("Runtime inventory identity is stale.");

                    var saved = unitOfWork.Items.GetItem(item.Id);
                    var maximum = MaximumStack(item);
                    if (saved == null || saved.StackSize != item.StackSize ||
                        item.StackSize == 0 || item.StackSize > maximum ||
                        CategoryStart(item) != slot / 50 * 50)
                        throw new GameplayRejectionException("Runtime inventory stack is stale.");

                    planned[slot] = item;
                    counts[slot] = item.StackSize;
                    _existing[item.EntityId] = new ExistingStack
                    {
                        Item = item,
                        Original = item.StackSize,
                        Final = item.StackSize
                    };
                }

                var seen = new HashSet<ulong>();
                foreach (var entry in loot)
                {
                    var item = entry?.Item;
                    if (item?.ItemTemplate == null || item.Id == 0 || item.StackSize == 0 ||
                        entry.Taken || !seen.Add(entry.EntityId) || entry.EntityId != item.EntityId ||
                        entry.ActorId != client.Player.EntityId || entry.PartyId != 0 ||
                        EntityManager.Instance.GetEntityType(item.EntityId) != EntityType.Item ||
                        !ReferenceEquals(EntityManager.Instance.GetItem(item.EntityId), item) ||
                        entry.ItemTemplateId != item.ItemTemplate.ItemTemplateId ||
                        entry.ItemClassId != (uint)item.ItemTemplate.Class ||
                        entry.ItemQuantity != item.StackSize ||
                        rows.Any(row => row.ItemId == item.Id))
                        throw new GameplayRejectionException("Loot item identity or ownership is invalid.");

                    var saved = unitOfWork.Items.GetItem(item.Id);
                    if (saved == null || saved.ItemTemplateId != item.ItemTemplate.ItemTemplateId ||
                        saved.StackSize != item.StackSize)
                        throw new GameplayRejectionException("Loot item durable state is stale.");

                    var maximum = MaximumStack(item);
                    var start = CategoryStart(item);
                    var remaining = item.StackSize;

                    for (var slot = start; slot < start + 50 && remaining > 0; slot++)
                    {
                        var existing = planned[slot];
                        if (existing?.ItemTemplate.ItemTemplateId != item.ItemTemplate.ItemTemplateId)
                            continue;

                        var room = maximum - counts[slot];
                        var moved = Math.Min(room, remaining);
                        counts[slot] += moved;
                        remaining -= moved;

                        if (_existing.TryGetValue(existing.EntityId, out var existingStack))
                            existingStack.Final = counts[slot];
                        else if (_plannedSources.TryGetValue(existing.EntityId, out var plannedSource))
                            plannedSource.Remaining = counts[slot];
                        else
                            throw new GameplayRejectionException(
                                "Planned loot stack identity is missing.");
                    }

                    int? destination = null;
                    if (remaining > 0)
                    {
                        if (preferredSlot.HasValue &&
                            preferredSlot.Value >= start && preferredSlot.Value < start + 50 &&
                            planned[(int)preferredSlot.Value] == null)
                            destination = (int)preferredSlot.Value;
                        else
                            for (var slot = start; slot < start + 50; slot++)
                                if (planned[slot] == null)
                                {
                                    destination = slot;
                                    break;
                                }

                        if (!destination.HasValue || remaining > maximum)
                            throw new GameplayRejectionException(
                                "The complete loot batch does not fit its inventory category.");

                        planned[destination.Value] = item;
                        counts[destination.Value] = remaining;
                    }

                    var source = new SourcePlan
                    {
                        Loot = entry,
                        Item = item,
                        Remaining = remaining,
                        Slot = destination
                    };
                    _sources.Add(source);
                    if (destination.HasValue)
                        _plannedSources[item.EntityId] = source;
                }

                foreach (var stack in _existing.Values.Where(stack => stack.Final != stack.Original))
                    unitOfWork.Items.UpdateItemStackSize(new Item(0, stack.Final, 0, 0)
                    {
                        Id = stack.Item.Id
                    });

                foreach (var source in _sources)
                {
                    if (source.Remaining == 0)
                    {
                        unitOfWork.Items.DeleteItem(source.Item.Id);
                        continue;
                    }

                    if (source.Remaining != source.Item.StackSize)
                        unitOfWork.Items.UpdateItemStackSize(new Item(0, source.Remaining, 0, 0)
                        {
                            Id = source.Item.Id
                        });

                    unitOfWork.CharacterInventories.AddInvItem(
                        client.AccountEntry.Id,
                        client.Player.Id,
                        (uint)InventoryType.Personal,
                        (uint)source.Slot.Value,
                        source.Item.Id);
                }
            }

            internal void Publish(Client client)
            {
                foreach (var stack in _existing.Values.Where(stack => stack.Final != stack.Original))
                {
                    stack.Item.StackSize = stack.Final;
                    client.CallMethod(stack.Item.EntityId, new SetStackCountPacket(stack.Final));
                }

                foreach (var source in _sources)
                {
                    source.Loot.Taken = true;

                    if (source.Remaining == 0)
                    {
                        EntityManager.Instance.ReleaseEntity(source.Item.EntityId, EntityType.Item);
                        continue;
                    }

                    source.Item.StackSize = source.Remaining;
                    source.Item.OwnerId = client.Player.Id;
                    source.Item.OwnerSlotId = (uint)source.Slot.Value;
                    client.Player.Inventory.PersonalInventory[source.Slot.Value] = source.Item.EntityId;
                    ItemManager.Instance.SendItemDataToClient(client, source.Item, false);
                    client.CallMethod(SysEntity.ClientInventoryManagerId,
                        new InventoryAddItemPacket(
                            InventoryType.Personal,
                            source.Item.EntityId,
                            (uint)source.Slot.Value));
                }
            }

            private static int CategoryStart(Item item)
            {
                var category = (int)item.ItemTemplate.InventoryCategory;
                if (category < 1 || category > 5)
                    throw new GameplayRejectionException("Loot item has an invalid inventory category.");
                return (category - 1) * 50;
            }

            private static uint MaximumStack(Item item)
            {
                var info = EntityClassManager.Instance.GetItemClassInfo(item);
                if (info == null || info.StackSize == 0 || item.StackSize > info.StackSize)
                    throw new GameplayRejectionException("Loot item has invalid stack metadata.");
                return info.StackSize;
            }
        }
    }
}
