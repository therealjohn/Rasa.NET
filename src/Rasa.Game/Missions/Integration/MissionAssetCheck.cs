using System;
using System.IO;
using System.Linq;
using Rasa.Game.Missions.Content;
using Rasa.Managers;

namespace Rasa.Game.Missions.Integration
{
    internal static class MissionAssetCheck
    {
        internal static int Run(string applicationDirectory, string workingDirectory)
        {
            var navigation = NavMeshManager.ResolveDirectory(null, workingDirectory, applicationDirectory);
            var packs = Path.Combine(applicationDirectory, "missions", "bootcamp");
            if (!Directory.Exists(navigation) || !Directory.Exists(packs))
                throw new DirectoryNotFoundException("Published navigation or mission assets are missing.");
            var meshes = Directory.GetFiles(navigation, "*.nav").Length;
            var manifest = Path.Combine(packs, "client-bindings.json");
            if (meshes == 0 || !File.Exists(manifest))
                throw new InvalidOperationException("Published assets have no navigation meshes or client manifest.");
            var documents = Directory.GetFiles(packs, "*.json")
                .Where(path => path != manifest).Select(path => MissionPackCodec.Read(File.ReadAllText(path))).ToArray();
            var bindings = System.Text.Json.JsonSerializer.Deserialize<ClientBindingManifest>(
                File.ReadAllText(manifest), MissionPackCodec.Options);
            var errors = MissionPackValidation.ValidateBindings(documents, bindings);
            if (errors.Count != 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            Console.WriteLine($"Navigation: {navigation} ({meshes} maps)");
            Console.WriteLine($"Mission assets: {packs} ({documents.Length} packs, client manifest present)");
            return 0;
        }
    }
}
