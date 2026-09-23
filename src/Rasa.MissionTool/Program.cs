using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rasa.Configuration;
using Rasa.Configuration.ConnectionStrings;
using Rasa.Configuration.ContextSetup;
using Rasa.Context.World;
using Rasa.Game.Missions.Content;
using Rasa.Services.DbContext;

namespace Rasa.MissionTool
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Mission content operation failed: {error.Message}");
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0)
                throw new ArgumentException(
                    "Usage: export|validate|diff|publish --database <sqlite-path> --directory <packs> " +
                    "[--client-manifest <path>] [--initialize-empty] [--revision <revision>] [--release <name>] [--missions <ids>]");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index++)
            {
                if (args[index] is "--initialize-empty" or "--bootcamp-scenes")
                    options.Add(args[index], "true");
                else if (args[index].StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length)
                    options.Add(args[index], args[++index]);
                else
                    throw new ArgumentException($"Invalid argument {args[index]}.");
            }
            var database = Path.GetFullPath(Required("--database"));
            var directory = Path.GetFullPath(Required("--directory"));
            if (options.ContainsKey("--initialize-empty") && (File.Exists(database) || File.Exists(database + ".db")))
                throw new InvalidOperationException("--initialize-empty refuses an existing database.");
            if (!options.ContainsKey("--initialize-empty") && !File.Exists(database) && !File.Exists(database + ".db"))
                throw new FileNotFoundException("The explicitly selected World database does not exist.", database);
            var config = Options.Create(new DatabaseConfiguration
            {
                Provider = "Sqlite", World = new DatabaseConnectionConfiguration { Database = database }
            });
            using var context = new SqliteWorldContext(config,
                new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory()), new SqliteDbContextPropertyModifier());
            if (options.ContainsKey("--initialize-empty"))
                context.Database.Migrate();
            var store = new MissionPackStore(context);
            if (args[0] == "export")
            {
                Directory.CreateDirectory(directory);
                var packs = Required("--missions").Split(',').Select(uint.Parse)
                    .Select(id => store.Export(id, Required("--revision"), Required("--release"))).ToList();
                if (options.ContainsKey("--bootcamp-scenes"))
                    packs.Add(BootcampPackMigration.Upgrade(packs));
                foreach (var pack in packs)
                    File.WriteAllText(Path.Combine(directory, pack.Experience != null ? "experience.json" :
                        $"{pack.Definition.MissionId}.json"), MissionPackCodec.Write(pack) + Environment.NewLine);
                var manifest = new ClientBindingManifest
                {
                    Source = "Reviewed existing World/client bindings; export is not client-ID discovery.",
                    Missions = packs.Where(pack => pack.Experience == null).ToDictionary(pack => pack.Definition.MissionId, pack => new ClientMissionBinding
                    {
                        NameTextId = pack.Definition.ClientNameTextId,
                        Objectives = pack.Objectives.ToDictionary(objective => objective.ObjectiveId,
                            objective => new ClientObjectiveBinding(objective.ClientNameTextId, objective.ClientBodyTextId))
                    })
                };
                File.WriteAllText(Path.Combine(directory, "client-bindings.json"),
                    JsonSerializer.Serialize(manifest, MissionPackCodec.Options) + Environment.NewLine);
                Console.WriteLine($"Exported {packs.Count} effective mission/experience packs from the selected World database.");
                return 0;
            }
            var documents = Directory.GetFiles(directory, "*.json").Where(path =>
                !Path.GetFileName(path).Equals("client-bindings.json", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal).Select(path => MissionPackCodec.Read(File.ReadAllText(path))).ToArray();
            var clientPath = options.GetValueOrDefault("--client-manifest", Path.Combine(directory, "client-bindings.json"));
            var client = JsonSerializer.Deserialize<ClientBindingManifest>(File.ReadAllText(clientPath), MissionPackCodec.Options)
                ?? throw new InvalidOperationException("Client binding manifest is empty.");
            var errors = store.Validate(documents, client);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            switch (args[0])
            {
                case "validate":
                    Console.WriteLine($"Validated {documents.Length} packs; no database changes.");
                    break;
                case "diff":
                    foreach (var change in store.Diff(documents))
                        Console.WriteLine(change);
                    break;
                case "publish":
                    Console.WriteLine($"Published release {documents[0].Release}, manifest {store.Publish(documents, client)}.");
                    break;
                default:
                    throw new ArgumentException($"Unknown operation {args[0]}.");
            }
            return 0;

            string Required(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException($"Missing {key}.");
        }
    }
}
