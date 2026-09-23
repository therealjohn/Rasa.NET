using System;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Content
{
    using Rasa.Game.Missions.Content;
    using Rasa.Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class MissionReleaseImmutabilityTests
    {
        [TestMethod]
        [DataRow("add")]
        [DataRow("remove")]
        [DataRow("change")]
        public void PublishedScenePresenceAndContentCannotChangeInAnotherRelease(string change)
        {
            using var harness = BootcampRuntimeTestHarness.Create(configurePacks: packs =>
            {
                if (change == "add")
                    packs.Single(pack => pack.Definition?.MissionId == 1990).Scene = null;
            });
            var store = new MissionPackStore(harness.WorldContext);
            var original = store.Export(1990, "deployment_11", "bootcamp-modular-v1");
            var packs = MissionPackTestSupport.ReadBootcampPacks();
            foreach (var pack in packs)
                pack.Release = "scene-change";
            var changed = packs.Single(pack => pack.Definition?.MissionId == 1990);
            if (change == "remove")
                changed.Scene = null;
            else if (change == "change")
                changed.Scene.Recovery = "RestartAttempt";

            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                store.Publish(packs, MissionPackTestSupport.ReadClientBindings()));
            StringAssert.Contains(error.Message, "Published scene binding");
            Assert.AreEqual("bootcamp-modular-v1", ActiveRelease(harness));
            Assert.AreEqual(JsonSerializer.Serialize(original.Scene, MissionPackCodec.Options),
                JsonSerializer.Serialize(store.Export(1990, "deployment_11", "bootcamp-modular-v1").Scene, MissionPackCodec.Options));
            Assert.IsFalse(harness.WorldContext.Set<MissionReleaseMemberEntry>().Any(entry => entry.ReleaseName == "scene-change"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void IdenticalPublishedSceneIncludingAbsenceCanBeSelectedByAnotherRelease(bool hasScene)
        {
            using var harness = BootcampRuntimeTestHarness.Create(configurePacks: packs =>
            {
                if (!hasScene)
                    packs.Single(pack => pack.Definition?.MissionId == 1990).Scene = null;
            });
            var packs = MissionPackTestSupport.ReadBootcampPacks();
            foreach (var pack in packs)
                pack.Release = "same-scene";
            if (!hasScene)
                packs.Single(pack => pack.Definition?.MissionId == 1990).Scene = null;
            new MissionPackStore(harness.WorldContext).Publish(packs, MissionPackTestSupport.ReadClientBindings());
            Assert.AreEqual("same-scene", ActiveRelease(harness));
        }

        [TestMethod]
        public void UnpublishedNormalizedRowsCanReceiveTheirFirstSceneBinding()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            RemovePublicationMetadata(harness, keepScene: false);
            var packs = MissionPackTestSupport.ReadBootcampPacks();
            new MissionPackStore(harness.WorldContext).Publish(packs, MissionPackTestSupport.ReadClientBindings());
            Assert.AreEqual(5, harness.WorldContext.Set<MissionSceneBindingEntry>().Count());
            Assert.AreEqual("bootcamp-modular-v1", ActiveRelease(harness));
        }

        [TestMethod]
        public void AnUnpublishedExistingSceneBindingCannotBeSilentlyRemoved()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            RemovePublicationMetadata(harness, keepScene: true);
            var packs = MissionPackTestSupport.ReadBootcampPacks();
            packs.Single(pack => pack.Definition?.MissionId == 1990).Scene = null;
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                new MissionPackStore(harness.WorldContext).Publish(packs, MissionPackTestSupport.ReadClientBindings()));
            StringAssert.Contains(error.Message, "immutable");
            Assert.AreEqual(0, harness.WorldContext.Set<MissionReleaseMemberEntry>().Count());
        }

        [TestMethod]
        [DataRow("remove")]
        [DataRow("replace")]
        [DataRow("change")]
        [DataRow("disable")]
        public void ReactivatingAnInactiveReleaseCannotChangeItsPersistedExperienceSet(string change)
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var store = new MissionPackStore(harness.WorldContext);
            var next = MissionPackTestSupport.ReadBootcampPacks();
            foreach (var pack in next)
                pack.Release = "other-release";
            store.Publish(next, MissionPackTestSupport.ReadClientBindings());
            var originalBinding = harness.WorldContext.Set<MissionExperienceBindingEntry>().AsNoTracking()
                .Single(entry => entry.ReleaseName == "bootcamp-modular-v1").Bindings;
            var packs = MissionPackTestSupport.ReadBootcampPacks();
            var experience = packs.Single(pack => pack.Experience != null);
            switch (change)
            {
                case "remove": packs = packs.Where(pack => pack.Experience == null).ToArray(); break;
                case "replace": experience.Experience.Key = "different-experience"; break;
                case "change": experience.Experience.Scene.Recovery = "Fail"; break;
                case "disable": experience.Enabled = false; break;
            }
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                store.Publish(packs, MissionPackTestSupport.ReadClientBindings()));
            StringAssert.Contains(error.Message, "Experience set");
            Assert.AreEqual("other-release", ActiveRelease(harness));
            Assert.AreEqual(originalBinding, harness.WorldContext.Set<MissionExperienceBindingEntry>().AsNoTracking()
                .Single(entry => entry.ReleaseName == "bootcamp-modular-v1").Bindings);
        }

        [TestMethod]
        public void ReactivatingAReleaseWithoutExperiencesCannotAddOne()
        {
            using var harness = BootcampRuntimeTestHarness.Create(configurePacks: packs =>
                packs.Single(pack => pack.Experience != null).Enabled = false);
            var store = new MissionPackStore(harness.WorldContext);
            var next = MissionPackTestSupport.ReadBootcampPacks();
            foreach (var pack in next)
                pack.Release = "with-experience";
            store.Publish(next, MissionPackTestSupport.ReadClientBindings());

            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                store.Publish(MissionPackTestSupport.ReadBootcampPacks(), MissionPackTestSupport.ReadClientBindings()));
            StringAssert.Contains(error.Message, "Experience set");
            Assert.AreEqual("with-experience", ActiveRelease(harness));
            Assert.IsFalse(harness.WorldContext.Set<MissionExperienceBindingEntry>()
                .Any(entry => entry.ReleaseName == "bootcamp-modular-v1"));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DisabledExperiencesAreNeitherPersistedNorBoundAtStartup(bool synthetic)
        {
            using var harness = BootcampRuntimeTestHarness.Create(configurePacks: packs =>
            {
                var experience = packs.Single(pack => pack.Experience != null);
                experience.Enabled = false;
                experience.Synthetic = synthetic;
            });
            Assert.AreEqual(0, harness.WorldContext.Set<MissionExperienceBindingEntry>().Count());
            Assert.IsFalse(harness.Manager.Scenes.OwnsExperience(BootcampRuntimeTestHarness.BootcampMapContextId));
        }

        [TestMethod]
        public void SyntheticExperienceCannotBeActivated()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var packs = MissionPackTestSupport.ReadBootcampPacks();
            foreach (var pack in packs)
                pack.Release = "synthetic-experience";
            packs.Single(pack => pack.Experience != null).Synthetic = true;
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                new MissionPackStore(harness.WorldContext).Publish(packs, MissionPackTestSupport.ReadClientBindings()));
            StringAssert.Contains(error.Message, "synthetic examples cannot be activated");
            Assert.AreEqual("bootcamp-modular-v1", ActiveRelease(harness));
            Assert.IsFalse(harness.WorldContext.Set<MissionExperienceBindingEntry>()
                .Any(entry => entry.ReleaseName == "synthetic-experience"));
        }

        private static string ActiveRelease(BootcampRuntimeTestHarness.Harness harness) =>
            harness.WorldContext.Set<MissionActiveReleaseEntry>().AsNoTracking().Single().ReleaseName;

        private static void RemovePublicationMetadata(BootcampRuntimeTestHarness.Harness harness, bool keepScene)
        {
            var world = harness.WorldContext;
            world.RemoveRange(world.Set<MissionActiveReleaseEntry>().ToArray());
            world.RemoveRange(world.Set<MissionReleaseMemberEntry>().ToArray());
            world.RemoveRange(world.Set<MissionExperienceBindingEntry>().ToArray());
            if (!keepScene)
                world.RemoveRange(world.Set<MissionSceneBindingEntry>().ToArray());
            world.SaveChanges();
        }
    }
}
