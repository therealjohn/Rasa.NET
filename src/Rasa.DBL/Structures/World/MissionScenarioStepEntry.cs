using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rasa.Structures.World
{
    public enum MissionScenarioStepKind : byte
    {
        Narrative = 1,
        Spawn = 2,
        Trigger = 3,
        Cleanup = 4
    }

    [Table(TableName)]
    public class MissionScenarioStepEntry
    {
        public const string TableName = "mission_scenario_step";

        [Column("mission_id")]
        [Required]
        public uint MissionId { get; set; }

        [Column("content_revision", TypeName = "varchar(32)")]
        [Required]
        public string ContentRevision { get; set; } = string.Empty;

        [Column("scenario_id")]
        [Required]
        public uint ScenarioId { get; set; }

        [Column("step_id")]
        [Required]
        public uint StepId { get; set; }

        [Column("requirement")]
        [Required]
        public MissionContentRequirement Requirement { get; set; } =
            MissionContentRequirement.Required;

        [Column("kind")]
        [Required]
        public MissionScenarioStepKind Kind { get; set; }

        [Column("sequence")]
        [Required]
        public uint Sequence { get; set; }

        [Column("comment", TypeName = "varchar(64)")]
        [Required]
        public string Comment { get; set; } = string.Empty;

        public MissionScenarioEntry Scenario { get; set; }
    }
}
