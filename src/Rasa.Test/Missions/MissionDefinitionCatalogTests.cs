using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Structures;

    [TestClass]
    public class MissionDefinitionCatalogTests
    {
        [TestMethod]
        public void RecoveredCatalogContainsOnlySourceBackedInactiveDefinitions()
        {
            var catalog = MissionDefinitionCatalog.CreateRecoveredInactiveDefinitions();

            CollectionAssert.AreEquivalent(new uint[] { 1069, 1407, 1449 },
                new List<uint>(catalog.Keys));
            Assert.AreEqual("Receptive Reception", catalog[1069].Name);
            Assert.AreEqual(6038U, catalog[1069].ClientNameTextId);
            Assert.AreEqual(6042U, catalog[1069].Objectives[1].ClientNameTextId);
            Assert.AreEqual(13794U, catalog[1069].Objectives[2].ClientBodyTextId);
            Assert.IsFalse(catalog[1069].IsOperational);
            Assert.IsNull(catalog[1069].Objectives[1].Ordinal);
            Assert.IsNull(catalog[1069].Objectives[1].InitialState);
            Assert.IsNull(catalog[1069].Objectives[1].IsRequired);

            var solis = catalog[1069].Objectives[2].Conversations[0];
            Assert.AreEqual(168U, solis.NpcPackageId);
            Assert.AreEqual(1U, solis.PlayerFlagId);
            Assert.AreEqual(MissionObjectiveConversationType.Completion, solis.Type);
            Assert.AreEqual(112U, catalog[1069].Objectives[3].Conversations[0].NpcPackageId);
            Assert.AreEqual(113U, catalog[1407].Objectives[1].Conversations[0].NpcPackageId);
            Assert.AreEqual(168U, catalog[1407].Objectives[10].Conversations[0].NpcPackageId);
            CollectionAssert.AreEquivalent(
                new uint[] { 1, 3, 4, 5, 6, 7, 8, 20, 21, 22, 23, 24, 25, 40, 41, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 58 },
                new List<uint>(catalog[1449].Objectives.Keys));
            Assert.AreEqual(12789U, catalog[1449].Objectives[1].ClientCounterTextIds[0]);
            Assert.AreEqual(13685U, catalog[1449].Objectives[55].ClientCounterTextIds[0]);
        }

        [TestMethod]
        public void DefinitionCollectionsAreDefensiveAndOperationalRequiresCompleteMetadata()
        {
            var sourceObjectives = new[]
            {
                new MissionObjectiveDefinition(
                    1, 10, 11, Array.Empty<uint?>(), 0,
                    MissionObjectiveState.Incomplete, true,
                    new Dictionary<uint, MissionObjectiveCounterDefinition>(),
                    new Dictionary<uint, MissionObjectiveItemCounterDefinition>(),
                    Array.Empty<MissionObjectiveConversation>(),
                    Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<MissionIndicator>())
            };
            var mission = new Mission(
                1, "test", 9, 7, 8, 5, 1, 2, true, false, sourceObjectives, true);

            sourceObjectives[0] = null;

            Assert.IsTrue(mission.IsOperational);
            Assert.IsNotNull(mission.Objectives[1]);
            Assert.ThrowsExactly<NotSupportedException>(() =>
                ((IDictionary<uint, MissionObjectiveDefinition>)mission.Objectives).Add(2, null));
        }
    }
}
