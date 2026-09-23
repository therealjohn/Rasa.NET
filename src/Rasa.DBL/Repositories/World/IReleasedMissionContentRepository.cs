using System.Collections.Generic;
using Rasa.Structures.World;

namespace Rasa.Repositories.World
{
    public interface IReleasedMissionContentRepository : IMissionContentRepository
    {
        MissionActiveReleaseEntry GetActiveRelease();
        List<MissionReleaseMemberEntry> GetReleaseMembers(string release);
        List<MissionSceneBindingEntry> GetSceneBindings();
        List<MissionExperienceBindingEntry> GetExperiences(string release);
    }
}
