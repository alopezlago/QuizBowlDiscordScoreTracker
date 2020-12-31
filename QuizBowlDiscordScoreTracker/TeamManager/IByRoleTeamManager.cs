using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public interface IByRoleTeamManager
    {
        /// <summary>
        /// Reloads teams from the roles
        /// </summary>
        /// <returns>Returns the message to report after the roles were reloaded</returns>
        string ReloadTeamRoles();
    }
}
