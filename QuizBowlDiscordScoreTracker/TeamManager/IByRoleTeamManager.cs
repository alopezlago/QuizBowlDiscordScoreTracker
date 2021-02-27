using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public interface IByRoleTeamManager : ITeamManager
    {
        Task<IReadOnlyDictionary<string, string>> GetTeamIdToNamesForServer();

        /// <summary>
        /// Reloads teams from the roles
        /// </summary>
        /// <returns>Returns the message to report after the roles were reloaded</returns>
        string ReloadTeamRoles();

        /// <summary>
        /// Gets a grouping of every player to their team in the server. This will ignore channel permissions for
        /// learning who is on a team
        /// </summary>
        /// <returns>A grouping of team names to PlayerTeamPairs for every team in the server.</returns>
        Task<IEnumerable<IGrouping<string, PlayerTeamPair>>> GetPlayerTeamPairsForServer();
    }
}
