using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public interface IByRoleTeamManager
    {
        void ReloadTeamRoles(out string message);
    }
}
