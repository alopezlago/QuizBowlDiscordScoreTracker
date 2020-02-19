using System;
using System.Collections.Generic;

namespace QBDiscordScoreTracker
{
    // TODO: Look into whether we want this to be an instance class. For now match the behavior in the shared locations
    // and treat this as a static method.
    public static class TeamNameParser
    {
        // Input: a list of comma-delimited team names
        // Output: a set of team names if we could parse it, or an error message if we couldn't.
        public static bool TryGetTeamNamesFromParts(
            string combinedTeamNames, out IList<string> teamNames, out string errorMessage)
        {
            errorMessage = null;
            teamNames = new List<string>();
            HashSet<string> previousTeamNames = new HashSet<string>();

            if (string.IsNullOrEmpty(combinedTeamNames))
            {
                // 0 teams is acceptable.
                return true;
            }

            bool possibleCommaEscapeStart = false;
            int startIndex = 0;
            int length;
            string teamName;
            for (int i = 0; i < combinedTeamNames.Length; i++)
            {
                char token = combinedTeamNames[i];
                if (token == ',')
                {
                    // If the previous token was a comma, then this is an escape (i.e. this character won't be the
                    // start of an escape). If not, then this could be the start of an escape.
                    possibleCommaEscapeStart = !possibleCommaEscapeStart;
                }
                else if (possibleCommaEscapeStart)
                {
                    // The previous character was a comma, but this one isn't, so it's a separator. Get the team
                    // name.
                    length = Math.Max(0, i - startIndex - 1);
                    teamName = combinedTeamNames
                        .Substring(startIndex, length)
                        .Trim()
                        .Replace(",,", ",", StringComparison.InvariantCulture);
                    if (previousTeamNames.Add(teamName))
                    {
                        teamNames.Add(teamName);
                    }

                    startIndex = i;
                    possibleCommaEscapeStart = false;
                }
            }

            // Add the remaining team.
            if (combinedTeamNames[combinedTeamNames.Length - 1] == ',' && possibleCommaEscapeStart)
            {
                errorMessage = "team missing from addTeams (trailing comma)";
                return false;
            }

            // No comma, so don't subtract 1.
            length = Math.Max(0, combinedTeamNames.Length - startIndex);
            teamName = combinedTeamNames
                .Substring(startIndex, length)
                .Trim()
                .Replace(",,", ",", StringComparison.InvariantCulture);
            if (!previousTeamNames.Contains(teamName))
            {
                teamNames.Add(teamName);
            }

            return true;
        }
    }
}
