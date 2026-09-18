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
        internal const int PersonalCategorySize = 50;
        internal const int PersonalCategoryCount = 5;

        internal readonly struct InventoryItemGrant
        {
            internal uint ItemTemplateId { get; }
            internal uint Quantity { get; }

            internal InventoryItemGrant(uint itemTemplateId, uint quantity)
            {
                ItemTemplateId = itemTemplateId;
                Quantity = quantity;
            }
        }

        internal sealed class InventoryGrant : IDisposable
        {
            private sealed class Slot
            {
                internal Item Existing;
                internal Item Staged;
                internal ItemTemplate Template;
                internal uint Count;
                internal uint OriginalCount;
                internal uint Maximum;
                internal bool OwnershipTransferred;
            }

            private readonly Slot[] _slots =
                new Slot[PersonalCategorySize * PersonalCategoryCount];
            private readonly Action<Item> _beforeRegister;

            internal InventoryGrant(Action<Item> beforeRegister = null)
            {
                _beforeRegister = beforeRegister;
            }

            internal void PlanAndSave(
                Client client,
                IReadOnlyList<InventoryItemGrant> grants,
                ICharUnitOfWork unitOfWork)
            {
                var player = client.Player;
                var inventory = player.Inventory.PersonalInventory;
                if (inventory.Count != _slots.Length)
                    throw new GameplayRejectionException(
                        "Personal inventory is not initialized.");

                var rows = unitOfWork.CharacterInventories.GetItems(client.AccountEntry.Id);
                var personalRows = rows.Where(row =>
                    row.CharacterId == player.Id &&
                    row.InventoryType == (uint)InventoryType.Personal).ToArray();
                if (personalRows.Select(row => row.SlotId).Distinct().Count() !=
                    personalRows.Length)
                    throw new GameplayRejectionException(
                        "Persisted personal inventory contains duplicate slots.");

                var personal = personalRows.ToDictionary(row => row.SlotId);
                if (personal.Keys.Any(slot => slot >= _slots.Length))
                    throw new GameplayRejectionException(
                        "Persisted inventory has an invalid slot.");

                var seen = new HashSet<uint>();
                for (var index = 0; index < _slots.Length; index++)
                {
                    personal.TryGetValue((uint)index, out var row);
                    if (inventory[index] == 0)
                    {
                        if (row != null)
                            throw new GameplayRejectionException(
                                "An empty runtime slot is occupied in storage.");
                        continue;
                    }

                    var item = EntityManager.Instance.GetItem(inventory[index]);
                    if (item?.ItemTemplate == null || item.Id == 0 ||
                        !seen.Add(item.Id) ||
                        EntityManager.Instance.GetEntityType(item.EntityId) != EntityType.Item ||
                        item.OwnerId != player.Id ||
                        item.OwnerSlotId != index ||
                        row?.ItemId != item.Id ||
                        rows.Count(entry => entry.ItemId == item.Id) != 1)
                        throw new GameplayRejectionException(
                            "Runtime inventory no longer matches its durable owner/slot.");

                    var template = RequireTemplate(item.ItemTemplate.ItemTemplateId);
                    var maximum = EntityClassManager.Instance.LoadedEntityClasses[
                        template.Class].ItemClassInfo.StackSize;
                    var saved = unitOfWork.Items.GetItem(item.Id);
                    if (template.Class != item.ItemTemplate.Class ||
                        saved?.ItemTemplateId != template.ItemTemplateId ||
                        saved.StackSize != item.StackSize ||
                        item.StackSize == 0 ||
                        item.StackSize > maximum ||
                        (int)template.InventoryCategory - 1 !=
                        index / PersonalCategorySize)
                        throw new GameplayRejectionException(
                            "An inventory stack changed or has invalid template/count data.");

                    _slots[index] = new Slot
                    {
                        Existing = item,
                        Template = template,
                        Count = item.StackSize,
                        OriginalCount = item.StackSize,
                        Maximum = maximum
                    };
                }

                foreach (var entry in grants)
                {
                    if (entry.ItemTemplateId == 0 || entry.Quantity == 0)
                        throw new GameplayRejectionException(
                            "Invalid inventory item grant.");

                    var template = RequireTemplate(entry.ItemTemplateId);
                    var maximum = EntityClassManager.Instance.LoadedEntityClasses[
                        template.Class].ItemClassInfo.StackSize;
                    var start =
                        ((int)template.InventoryCategory - 1) * PersonalCategorySize;
                    var remaining = entry.Quantity;

                    for (var index = start;
                         index < start + PersonalCategorySize && remaining > 0;
                         index++)
                    {
                        var slot = _slots[index];
                        if (slot?.Template.ItemTemplateId != template.ItemTemplateId)
                            continue;
                        var added = Math.Min(remaining, slot.Maximum - slot.Count);
                        slot.Count += added;
                        remaining -= added;
                    }

                    for (var index = start;
                         index < start + PersonalCategorySize && remaining > 0;
                         index++)
                    {
                        if (_slots[index] != null)
                            continue;
                        var added = Math.Min(remaining, maximum);
                        _slots[index] = new Slot
                        {
                            Template = template,
                            Count = added,
                            Maximum = maximum
                        };
                        remaining -= added;
                    }

                    if (remaining != 0)
                        throw new GameplayRejectionException(
                            "The complete item grant does not fit its inventory categories.");
                }

                for (var index = 0; index < _slots.Length; index++)
                {
                    var slot = _slots[index];
                    if (slot == null || slot.Count == slot.OriginalCount)
                        continue;

                    if (slot.Existing != null)
                    {
                        unitOfWork.Items.UpdateItemStackSize(
                            new Item(0, slot.Count, 0, 0) { Id = slot.Existing.Id });
                        continue;
                    }

                    slot.Staged = ItemManager.StageItem(slot.Template, slot.Count, "");
                    slot.Staged.OwnerId = player.Id;
                    slot.Staged.OwnerSlotId = (uint)index;
                    slot.Staged.Id = unitOfWork.Items.CreateItem(slot.Staged);
                    if (slot.Staged.Id == 0)
                        throw new GameplayRejectionException(
                            "Item creation returned an invalid durable identity.");
                    unitOfWork.CharacterInventories.AddInvItem(
                        client.AccountEntry.Id,
                        player.Id,
                        (uint)InventoryType.Personal,
                        (uint)index,
                        slot.Staged.Id);
                }
            }

            internal void ConvergeRuntime(Client client)
            {
                for (var index = 0; index < _slots.Length; index++)
                {
                    var slot = _slots[index];
                    if (slot == null || slot.Count == slot.OriginalCount)
                        continue;

                    if (slot.Existing != null)
                    {
                        slot.Existing.StackSize = slot.Count;
                        continue;
                    }

                    var item = slot.Staged;
                    if (!slot.OwnershipTransferred)
                    {
                        EntityManager.Instance.RegisterEntity(item.EntityId, EntityType.Item);
                        EntityManager.Instance.RegisterItem(item.EntityId, item);
                        slot.OwnershipTransferred = true;
                        MissionManager.TryPublish(
                            () => _beforeRegister?.Invoke(item),
                            $"mission reward item {item.Id} publication hook");
                    }
                    client.Player.Inventory.PersonalInventory[index] = item.EntityId;
                }
            }

            internal void Publish(Client client)
            {
                ConvergeRuntime(client);
                for (var index = 0; index < _slots.Length; index++)
                {
                    var slot = _slots[index];
                    if (slot == null || slot.Count == slot.OriginalCount)
                        continue;

                    if (slot.Existing != null)
                    {
                        MissionManager.TryPublish(
                            () => client.CallMethod(
                                slot.Existing.EntityId,
                                new SetStackCountPacket(slot.Count)),
                            $"mission reward stack {slot.Existing.Id}");
                        continue;
                    }

                    var item = slot.Staged;
                    MissionManager.TryPublish(
                        () => ItemManager.Instance.SendItemDataToClient(
                            client, item, false),
                        $"mission reward item {item.Id} entity data");
                    MissionManager.TryPublish(
                        () => client.CallMethod(
                            SysEntity.ClientInventoryManagerId,
                            new InventoryAddItemPacket(
                                InventoryType.Personal,
                                item.EntityId,
                                (uint)index)),
                        $"mission reward item {item.Id} inventory slot");
                }
            }

            private static ItemTemplate RequireTemplate(uint id)
            {
                if (!ItemManager.Instance.ItemTemplateItemClass.TryGetValue(
                        id, out var classId) ||
                    !EntityClassManager.Instance.LoadedEntityClasses.TryGetValue(
                        classId, out var info) ||
                    info.ItemClassInfo == null ||
                    info.ItemClassInfo.StackSize == 0 ||
                    !info.ItemTemplates.TryGetValue(id, out var template) ||
                    template == null ||
                    template.ItemTemplateId != id ||
                    template.Class != classId ||
                    template.ItemInfo == null ||
                    (int)template.InventoryCategory < 1 ||
                    (int)template.InventoryCategory > PersonalCategoryCount ||
                    (template.WeaponInfo != null && info.WeaponClassInfo == null))
                    throw new GameplayRejectionException(
                        $"Missing or invalid inventory template {id}.");

                return template;
            }

            public void Dispose()
            {
                foreach (var slot in _slots)
                    if (slot?.Staged != null && !slot.OwnershipTransferred)
                        EntityManager.Instance.FreeEntity(slot.Staged.EntityId);
            }
        }
    }
}
