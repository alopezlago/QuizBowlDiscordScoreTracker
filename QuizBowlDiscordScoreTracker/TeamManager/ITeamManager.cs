using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public interface ITeamManager
    {
        string JoinTeamDescription { get; }

        Task<IEnumerable<PlayerTeamPair>> GetKnownPlayers();

        Task<string> GetTeamIdOrNull(ulong userId);

        Task<IReadOnlyDictionary<string, string>> GetTeamIdToNames();
    }
}
