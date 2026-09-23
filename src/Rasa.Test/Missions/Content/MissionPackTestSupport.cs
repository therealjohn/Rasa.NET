using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Rasa.Test.Missions.Content
{
    using Rasa.Context.World;
    using Rasa.Game.Missions.Content;

    internal static class MissionPackTestSupport
    {
        internal static MissionExperienceDocument ReadExperience() =>
            MissionPackCodec.Read(File.ReadAllText(Path.Combine(FindBootcampDirectory(), "experience.json"))).Experience!;

        internal static MissionPackDocument[] ReadBootcampPacks() =>
            Directory.GetFiles(FindBootcampDirectory(), "*.json")
                .Where(path => Path.GetFileName(path) != "client-bindings.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => MissionPackCodec.Read(File.ReadAllText(path))).ToArray();

        internal static ClientBindingManifest ReadClientBindings() =>
            JsonSerializer.Deserialize<ClientBindingManifest>(
                File.ReadAllText(Path.Combine(FindBootcampDirectory(), "client-bindings.json")), MissionPackCodec.Options)!;

        internal static void PublishBootcamp(WorldContext context,
            Action<IReadOnlyList<MissionPackDocument>> configure = null)
        {
            var packs = ReadBootcampPacks();
            configure?.Invoke(packs);
            new MissionPackStore(context).Publish(packs, ReadClientBindings());
        }

        private static string FindBootcampDirectory()
        {
            foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "content", "missions", "bootcamp");
                    if (File.Exists(Path.Combine(candidate, "client-bindings.json")))
                        return candidate;
                }
            throw new DirectoryNotFoundException("Checked-in Bootcamp mission packs were not found.");
        }
    }
}
