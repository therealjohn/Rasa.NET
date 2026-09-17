using System.Linq;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Structures;
    using Rasa.Managers;

    [TestClass]
    [DoNotParallelize]
    public class AbilityTrayTests
    {
        [TestMethod]
        [DataRow("select")]
        [DataRow("set")]
        [DataRow("swap")]
        [DataRow("train")]
        public void MissingDurableCharacterRejectsLoadoutWithoutBlockingHealthyRequests(string operation)
        {
            using (var context = new AbilityTestContext())
            {
                context.Learn(165, 1);
                context.Manager.RequestSetAbilitySlot(context.Client,
                    new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 1 });
                context.Drain();
                using (var unit = context.Storage.CreateChar())
                {
                    unit.Characters.Delete(context.Client.Player.Id);
                    unit.Complete();
                }
                var saves = context.Storage.SaveAttempts;

                ChangeLoadout(context, operation);

                Assert.AreEqual(saves, context.Storage.SaveAttempts);
                Assert.AreEqual(0, context.Client.Player.CurrentAbilityDrawer);
                Assert.AreEqual(1, context.Client.Player.Skills[(SkillId)165].SkillLevel);
                Assert.AreEqual(1, context.Client.Player.Abilities.Count);
                Assert.AreEqual(401, context.Client.Player.Abilities[0].AbilityId);
                Assert.AreEqual(0, context.Drain().Count);
            }
            using var healthy = new AbilityTestContext();
            healthy.Manager.RequestArmAbility(healthy.Client, 24);
            Assert.AreEqual(24, healthy.Client.Player.CurrentAbilityDrawer);
            Assert.AreEqual(1, healthy.Drain().OfType<AbilityDrawerSlotPacket>().Count());
        }

        [TestMethod]
        [DataRow("select", false)]
        [DataRow("select", true)]
        [DataRow("set", false)]
        [DataRow("set", true)]
        [DataRow("swap", false)]
        [DataRow("swap", true)]
        [DataRow("train", false)]
        [DataRow("train", true)]
        public void UnrelatedLoadoutSaveErrorsKeepTheirIdentityStackAndRollback(string operation, bool invalidOperation)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 1 });
            context.Drain();
            System.Exception injected = invalidOperation
                ? new System.InvalidOperationException("Unrelated loadout bug.")
                : new System.NullReferenceException("Unrelated loadout bug.");
            context.Storage.AfterSave = _ => throw injected;

            var actual = invalidOperation
                ? Assert.ThrowsExactly<System.InvalidOperationException>(() => ChangeLoadout(context, operation))
                : (System.Exception)Assert.ThrowsExactly<System.NullReferenceException>(() => ChangeLoadout(context, operation));

            Assert.AreSame(injected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(UnrelatedLoadoutSaveErrorsKeepTheirIdentityStackAndRollback));
            Assert.AreEqual(0, context.Client.Player.CurrentAbilityDrawer);
            Assert.AreEqual(1, context.Client.Player.Skills[(SkillId)165].SkillLevel);
            Assert.AreEqual(1, context.Client.Player.Abilities.Count);
            Assert.AreEqual(0, context.Drain().Count);
            using (var unit = context.Storage.CreateChar())
            {
                Assert.AreEqual((byte)0, unit.Characters.Get(context.Client.Player.Id).CurrentAbilitySlot);
                Assert.AreEqual(1, unit.CharacterSkills.GetCharacterSkills(context.Client.Player.Id).Single().SkillLevel);
                Assert.AreEqual(0, unit.CharacterAbilityDrawers.GetCharacterAbilities(context.Client.Player.Id).Single().AbilitySlot);
            }
            context.Storage.AfterSave = null;
            ChangeLoadout(context, operation);
            Assert.IsTrue(context.Drain().Count > 0);
        }

        private static void ChangeLoadout(AbilityTestContext context, string operation)
        {
            switch (operation)
            {
                case "select": context.Manager.RequestArmAbility(context.Client, 24); break;
                case "set": context.Manager.RequestSetAbilitySlot(context.Client,
                    new RequestSetAbilitySlotPacket { SlotId = 24, AbilityId = 401, AbilityLevel = 1 }); break;
                case "swap": context.Manager.RequestSwapAbilitySlots(context.Client,
                    new RequestSwapAbilitySlotsPacket { FromSlot = 0, ToSlot = 24 }); break;
                case "train": context.Manager.LevelSkills(context.Client,
                    new LevelSkillsPacket { ListLenght = 1, SkillIds = new[] { 165 }, SkillLevels = new[] { 2 } }); break;
                default: Assert.Fail($"Unknown loadout operation {operation}."); break;
            }
        }

        [TestMethod]
        [DataRow("skill")]
        [DataRow("slot")]
        [DataRow("selection")]
        public void CorruptRestoredLoadoutsNeverPublishAnInvalidDrawer(string corrupt)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 1 });
            if (corrupt == "skill") context.Client.Player.Skills[(SkillId)165].AbilityId = 194;
            if (corrupt == "slot") context.Client.Player.Abilities[0].AbilitySlotId = 25;
            if (corrupt == "selection") context.Client.Player.CurrentAbilityDrawer = 25;
            context.Drain();
            context.Manager.PublishAbilityLoadout(context.Client);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void HistoricalCharacterRowsAndDrawerContentsSurviveMigrationAndReopening()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 3);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 24, AbilityId = 401, AbilityLevel = 2 });
            using (var database = context.Storage.Open())
            {
                database.GetService<IMigrator>().Migrate("20230202081214_edited_character_teleporter");
                var previous = database.Database.GetAppliedMigrations().ToArray();
                database.Database.Migrate();
                CollectionAssert.IsSubsetOf(previous, database.Database.GetAppliedMigrations().ToArray());
            }
            using var unit = context.Storage.CreateChar();
            var row = unit.Characters.Get(context.Client.Player.Id);
            var map = new MapChannelManager(context.Storage);
            var reloaded = new Manifestation(row, new())
            {
                Abilities = map.GetPlayerAbilities(row.Id), Skills = map.GetPlayerSkills(row.Id)
            };
            Assert.AreEqual(0, reloaded.CurrentAbilityDrawer);
            Assert.AreEqual(3, reloaded.Skills[(SkillId)165].SkillLevel);
            Assert.AreEqual(2u, reloaded.Abilities[24].AbilityLevel);
            Assert.AreEqual((byte)15, reloaded.Level);
        }

        [TestMethod]
        public void KnownUnimplementedAbilitiesRemainLearnableAndAssignableButExecutionFails()
        {
            using var context = new AbilityTestContext();
            context.Learn(20, 1);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 137, AbilityLevel = 1 });
            Assert.AreEqual(137, context.Client.Player.Abilities[0].AbilityId);
            context.Drain();
            context.Cast(action: (ActionId)137);
            Assert.AreEqual(1, context.Drain().OfType<ActionFailedPacket>().Count());
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
        }

        [TestMethod]
        public void TrainingUsesCumulativeCostsAndPublishesFinalAvailablePointsAfterCommit()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 2);
            context.Manager.LevelSkills(context.Client, new LevelSkillsPacket
            {
                ListLenght = 1, SkillIds = new[] { 165 }, SkillLevels = new[] { 3 }
            });
            Assert.AreEqual(31, context.Manager.GetSkillPointsAvailable(context.Client.Player));
            var packets = context.Drain();
            Assert.IsInstanceOfType<SkillsPacket>(packets[0]);
            Assert.IsInstanceOfType<AbilitiesPacket>(packets[1]);
            Assert.IsInstanceOfType<AvailableAllocationPointsPacket>(packets[2]);
            using var unit = context.Storage.CreateChar();
            Assert.AreEqual(3, unit.CharacterSkills.GetCharacterSkills(context.Client.Player.Id).Single().SkillLevel);
        }

        [TestMethod]
        public void StaleDurableLearnedStateCannotBeOverwrittenByTrainingOrDrawerChanges()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 2);
            using (var unit = context.Storage.CreateChar())
                unit.CharacterSkills.AddOrUpdate(context.Client.Player.Id, 165, 401, 3);
            var saves = context.Storage.SaveAttempts;
            context.Manager.LevelSkills(context.Client, new LevelSkillsPacket
            {
                ListLenght = 1, SkillIds = new[] { 165 }, SkillLevels = new[] { 4 }
            });
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 2 });
            Assert.AreEqual(saves, context.Storage.SaveAttempts);
            Assert.AreEqual(2, context.Client.Player.Skills[(SkillId)165].SkillLevel);
            Assert.AreEqual(0, context.Client.Player.Abilities.Count);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void OversizedTrainingListsAreRejectedBeforeReadingOrAllocatingEntries()
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                AbilityTestContext.Decode<LevelSkillsPacket>(writer =>
                {
                    writer.WriteTuple(1);
                    writer.WriteList(74);
                    for (var i = 0; i < 74; i++)
                    {
                        writer.WriteTuple(2);
                        writer.WriteInt(49);
                        writer.WriteInt(1);
                    }
                }));
        }

        [TestMethod]
        public void SwapDecodingCannotWrapALargeSourceSlotIntoAValidSlot()
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                AbilityTestContext.Decode<RequestSwapAbilitySlotsPacket>(writer =>
                {
                    writer.WriteTuple(2);
                    writer.WriteLong(1L << 32);
                    writer.WriteInt(0);
                }));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DrawerDecoderKeepsNoneDistinctFromNumericZero(bool none)
        {
            var packet = AbilityTestContext.Decode<RequestSetAbilitySlotPacket>(writer =>
            {
                writer.WriteTuple(4);
                writer.WriteInt(0);
                if (none) writer.WriteNoneStruct(); else writer.WriteLong(0);
                if (none) writer.WriteNoneStruct(); else writer.WriteLong(0);
                writer.WriteNoneStruct();
            });
            Assert.AreEqual(!none, packet.AbilityId.HasValue);
            Assert.AreEqual(!none, packet.AbilityLevel.HasValue);
        }

        [TestMethod]
        [DataRow(-1, 1)]
        [DataRow(0, 1)]
        [DataRow(2, 1)]
        [DataRow(200, 1)]
        [DataRow(int.MaxValue, 1)]
        [DataRow(49, -1)]
        [DataRow(49, 0)]
        [DataRow(49, 6)]
        public void TrainingRejectsUnknownSkillsAndInvalidRanksWithoutWrites(int id, int rank)
        {
            using var context = new AbilityTestContext();
            context.Manager.LevelSkills(context.Client, new LevelSkillsPacket
            {
                ListLenght = 1, SkillIds = new[] { id }, SkillLevels = new[] { rank }
            });
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            Assert.AreEqual(0, context.Client.Player.Skills.Count);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        [DataRow("duplicate")]
        [DataRow("length")]
        [DataRow("null")]
        [DataRow("budget")]
        public void InvalidTrainingBatchesAreRejectedAsAWhole(string invalid)
        {
            using var context = new AbilityTestContext();
            var packet = new LevelSkillsPacket
            {
                ListLenght = 2, SkillIds = new[] { 49, 165 }, SkillLevels = new[] { 5, 5 }
            };
            if (invalid == "duplicate") packet.SkillIds[1] = 49;
            if (invalid == "length") packet.ListLenght = 1;
            if (invalid == "null") packet.SkillLevels = null;
            if (invalid == "budget") context.Client.Player.Level = 1;
            context.Manager.LevelSkills(context.Client, packet);
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            Assert.AreEqual(0, context.Client.Player.Skills.Count);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void DrawerSetCommitsBeforeChangingRuntimeOrPublishingAndSurvivesRelog()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 5);
            context.Storage.BeforeSave = _ =>
            {
                Assert.AreEqual(0, context.Client.Player.Abilities.Count);
                Assert.AreEqual(0, context.Drain().Count);
            };
            context.Storage.AfterSave = _ => throw new DbUpdateException("Injected drawer save failure.");
            var packet = new RequestSetAbilitySlotPacket { SlotId = 24, AbilityId = 194, AbilityLevel = 2 };
            context.Manager.RequestSetAbilitySlot(context.Client, packet);
            Assert.AreEqual(0, context.Client.Player.Abilities.Count);
            Assert.AreEqual(0, context.Drain().Count);
            using (var database = context.Storage.Open())
                Assert.AreEqual(0, database.CharacterAbilityDrawerEntries.Count());

            context.Storage.AfterSave = null;
            context.Manager.RequestSetAbilitySlot(context.Client, packet);
            Assert.AreEqual(2u, new MapChannelManager(context.Storage).GetPlayerAbilities(context.Client.Player.Id)[24].AbilityLevel);
            Assert.AreEqual(1, context.Drain().OfType<AbilityDrawerPacket>().Count());
        }

        [TestMethod]
        public void SelectedSlotIsSavedAndLoadedByTheCharacterManifestation()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 3);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 24, AbilityId = 401, AbilityLevel = 1 });
            context.Drain();
            context.Manager.RequestArmAbility(context.Client, 24);
            using var unit = context.Storage.CreateChar();
            var reloaded = new Manifestation(unit.Characters.Get(context.Client.Player.Id), new());
            Assert.AreEqual(24, reloaded.CurrentAbilityDrawer);
            Assert.AreEqual(24, context.Drain().OfType<AbilityDrawerSlotPacket>().Single().AbilityDrawerSlot);
        }

        [TestMethod]
        public void SwappingIntoAnEmptySlotRollsBackBothRowsOnFailure()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 3);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 1 });
            context.Drain();
            var start = context.Storage.SaveAttempts;
            context.Storage.AfterSave = _ =>
            {
                if (context.Storage.SaveAttempts == start + 2)
                    throw new DbUpdateException("Injected second swap write failure.");
            };
            var packet = new RequestSwapAbilitySlotsPacket { FromSlot = 0, ToSlot = 24 };
            context.Manager.RequestSwapAbilitySlots(context.Client, packet);
            Assert.IsTrue(context.Client.Player.Abilities.ContainsKey(0));
            Assert.IsFalse(context.Client.Player.Abilities.ContainsKey(24));
            Assert.AreEqual(0, context.Drain().Count);
            var reloaded = new MapChannelManager(context.Storage).GetPlayerAbilities(context.Client.Player.Id);
            Assert.IsTrue(reloaded.ContainsKey(0));
            Assert.IsFalse(reloaded.ContainsKey(24));
        }

        [TestMethod]
        [DataRow(-1, 401L, 1L)]
        [DataRow(25, 401L, 1L)]
        [DataRow(int.MaxValue, 401L, 1L)]
        [DataRow(0, 999L, 1L)]
        [DataRow(0, 194L, 1L)]
        [DataRow(0, 401L, 0L)]
        [DataRow(0, 401L, 4L)]
        [DataRow(0, 401L, -1L)]
        [DataRow(0, long.MaxValue, 1L)]
        [DataRow(0, 0L, 1L)]
        public void DrawerRejectsInvalidSlotsIdsAndUnlearnedRanks(int slot, long ability, long rank)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 3);
            var saves = context.Storage.SaveAttempts;
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = slot, AbilityId = ability, AbilityLevel = rank });
            Assert.AreEqual(saves, context.Storage.SaveAttempts);
            Assert.AreEqual(0, context.Client.Player.Abilities.Count);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ClearFormsStayDistinctAndNeitherAcceptsPartialNull(bool useNone)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 3);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 2 });
            context.Drain();
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = null, AbilityLevel = 0 });
            Assert.AreEqual(2u, context.Client.Player.Abilities[0].AbilityLevel);
            Assert.AreEqual(0, context.Drain().Count);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = useNone ? null : 0, AbilityLevel = useNone ? null : 0 });
            Assert.AreEqual(0, context.Client.Player.Abilities.Count);
            Assert.AreEqual(0, new MapChannelManager(context.Storage).GetPlayerAbilities(context.Client.Player.Id).Count);
            Assert.AreEqual(0, context.Drain().OfType<AbilityDrawerPacket>().Single().Abilities.Count);
        }

        [TestMethod]
        public void SwapCanMoveFromEitherEmptySideAndOccupiedSlotsWithoutAliasing()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 3);
            context.Learn(165, 3);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 194, AbilityLevel = 1 });
            context.Manager.RequestSwapAbilitySlots(context.Client, new RequestSwapAbilitySlotsPacket { FromSlot = 24, ToSlot = 0 });
            Assert.IsFalse(context.Client.Player.Abilities.ContainsKey(0));
            Assert.AreEqual(194, context.Client.Player.Abilities[24].AbilityId);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 2 });
            context.Manager.RequestSwapAbilitySlots(context.Client, new RequestSwapAbilitySlotsPacket { FromSlot = 24, ToSlot = 0 });
            Assert.AreEqual(194, context.Client.Player.Abilities[0].AbilityId);
            Assert.AreEqual(401, context.Client.Player.Abilities[24].AbilityId);
            Assert.AreEqual(0, context.Client.Player.Abilities[0].AbilitySlotId);
            Assert.AreEqual(24, context.Client.Player.Abilities[24].AbilitySlotId);
            context.Manager.RequestSwapAbilitySlots(context.Client, new RequestSwapAbilitySlotsPacket { FromSlot = 24, ToSlot = 24 });
            Assert.AreEqual(401, context.Client.Player.Abilities[24].AbilityId);
        }

        [TestMethod]
        public void SelectionFailureLeavesRuntimePacketsAndPersistedSelectionUntouched()
        {
            using var context = new AbilityTestContext();
            context.Storage.AfterSave = _ => throw new DbUpdateException("Injected selection save failure.");
            context.Manager.RequestArmAbility(context.Client, 24);
            Assert.AreEqual(0, context.Client.Player.CurrentAbilityDrawer);
            Assert.AreEqual(0, context.Drain().Count);
            using var unit = context.Storage.CreateChar();
            Assert.AreEqual((byte)0, unit.Characters.Get(context.Client.Player.Id).CurrentAbilitySlot);
        }

        [TestMethod]
        public void QueuedSkillsAndDrawerSnapshotsDoNotFollowLaterMutationsOrAnotherPlayer()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            var firstSkills = new SkillsPacket(context.Client.Player.Skills);
            context.Manager.RequestSetAbilitySlot(context.Client,
                new RequestSetAbilitySlotPacket { SlotId = 0, AbilityId = 401, AbilityLevel = 1 });
            var firstDrawer = context.Drain().OfType<AbilityDrawerPacket>().Single();
            var firstPreload = new PreloadDataPacket(0, context.Client.Player.Abilities);
            var preloadBytes = AbilityTestContext.Encode(firstPreload);
            var skillsBytes = AbilityTestContext.Encode(firstSkills);
            var drawerBytes = AbilityTestContext.Encode(firstDrawer);
            context.Client.Player.Skills[(SkillId)165].SkillLevel = 5;
            context.Client.Player.Abilities[0].AbilityLevel = 5;
            _ = new SkillsPacket(new());
            CollectionAssert.AreEqual(skillsBytes, AbilityTestContext.Encode(firstSkills));
            CollectionAssert.AreEqual(drawerBytes, AbilityTestContext.Encode(firstDrawer));
            CollectionAssert.AreEqual(preloadBytes, AbilityTestContext.Encode(firstPreload));
        }

        [TestMethod]
        public void RelogPublicationIncludesAnEmptyDrawerAndThePersistedCursor()
        {
            using var context = new AbilityTestContext();
            context.Manager.RequestArmAbility(context.Client, 24);
            context.Drain();
            context.Manager.PublishAbilityLoadout(context.Client);
            var packets = context.Drain();
            Assert.IsInstanceOfType<AbilityDrawerPacket>(packets[0]);
            Assert.AreEqual(0, ((AbilityDrawerPacket)packets[0]).Abilities.Count);
            Assert.AreEqual(24, ((AbilityDrawerSlotPacket)packets[1]).AbilityDrawerSlot);
        }

        [TestMethod]
        public void TrainingCommitsTheEntireBatchBeforeRuntimeAndAcknowledgements()
        {
            using var context = new AbilityTestContext();
            context.Storage.BeforeSave = _ =>
            {
                Assert.AreEqual(0, context.Client.Player.Skills.Count);
                Assert.AreEqual(0, context.Drain().Count);
            };
            context.Storage.AfterSave = database =>
            {
                if (database.CharacterSkillsEntries.Count() == 2)
                    throw new DbUpdateException("Injected failure after the second training write.");
            };

            context.Manager.LevelSkills(context.Client, new LevelSkillsPacket
            {
                ListLenght = 2, SkillIds = new[] { 49, 165 }, SkillLevels = new[] { 2, 3 }
            });

            Assert.AreEqual(0, context.Client.Player.Skills.Count);
            Assert.AreEqual(0, context.Drain().Count);
            using (var reopened = context.Storage.Open())
                Assert.AreEqual(0, reopened.CharacterSkillsEntries.Count());
            context.Storage.AfterSave = null;
            context.Manager.LevelSkills(context.Client, new LevelSkillsPacket
            {
                ListLenght = 2, SkillIds = new[] { 49, 165 }, SkillLevels = new[] { 2, 3 }
            });
            Assert.AreEqual(2, context.Client.Player.Skills[(SkillId)49].SkillLevel);
            Assert.AreEqual(3, context.Client.Player.Skills[(SkillId)165].SkillLevel);
            Assert.AreEqual(3, context.Drain().Count);
        }
    }
}
