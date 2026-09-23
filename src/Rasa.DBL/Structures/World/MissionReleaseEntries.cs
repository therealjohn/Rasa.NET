using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rasa.Structures.World
{
    [Table("mission_active_release")]
    public sealed class MissionActiveReleaseEntry
    {
        [Key, Column("id")] public int Id { get; set; } = 1;
        [Required, Column("release_name", TypeName = "varchar(32)")] public string ReleaseName { get; set; } = "";
        [Required, Column("manifest_hash", TypeName = "varchar(64)")] public string ManifestHash { get; set; } = "";
        [ConcurrencyCheck, Column("version")] public long Version { get; set; }
    }

    [Table("mission_release_member")]
    public sealed class MissionReleaseMemberEntry
    {
        [Required, Column("release_name", TypeName = "varchar(32)")] public string ReleaseName { get; set; } = "";
        [Column("mission_id")] public uint MissionId { get; set; }
        [Required, Column("content_revision", TypeName = "varchar(32)")] public string ContentRevision { get; set; } = "";
        [Column("enabled")] public bool Enabled { get; set; }
    }

    [Table("mission_scene_binding")]
    public sealed class MissionSceneBindingEntry
    {
        [Column("mission_id")] public uint MissionId { get; set; }
        [Required, Column("content_revision", TypeName = "varchar(32)")] public string ContentRevision { get; set; } = "";
        [Column("script_key", TypeName = "varchar(64)")] public string ScriptKey { get; set; }
        [Column("state_version")] public int StateVersion { get; set; } = 1;
        [Required, Column("bindings", TypeName = "text")] public string Bindings { get; set; } = "{}";
    }

    [Table("mission_experience_binding")]
    public sealed class MissionExperienceBindingEntry
    {
        [Required, Column("release_name", TypeName = "varchar(32)")] public string ReleaseName { get; set; } = "";
        [Required, Column("experience_key", TypeName = "varchar(64)")] public string ExperienceKey { get; set; } = "";
        [Column("map_context_id")] public uint MapContextId { get; set; }
        [Required, Column("bindings", TypeName = "text")] public string Bindings { get; set; } = "{}";
    }
}
