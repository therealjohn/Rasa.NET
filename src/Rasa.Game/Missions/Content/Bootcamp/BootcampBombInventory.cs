using System;
using System.Collections.Generic;
using System.Linq;

namespace Rasa.Game.Missions.Content.Bootcamp
{
    using Data;
    using Managers;
    using Packets.Inventory.Server;
    using Repositories.Char;
    using Repositories.UnitOfWork;
    using Structures;
    using Structures.Char;

    internal static class BootcampBombInventory
    {
        private const uint BombTemplate = 11519;
        private const string IssuedPrefix = "bootcamp-bomb-issued:";

        internal static Action<Client> Plan(Client client, ICharUnitOfWork unit, MissionApplication missions,
            uint? changedMission = null)
        {
            var player = client?.Player;
            if (player?.MapChannel?.IsPrivateInstance != true || player.MapContextId != 1985 ||
                player.MapChannel.OwnerCharacterId != player.Id || client.AccountEntry == null ||
                !(player.Missions.ContainsKey(1995) || player.Missions.ContainsKey(2005) ||
                    changedMission is 1995 or 2005))
                return null;

            var supported = new[] { (Id: 1995U, Script: "bootcamp.reinforcements"), (Id: 2005U, Script: "bootcamp.bomb-retry") }
                .Where(binding => missions.TryGetOperationalMission(binding.Id, out var definition) &&
                    definition.ContentRevision == "deployment_11" && missions.Scenes.UsesScript(binding.Id, binding.Script))
                .Select(binding => binding.Id).ToArray();
            if (supported.Length == 0)
                return null;
            var assignments = supported.Select(id => unit.CharacterMissions.GetByCharacterAndMission(player.Id, id))
                .Where(assignment => assignment != null).ToArray();
            if (assignments.Length == 0 && (!changedMission.HasValue || !supported.Contains(changedMission.Value)))
                return null;
            var carrying = assignments.SingleOrDefault(assignment =>
                assignment.MissionState == (uint)MissionState.Active &&
                unit.CharacterMissionDeadlines.Get(player.Id, assignment.MissionId)?.State == CharacterMissionDeadlineState.Active);
            var rows = unit.CharacterInventories.GetItems(client.AccountEntry.Id)
                .Where(row => row.CharacterId == player.Id && row.InventoryType == (uint)InventoryType.Personal &&
                    unit.Items.GetItem(row.ItemId)?.ItemTemplateId == BombTemplate).ToArray();
            if (carrying == null)
            {
                if (rows.Length == 0)
                    return null;
                var quantities = new Dictionary<ulong, uint>();
                foreach (var row in rows)
                {
                    if (row.SlotId >= player.Inventory.PersonalInventory.Count)
                        throw new GameplayRejectionException("The mission bomb has an invalid inventory slot.");
                    var item = EntityManager.Instance.GetItem(player.Inventory.PersonalInventory[(int)row.SlotId]);
                    if (item?.Id != row.ItemId)
                        throw new GameplayRejectionException("The mission bomb inventory changed before removal.");
                    quantities.Add(item.EntityId, item.StackSize);
                }
                var consumption = new InventoryManager.InventoryConsumption();
                consumption.PlanAndSave(client, quantities, unit);
                return consumption.Publish;
            }

            if (rows.Length > 1 || rows.Length == 1 && unit.Items.GetItem(rows[0].ItemId).StackSize != 1)
                throw new GameplayRejectionException("The active bomb attempt must own exactly one bomb.");
            var key = IssuedPrefix + carrying.AssignmentId;
            if (rows.Length == 1)
            {
                if (!unit.CharacterMissionScenario.HasStep(player.Id, carrying.MissionId, key))
                    unit.CharacterMissionScenario.Add(new CharacterMissionScenarioStepEntry(player.Id, carrying.MissionId, key));
                return null;
            }
            if (unit.CharacterMissionScenario.HasStep(player.Id, carrying.MissionId, key))
                throw new GameplayRejectionException("The issued mission bomb is missing; restore it to Mission inventory or retry the failed attempt.");

            var template = ItemManager.Instance.GetItemTemplateById(BombTemplate);
            var itemClass = template == null ? null : EntityClassManager.Instance.GetClassInfo(template.Class)?.ItemClassInfo;
            if (template?.InventoryCategory != InventoryCategory.Mission || itemClass == null)
                throw new GameplayRejectionException("The native Bootcamp bomb item template is unavailable.");
            using (var validation = new InventoryManager.InventoryGrant())
                validation.PlanAndSave(client, Array.Empty<InventoryManager.InventoryItemGrant>(), unit);
            var slot = player.Inventory.PersonalInventory.FindIndex(150, 50, id => id == 0);
            if (slot < 0)
                throw new GameplayRejectionException("Mission inventory is full; make room before recovering the bomb.");
            var saved = new Item(BombTemplate, 1, itemClass.MaxHitPoints, 0);
            saved.Id = unit.Items.CreateItem(saved);
            if (saved.Id == 0)
                throw new GameplayRejectionException("Mission bomb creation returned an invalid identity.");
            unit.CharacterInventories.AddInvItem(client.AccountEntry.Id, player.Id,
                (uint)InventoryType.Personal, (uint)slot, saved.Id);
            unit.CharacterMissionScenario.Add(new CharacterMissionScenarioStepEntry(player.Id, carrying.MissionId, key));
            var published = false;
            return recipient =>
            {
                if (published)
                    return;
                published = true;
                var item = new Item
                {
                    Id = saved.Id, ItemTemplate = template, ItemTemplateId = BombTemplate,
                    OwnerId = player.Id, OwnerSlotId = (uint)slot, StackSize = 1,
                    CurrentHitPoints = saved.CurrentHitPoints, Crafter = ""
                };
                EntityManager.Instance.RegisterEntity(item.EntityId, EntityType.Item);
                EntityManager.Instance.RegisterItem(item.EntityId, item);
                player.Inventory.PersonalInventory[slot] = item.EntityId;
                MissionApplication.TryPublish(() => ItemManager.Instance.SendItemDataToClient(recipient, item, false),
                    "Bootcamp mission bomb item");
                MissionApplication.TryPublish(() => recipient.CallMethod(SysEntity.ClientInventoryManagerId,
                    new InventoryAddItemPacket(InventoryType.Personal, item.EntityId, (uint)slot)), "Bootcamp mission bomb inventory");
            };
        }

    }
}
