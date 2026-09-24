namespace Rasa.Missions.Scenes
{
    public sealed record PublicEncounterBinding(
        uint MissionId, uint SpawnId, string Role, string ScriptKey,
        string OwnerLossPolicy = "Reset", bool IncludeEligibleParty = false);
}
