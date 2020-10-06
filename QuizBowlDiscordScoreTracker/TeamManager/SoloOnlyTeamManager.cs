using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public class SoloOnlyTeamManager : ITeamManager
    {
        public static readonly SoloOnlyTeamManager Instance = new SoloOnlyTeamManager();

        public string JoinTeamDescription => string.Empty;

        public Task<IEnumerable<PlayerTeamPair>> GetKnownPlayers()
        {
            // There are no teams, so we don't know any of the players
            return Task.FromResult(Enumerable.Empty<PlayerTeamPair>());
        }

        public Task<string> GetTeamIdOrNull(ulong userId)
        {
            return Task.FromResult<string>(null);
        }

        public Task<IReadOnlyDictionary<string, string>> GetTeamIdToNames()
        {
            // TODO: Cache this, if we ever use SoloOnly outside of tests
            IReadOnlyDictionary<string, string> teamIdToNames = new Dictionary<string, string>();
            return Task.FromResult(teamIdToNames);
        }
    }
}
