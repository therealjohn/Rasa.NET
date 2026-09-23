using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions.Content
{
    using Rasa.Game.Missions.Content;
    using Rasa.Missions.Scenes;
    using Rasa.Structures.World;

    [TestClass]
    public class MissionPackTests
    {
        [TestMethod]
        public void TypedPackRoundTripsWithoutEfNavigationOrProviderDetails()
        {
            var pack = new MissionPackDocument
            {
                Release = "test", Definition = new MissionContentDefinitionEntry
                { MissionId = 321, ContentRevision = "v1", Comment = "Example" },
                Scene = new MissionSceneDocument
                {
                    Script = "data.sequence",
                    Actors = new() { ["guide"] = new("guide", SceneActorKind.PublicSpawn, 77) },
                    Sequences = new() { [0] = new SceneSequenceDocument
                        { World = new() { new EnsureActorIntent("guide", "guide") } } }
                }
            };
            var json = MissionPackCodec.Write(pack);
            Assert.IsFalse(json.Contains("\"transition\":", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("DbContext", StringComparison.Ordinal));
            var read = MissionPackCodec.Read(json);
            Assert.AreEqual(321U, read.Definition.MissionId);
            Assert.IsInstanceOfType<EnsureActorIntent>(read.Scene.Sequences[0].World[0]);
        }

        [TestMethod]
        public void UnknownFieldsCannotSilentlyChangeAnAuthoredContract()
        {
            Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
                MissionPackCodec.Read("{\"schemaVersion\":1,\"relase\":\"typo\"}"));
        }

        [TestMethod]
        public void SyntheticContentAndUnknownClientBindingsCannotBePublished()
        {
            var pack = new MissionPackDocument
            {
                Release = "test", Enabled = true, Synthetic = true,
                Definition = new MissionContentDefinitionEntry { MissionId = 999, ContentRevision = "v1" }
            };
            var errors = MissionPackValidation.ValidateBindings(new[] { pack }, new ClientBindingManifest());
            Assert.IsTrue(errors.Exists(message => message.Contains("synthetic", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(errors.Exists(message => message.Contains("client", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
