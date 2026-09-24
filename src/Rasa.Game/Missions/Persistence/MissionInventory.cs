namespace Rasa.Game.Missions.Persistence
{
    using System;
    using Managers;
    using Repositories.Char;

    internal static class MissionInventory
    {
        internal static Action<Client> Plan(Client client, ICharUnitOfWork unit,
            MissionApplication manager, uint? missionId = null) =>
            Content.Bootcamp.BootcampBombInventory.Plan(client, unit, manager, missionId);
    }
}
