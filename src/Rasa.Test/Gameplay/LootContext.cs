using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Packets;
    using Rasa.Packets.Protocol;
    using Rasa.Structures;
    using Rasa.Test.World;

    internal sealed class LootContext : IDisposable
    {
        internal WeaponAmmoContext Storage { get; } = new(characterId: 42);
        internal Client Client => Storage.Client;
        internal MapChannel Map => Storage.World.Map;
        internal Creature Corpse { get; }
        internal LootDispenser Loot { get; }
        private readonly HashSet<ulong> _originalItems;

        internal LootContext(uint quantity = 3, int credits = 7)
        {
            _originalItems = EntityManager.Instance.Items.Keys.ToHashSet();
            var template = new ItemTemplate(new Structures.World.ItemTemplateItemClassEntry
            {
                ItemTemplateId = 28, ItemClass = 3147
            }) { InventoryCategory = (InventoryCategory)2 };
            ItemManager.Instance.ItemTemplateItemClass.TryAdd(28, (EntityClasses)3147);
            EntityClassManager.Instance.LoadedEntityClasses[(EntityClasses)3147].ItemTemplates[28] = template;
            Client.Player.Credits[CurencyType.Credits] = 100;
            using (var database = Storage.Open())
            {
                var character = database.CharacterEntries.Single();
                character.Credit = 100;
                database.SaveChanges();
            }
            Corpse = new Creature
            {
                EntityClass = EntityClasses.HumanBaseMale, MapContextId = Map.MapInfo.MapContextId,
                Level = 1, State = CharacterState.Dead,
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                Attributes = Enum.GetValues<Attributes>().ToDictionary(
                    id => id, id => new ActorAttributes(id, 100, 100, 0, 0, 0))
            };
            CellManager.Instance.AddToWorld(Map, Corpse);
            Loot = LootDispenserManager.Instance.Create(Client, Corpse);
            foreach (var item in Loot.LootItems)
                EntityManager.Instance.FreeEntity(item.EntityId);
            Loot.LootItems.Clear();
            if (quantity > 0)
                Loot.LootItems.Add(new LootItem(28, 3147, quantity, Client.Player.EntityId, 0));
            Loot.Credits = credits;
            Drain();
        }

        internal List<PythonPacket> Drain() => WorldTestContext.Drain(Client)
            .Select(packet => packet.Message).OfType<CallMethodMessage>().Select(message => message.Packet).ToList();

        internal int ReadCredits()
        {
            using var database = Storage.Open();
            return database.CharacterEntries.AsNoTracking().Single().Credit;
        }

        internal Item AddAmmo(uint count, uint slot = 50)
        {
            var item = Storage.AddAmmo(count, slot);
            item.ItemTemplate.InventoryCategory = (InventoryCategory)2;
            return item;
        }

        public void Dispose()
        {
            CellManager.Instance.RemoveCreatureFromWorld(Map, Corpse);
            foreach (var loot in Map.LootDispensers.Values)
            {
                foreach (var item in loot.LootItems)
                    EntityManager.Instance.FreeEntity(item.EntityId);
            }
            Map.LootDispensers.Clear();
            foreach (var id in EntityManager.Instance.Items.Keys.Except(_originalItems).ToArray())
                EntityManager.Instance.ReleaseEntity(id, EntityType.Item);
            ItemManager.Instance.ItemTemplateItemClass.Remove(28);
            Storage.Dispose();
        }
    }
}
