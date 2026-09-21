using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rasa.Structures.World
{
    public enum MissionActionKind : byte
    {
        /// <summary>Uses target_objective_id.</summary>
        RevealObjective = 1,

        /// <summary>Uses target_objective_id and objective_state.</summary>
        ActivateObjective = 2,

        /// <summary>Uses target_objective_id and objective_state.</summary>
        CompleteObjective = 3,

        /// <summary>Uses reward_id.</summary>
        GrantReward = 4,

        /// <summary>Uses scenario_id.</summary>
        StartScenario = 5,

        /// <summary>Uses spawn_group_id.</summary>
        ActivateSpawnGroup = 6,

        /// <summary>Uses indicator_id.</summary>
        ShowIndicator = 7,

        /// <summary>Uses player_flag_id and player_flag_value.</summary>
        SetPlayerFlag = 8,

        /// <summary>
        /// Uses npc_package_id, which for this kind holds a client npcgreetinglanguage text id,
        /// not an actual NPC package - this proactively forces the conversation window open via
        /// ForceConverse (Manifestation.Recv_ForceConverse), which needs no NPC entity at all.
        /// player_flag_id is set but unused; it exists only to satisfy this table's "one row per
        /// kind, same required-column shape" check constraint. Unlike Completion-type conversation
        /// triggers, this is not a gate the player reaches by clicking an NPC first - it rides
        /// along with whatever progress rule already gates this transition.
        /// </summary>
        ShowAmbientConversation = 9
    }

    /// <summary>
    /// Only the columns documented on <see cref="Kind"/> are populated for a row. The remaining
    /// typed columns stay null.
    /// </summary>
    [Table(TableName)]
    public class MissionActionEntry
    {
        public const string TableName = "mission_action";

        [Column("mission_id")]
        [Required]
        public uint MissionId { get; set; }

        [Column("content_revision", TypeName = "varchar(32)")]
        [Required]
        public string ContentRevision { get; set; } = string.Empty;

        [Column("objective_id")]
        [Required]
        public uint ObjectiveId { get; set; }

        [Column("transition_id")]
        [Required]
        public uint TransitionId { get; set; }

        [Column("action_id")]
        [Required]
        public uint ActionId { get; set; }

        [Column("requirement")]
        [Required]
        public MissionContentRequirement Requirement { get; set; } =
            MissionContentRequirement.Required;

        [Column("kind")]
        [Required]
        public MissionActionKind Kind { get; set; }

        [Column("sequence")]
        [Required]
        public uint Sequence { get; set; }

        [Column("target_objective_id")]
        public uint? TargetObjectiveId { get; set; }

        [Column("objective_state")]
        public byte? ObjectiveState { get; set; }

        [Column("reward_id")]
        public uint? RewardId { get; set; }

        [Column("spawn_group_id")]
        public uint? SpawnGroupId { get; set; }

        [Column("scenario_id")]
        public uint? ScenarioId { get; set; }

        [Column("indicator_id")]
        public uint? IndicatorId { get; set; }

        [Column("player_flag_id")]
        public uint? PlayerFlagId { get; set; }

        [Column("player_flag_value")]
        public uint? PlayerFlagValue { get; set; }

        [Column("npc_package_id")]
        public uint? NpcPackageId { get; set; }

        [Column("comment", TypeName = "varchar(64)")]
        [Required]
        public string Comment { get; set; } = string.Empty;

        public MissionObjectiveTransitionEntry Transition { get; set; }
    }
}
