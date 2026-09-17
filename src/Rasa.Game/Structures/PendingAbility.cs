namespace Rasa.Structures
{
    using Data;
    using Game;

    internal sealed record PendingAbility(Client Client, Manifestation Player, MapChannel Map,
        Actor Target, ActionData Action, ActionId Id, int Rank, long ReadyAt)
    {
        internal bool Completed { get; set; }
        internal long PlayerLifetime { get; } = Player.ActionLifetime;
        internal long TargetLifetime { get; } = Target.ActionLifetime;
    }
}
