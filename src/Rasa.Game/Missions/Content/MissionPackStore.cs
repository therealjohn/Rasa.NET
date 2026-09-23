using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Game.Missions.Content
{
    using Context.World;
    using Managers;
    using Repositories.World;
    using Structures.World;

    public sealed class MissionPackStore
    {
        private readonly WorldContext _context;
        public MissionPackStore(WorldContext context) => _context = context;

        public MissionPackDocument Export(uint missionId, string revision, string release)
        {
            var pack = new MissionPackDocument
            {
                Release = release, Enabled = true,
                Definition = _context.MissionContentDefinitionEntries.AsNoTracking()
                    .Single(entry => entry.MissionId == missionId && entry.ContentRevision == revision)
            };
            pack.Prerequisites = Rows<MissionPrerequisiteEntry>(missionId, revision);
            pack.Objectives = Rows<MissionObjectiveDefinitionEntry>(missionId, revision);
            pack.Transitions = Rows<MissionObjectiveTransitionEntry>(missionId, revision);
            pack.Triggers = Rows<MissionTriggerEntry>(missionId, revision);
            pack.Actions = Rows<MissionActionEntry>(missionId, revision);
            pack.Rewards = Rows<MissionRewardDefinitionEntry>(missionId, revision);
            pack.RewardItems = Rows<MissionRewardItemEntry>(missionId, revision);
            pack.Indicators = Rows<MissionIndicatorEntry>(missionId, revision);
            pack.Areas = Rows<MissionAreaEntry>(missionId, revision);
            pack.SpawnGroups = Rows<MissionSpawnGroupEntry>(missionId, revision);
            pack.Spawns = Rows<MissionSpawnEntry>(missionId, revision);
            pack.Scenarios = Rows<MissionScenarioEntry>(missionId, revision);
            pack.ScenarioSteps = Rows<MissionScenarioStepEntry>(missionId, revision);
            pack.Evidence = Rows<MissionEvidenceEntry>(missionId, revision);
            var scene = _context.Set<MissionSceneBindingEntry>().AsNoTracking()
                .SingleOrDefault(entry => entry.MissionId == missionId && entry.ContentRevision == revision);
            if (scene != null)
                pack.Scene = JsonSerializer.Deserialize<MissionSceneDocument>(scene.Bindings, MissionPackCodec.Options)
                    ?? throw new InvalidOperationException($"Mission {missionId} has an invalid stored scene binding.");
            return pack;
        }

        public List<string> Validate(IReadOnlyList<MissionPackDocument> packs, ClientBindingManifest client)
        {
            var errors = MissionPackValidation.ValidateBindings(packs, client);
            if (errors.Count != 0)
                return errors;
            var active = packs.Where(pack => pack.Enabled && pack.Experience == null).ToArray();
            if (active.Length == 0)
                return errors;
            var repository = new PackRepository(active);
            var snapshot = new MissionContentLoader().Load(repository);
            errors.AddRange(new MissionContentValidator().ValidatePublication(snapshot, _context)
                .Diagnostics.Select(diagnostic => diagnostic.ToOperatorMessage()));
            foreach (var pack in active.Where(pack => pack.Scene != null))
            {
                foreach (var actor in pack.Scene.Actors.Values)
                    if (actor.Kind == global::Rasa.Missions.Scenes.SceneActorKind.PublicSpawn &&
                        !_context.SpawnPoolEntries.Any(spawn => spawn.Id == actor.TemplateId))
                        errors.Add($"Mission {pack.Definition.MissionId}: public spawn {actor.TemplateId} does not exist.");
            }
            return errors;
        }

        public IReadOnlyList<string> Diff(IReadOnlyList<MissionPackDocument> packs)
        {
            var changes = new List<string>();
            foreach (var pack in packs.OrderBy(pack => pack.Definition?.MissionId ?? 0))
            {
                if (pack.Experience != null)
                {
                    changes.Add($"BIND private experience {pack.Experience.Key} in release {pack.Release}.");
                    continue;
                }
                var definition = pack.Definition;
                if (!_context.MissionContentDefinitionEntries.Any(entry =>
                    entry.MissionId == definition.MissionId && entry.ContentRevision == definition.ContentRevision))
                    changes.Add($"ADD mission {definition.MissionId}@{definition.ContentRevision} ({pack.Rows().Count()} rows).");
                else if (CanonicalRows(pack) != CanonicalRows(Export(definition.MissionId, definition.ContentRevision, pack.Release)))
                    changes.Add($"CONFLICT immutable mission {definition.MissionId}@{definition.ContentRevision}; author a new revision.");
                else
                    changes.Add($"KEEP mission {definition.MissionId}@{definition.ContentRevision}.");
                changes.Add($"{(pack.Enabled ? "ACTIVATE" : "DISABLE")} {definition.MissionId} in release {pack.Release}.");
            }
            return changes;
        }

        public string Publish(IReadOnlyList<MissionPackDocument> packs, ClientBindingManifest client)
        {
            var errors = Validate(packs, client);
            if (!packs.Any(pack => pack.Enabled && pack.Experience == null))
                errors.Add("A publication must include a nonempty active release, not only inactive examples.");
            if (errors.Count != 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            var release = packs[0].Release;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                packs.OrderBy(pack => pack.Definition?.MissionId ?? 0).Select(MissionPackCodec.Write))))).ToLowerInvariant();
            using var transaction = _context.Database.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var oldMembers = _context.Set<MissionReleaseMemberEntry>().Where(entry => entry.ReleaseName == release).ToArray();
                var oldExperiences = _context.Set<MissionExperienceBindingEntry>()
                    .Where(entry => entry.ReleaseName == release).ToArray();
                var experiences = packs.Where(pack => pack.Enabled && pack.Experience != null)
                    .ToDictionary(pack => pack.Experience.Key,
                        pack => JsonSerializer.Serialize(pack.Experience, MissionPackCodec.Options));
                var missions = packs.Where(pack => pack.Experience == null).ToArray();
                if (oldMembers.Length > 0 && (oldMembers.Length != missions.Length ||
                    oldMembers.Any(member => !missions.Any(pack => pack.Definition.MissionId == member.MissionId &&
                        pack.Definition.ContentRevision == member.ContentRevision && pack.Enabled == member.Enabled))))
                    throw new InvalidOperationException($"Release {release} is immutable; publish a new release name.");
                if (oldMembers.Length > 0 && (oldExperiences.Length != experiences.Count ||
                    oldExperiences.Any(existing => !experiences.TryGetValue(existing.ExperienceKey, out var json) ||
                        json != existing.Bindings)))
                    throw new InvalidOperationException($"Experience set for release {release} is immutable.");
                foreach (var pack in packs)
                {
                    if (pack.Experience != null)
                    {
                        if (!pack.Enabled)
                            continue;
                        var json = JsonSerializer.Serialize(pack.Experience, MissionPackCodec.Options);
                        var existing = _context.Set<MissionExperienceBindingEntry>().Find(release, pack.Experience.Key);
                        if (existing != null && existing.Bindings != json)
                            throw new InvalidOperationException($"Experience {pack.Experience.Key} is immutable in release {release}.");
                        if (existing == null)
                            _context.Add(new MissionExperienceBindingEntry
                            {
                                ReleaseName = release, ExperienceKey = pack.Experience.Key,
                                MapContextId = pack.Experience.MapContextId, Bindings = json
                            });
                        continue;
                    }
                    var definition = pack.Definition;
                    var exists = _context.MissionContentDefinitionEntries.Any(entry =>
                        entry.MissionId == definition.MissionId && entry.ContentRevision == definition.ContentRevision);
                    if (exists && CanonicalRows(pack) != CanonicalRows(Export(definition.MissionId, definition.ContentRevision, release)))
                        throw new InvalidOperationException($"Mission {definition.MissionId}@{definition.ContentRevision} is immutable.");
                    var storedScene = _context.Set<MissionSceneBindingEntry>().Find(definition.MissionId, definition.ContentRevision);
                    var sceneJson = pack.Scene == null ? null : JsonSerializer.Serialize(pack.Scene, MissionPackCodec.Options);
                    var publishedRevision = exists && _context.Set<MissionReleaseMemberEntry>().Any(member =>
                        member.MissionId == definition.MissionId && member.ContentRevision == definition.ContentRevision);
                    if ((publishedRevision || storedScene != null) && storedScene?.Bindings != sceneJson)
                        throw new InvalidOperationException($"{(publishedRevision ? "Published scene" : "Scene")} binding " +
                            $"{definition.MissionId}@{definition.ContentRevision} is immutable, including presence.");
                    if (!exists)
                        _context.AddRange(pack.Rows());
                    if (oldMembers.Length == 0)
                        _context.Add(new MissionReleaseMemberEntry
                        {
                            ReleaseName = release, MissionId = definition.MissionId,
                            ContentRevision = definition.ContentRevision, Enabled = pack.Enabled
                        });
                    if (pack.Scene != null)
                    {
                        var json = sceneJson;
                        var scene = storedScene;
                        if (scene != null && scene.Bindings != json)
                            throw new InvalidOperationException($"Scene binding {definition.MissionId}@{definition.ContentRevision} is immutable.");
                        if (scene == null)
                            _context.Add(new MissionSceneBindingEntry
                            {
                                MissionId = definition.MissionId, ContentRevision = definition.ContentRevision,
                                ScriptKey = pack.Scene.Script, StateVersion = pack.Scene.StateVersion, Bindings = json
                            });
                    }
                }
                var head = _context.Set<MissionActiveReleaseEntry>().Find(1);
                if (head == null)
                    _context.Add(new MissionActiveReleaseEntry { ReleaseName = release, ManifestHash = hash });
                else
                {
                    if (head.ReleaseName == release && head.ManifestHash != hash)
                        throw new InvalidOperationException($"Release {release} already has another manifest hash.");
                    head.ReleaseName = release; head.ManifestHash = hash; head.Version++;
                }
                _context.SaveChanges();
                transaction.Commit();
                return hash;
            }
            catch
            {
                _context.ChangeTracker.Clear();
                throw;
            }
        }

        private List<T> Rows<T>(uint missionId, string revision) where T : class =>
            _context.Set<T>().AsNoTracking()
                .Where(row => EF.Property<uint>(row, "MissionId") == missionId &&
                    EF.Property<string>(row, "ContentRevision") == revision).ToList()
                .OrderBy(row => JsonSerializer.Serialize(row, MissionPackCodec.Options), StringComparer.Ordinal).ToList();

        private static string CanonicalRows(MissionPackDocument pack) => string.Join("\n",
            pack.Rows().Select(row => row.GetType().Name + ":" +
                JsonSerializer.Serialize(row, row.GetType(), MissionPackCodec.Options)).OrderBy(row => row, StringComparer.Ordinal));

        internal sealed class PackRepository : IMissionContentRepository
        {
            private readonly IReadOnlyList<MissionPackDocument> _packs;
            internal PackRepository(IReadOnlyList<MissionPackDocument> packs) => _packs = packs;
            public List<MissionContentDefinitionEntry> GetDefinitions() => _packs.Select(pack => pack.Definition).ToList();
            public List<MissionPrerequisiteEntry> GetPrerequisites() => _packs.SelectMany(pack => pack.Prerequisites).ToList();
            public List<MissionObjectiveDefinitionEntry> GetObjectives() => _packs.SelectMany(pack => pack.Objectives).ToList();
            public List<MissionObjectiveTransitionEntry> GetTransitions() => _packs.SelectMany(pack => pack.Transitions).ToList();
            public List<MissionTriggerEntry> GetTriggers() => _packs.SelectMany(pack => pack.Triggers).ToList();
            public List<MissionActionEntry> GetActions() => _packs.SelectMany(pack => pack.Actions).ToList();
            public List<MissionRewardDefinitionEntry> GetRewards() => _packs.SelectMany(pack => pack.Rewards).ToList();
            public List<MissionRewardItemEntry> GetRewardItems() => _packs.SelectMany(pack => pack.RewardItems).ToList();
            public List<MissionIndicatorEntry> GetIndicators() => _packs.SelectMany(pack => pack.Indicators).ToList();
            public List<MissionAreaEntry> GetAreas() => _packs.SelectMany(pack => pack.Areas).ToList();
            public List<MissionSpawnGroupEntry> GetSpawnGroups() => _packs.SelectMany(pack => pack.SpawnGroups).ToList();
            public List<MissionSpawnEntry> GetSpawns() => _packs.SelectMany(pack => pack.Spawns).ToList();
            public List<MissionScenarioEntry> GetScenarios() => _packs.SelectMany(pack => pack.Scenarios).ToList();
            public List<MissionScenarioStepEntry> GetScenarioSteps() => _packs.SelectMany(pack => pack.ScenarioSteps).ToList();
            public List<MissionEvidenceEntry> GetEvidence() => _packs.SelectMany(pack => pack.Evidence).ToList();
        }
    }
}
