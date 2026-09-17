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
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.Protocol;
    using Rasa.Structures;
    using Rasa.Test.World;

    internal sealed class AbilityTestContext : IDisposable
    {
        internal WeaponAmmoContext Storage { get; } = new();
        internal Client Client => Storage.Client;
        internal MapChannel Map => Storage.World.Map;
        internal ManifestationManager Manager { get; }
        internal long Now { get; set; } = 10000;
        internal System.Func<int, int, int> Roll { get; set; } = (minimum, _) => minimum;
        private readonly List<Creature> _targets = new();

        internal AbilityTestContext()
        {
            Client.Player.Level = 15;
            Client.Player.Attributes = Enum.GetValues<Attributes>().ToDictionary(
                id => id, id => new ActorAttributes(id, 1000, 1000, 1000, 0, 0));
            Client.Player.Attributes[Attributes.Mind] = new ActorAttributes(Attributes.Mind, 38, 38, 38, 0, 0);
            using var database = Storage.Open();
            database.CharacterEntries.Single().Level = 15;
            database.SaveChanges();
            Manager = new ManifestationManager(Storage, null, () => Now, (minimum, maximum) => Roll(minimum, maximum));
        }

        internal void Learn(int skillId, int rank)
        {
            Manager.LevelSkills(Client, new LevelSkillsPacket
            {
                ListLenght = 1, SkillIds = new[] { skillId }, SkillLevels = new[] { rank }
            });
            Drain();
        }

        internal Creature Target(float distance = 10)
        {
            var target = new Creature
            {
                MapContextId = Map.MapInfo.MapContextId,
                Position = new System.Numerics.Vector3(distance, 0, 0),
                State = CharacterState.Normal, Faction = Factions.Bane, Level = 1,
                EntityClass = EntityClasses.HumanBaseMale, AppearanceData = new(),
                Attributes = new()
                {
                    [Attributes.Health] = new ActorAttributes(Attributes.Health, 10000, 10000, 10000, 0, 0),
                    [Attributes.Armor] = new ActorAttributes(Attributes.Armor, 0, 0, 0, 0, 0)
                }
            };
            _targets.Add(target);
            EntityManager.Instance.RegisterEntity(target.EntityId, EntityType.Creature);
            EntityManager.Instance.RegisterCreature(target);
            EntityManager.Instance.RegisterActor(target.EntityId, target);
            var seed = CellManager.Instance.GetCellSeed(target.Position);
            target.Cells = CellManager.Instance.CreateCellMatrix(Map, seed & 0xFFFF, seed >> 16);
            var cell = CellManager.Instance.GetCell(Map, seed & 0xFFFF, seed >> 16);
            cell.CreatureList.Add(target);
            return target;
        }

        internal void Cast(int rank = 1, ulong target = 0, ActionId action = ActionId.AaRecruitLightning) =>
            Manager.RequestPerformAbility(Client, new RequestPerformAbilityPacket
            {
                ActionId = action, ActionArgId = rank, Target = target
            });

        internal void Advance(long milliseconds)
        {
            Now += milliseconds;
            new ActorActionManager(Manager).DoWork(Map, milliseconds);
        }

        internal List<PythonPacket> Drain() => WorldTestContext.Drain(Client)
            .Select(p => p.Message).OfType<CallMethodMessage>().Select(p => p.Packet).ToList();

        internal static byte[] Encode(PythonPacket packet)
        {
            using var stream = new System.IO.MemoryStream();
            using var binary = new System.IO.BinaryWriter(stream);
            using var writer = new Rasa.Memory.PythonWriter(binary);
            packet.Write(writer);
            return stream.ToArray();
        }

        internal static T Decode<T>(Action<Rasa.Memory.PythonWriter> payload) where T : ClientPythonPacket, new()
        {
            using var stream = new System.IO.MemoryStream();
            using var binary = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            using (var writer = new Rasa.Memory.PythonWriter(binary))
                payload(writer);
            stream.Position = 0;
            using var reader = new Rasa.Memory.PythonReader(new System.IO.BinaryReader(stream));
            var packet = new T();
            packet.Read(reader);
            return packet;
        }

        public void Dispose()
        {
            foreach (var target in _targets)
            {
                EntityManager.Instance.UnregisterEntity(target.EntityId);
                EntityManager.Instance.UnregisterCreature(target.EntityId);
                EntityManager.Instance.UnregisterActor(target.EntityId);
                EntityManager.Instance.FreeEntity(target.EntityId);
            }
            Storage.Dispose();
        }
    }
}
