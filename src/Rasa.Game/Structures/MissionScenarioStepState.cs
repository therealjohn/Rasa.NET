using System;
using System.Globalization;

namespace Rasa.Structures
{
    internal enum MissionScenarioStepStateKind
    {
        CompletedStep,
        ScheduledScenario
    }

    internal readonly struct MissionScenarioStepState
    {
        private const string StepPrefix = "scenario:";
        private const string StepSeparator = ":step:";
        private const string SchedulePrefix = "schedule:";

        public MissionScenarioStepStateKind Kind { get; }
        public string StepKey { get; }
        public uint ScenarioId { get; }
        public uint StepId { get; }
        public uint? TargetScenarioId { get; }
        public DateTime? DueAtUtc { get; }

        private MissionScenarioStepState(
            MissionScenarioStepStateKind kind,
            string stepKey,
            uint scenarioId,
            uint stepId,
            uint? targetScenarioId = null,
            DateTime? dueAtUtc = null)
        {
            Kind = kind;
            StepKey = stepKey ?? string.Empty;
            ScenarioId = scenarioId;
            StepId = stepId;
            TargetScenarioId = targetScenarioId;
            DueAtUtc = dueAtUtc;
        }

        internal static string CreateCompletedKey(uint scenarioId, uint stepId) =>
            $"{StepPrefix}{scenarioId}{StepSeparator}{stepId}";

        internal static string CreateScheduledKey(
            uint scenarioId,
            uint stepId,
            uint targetScenarioId,
            DateTime dueAtUtc)
        {
            var due = new DateTimeOffset(dueAtUtc).ToUnixTimeMilliseconds();
            return SchedulePrefix + scenarioId.ToString(CultureInfo.InvariantCulture) +
                   ":" + stepId.ToString(CultureInfo.InvariantCulture) +
                   ":" + targetScenarioId.ToString(CultureInfo.InvariantCulture) +
                   ":" + due.ToString(CultureInfo.InvariantCulture);
        }

        internal static string CreateScenarioPrefix(uint scenarioId) =>
            $"{StepPrefix}{scenarioId}{StepSeparator}";

        internal static bool TryParse(string stepKey, out MissionScenarioStepState state)
        {
            if (!string.IsNullOrWhiteSpace(stepKey) &&
                stepKey.StartsWith(StepPrefix, StringComparison.Ordinal))
            {
                var separatorIndex = stepKey.IndexOf(StepSeparator, StringComparison.Ordinal);
                if (separatorIndex > StepPrefix.Length &&
                    uint.TryParse(
                        stepKey.Substring(
                            StepPrefix.Length,
                            separatorIndex - StepPrefix.Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var scenarioId) &&
                    uint.TryParse(
                        stepKey[(separatorIndex + StepSeparator.Length)..],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var stepId))
                {
                    state = new MissionScenarioStepState(
                        MissionScenarioStepStateKind.CompletedStep,
                        stepKey,
                        scenarioId,
                        stepId);
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(stepKey) &&
                stepKey.StartsWith(SchedulePrefix, StringComparison.Ordinal))
            {
                var tokens = stepKey.Split(':');
                if (tokens.Length == 5 &&
                    uint.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var scenarioId) &&
                    uint.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stepId) &&
                    uint.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out var targetScenarioId) &&
                    long.TryParse(tokens[4], NumberStyles.None, CultureInfo.InvariantCulture, out var dueUnixMilliseconds))
                {
                    state = new MissionScenarioStepState(
                        MissionScenarioStepStateKind.ScheduledScenario,
                        stepKey,
                        scenarioId,
                        stepId,
                        targetScenarioId,
                        DateTimeOffset.FromUnixTimeMilliseconds(dueUnixMilliseconds).UtcDateTime);
                    return true;
                }
            }

            state = default;
            return false;
        }
    }
}
