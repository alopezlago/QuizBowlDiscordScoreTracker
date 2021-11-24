using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public class ByCommandTeamManager : ITeamManager, ISelfManagedTeamManager
    {
        // Used by tests
        internal const int MaximumTeamCount = 10;

        private readonly object collectionLock = new object();

        public ByCommandTeamManager()
        {
            this.PlayerIdToTeamId = new Dictionary<ulong, (string, string)>();
            this.TeamIdToName = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        }

        public string JoinTeamDescription => @"No teams. Use ""!addTeam *teamName*"" to add a team.";

        // The Team ID is the team name, which must be unique
        private IDictionary<ulong, (string teamId, string playerName)> PlayerIdToTeamId { get; }

        // we use a dictionary instead of a Set to easilsy support the GetTeamIdToName method
        private IDictionary<string, string> TeamIdToName { get; }

        public Task<IEnumerable<PlayerTeamPair>> GetKnownPlayers()
        {
            return Task.FromResult(this.PlayerIdToTeamId.Select(
                kvp => new PlayerTeamPair(kvp.Key, kvp.Value.playerName, kvp.Value.teamId)));
        }

        public Task<string> GetTeamIdOrNull(ulong userId)
        {
            string teamId;
            lock (this.collectionLock)
            {
                if (!this.PlayerIdToTeamId.TryGetValue(userId, out (string teamId, string) tuple) || tuple.teamId == null)
                {
                    return Task.FromResult<string>(null);
                }

                teamId = tuple.teamId;
            }

            return Task.FromResult(teamId);
        }

        public Task<IReadOnlyDictionary<string, string>> GetTeamIdToNames()
        {
            IReadOnlyDictionary<string, string> teamIdToName = (IReadOnlyDictionary<string, string>)this.TeamIdToName;
            return Task.FromResult(teamIdToName);
        }

        public Task<string> GetTeamNameOrNull(string teamId)
        {
            // The team ID is the team name here
            return Task.FromResult(this.TeamIdToName.TryGetValue(teamId, out string teamName) ? teamName : null);
        }

        public bool TryAddPlayerToTeam(ulong userId, string playerDisplayName, string teamName)
        {
            Verify.IsNotNull(teamName, nameof(teamName));

            teamName = teamName.Trim();
            if (!this.TeamIdToName.ContainsKey(teamName))
            {
                return false;
            }

            // Correct any case irregularities from the player by replacing with "default" casing
            teamName = this.TeamIdToName[teamName];
            this.PlayerIdToTeamId[userId] = (teamName, playerDisplayName);
            return true;
        }

        public bool TryAddTeam(string teamName, out string message)
        {
            Verify.IsNotNull(teamName, nameof(teamName));

            teamName = teamName.Trim();
            if (this.TeamIdToName.Count >= MaximumTeamCount)
            {
                message = $@"Cannot add team ""{teamName}"" because there are already {MaximumTeamCount} teams";
                return false;
            }
            else if (this.TeamIdToName.ContainsKey(teamName))
            {
                message = $@"Team ""{teamName}"" already exists.";
                return false;
            }

            message = $@"Added team ""{teamName}"".";
            this.TeamIdToName[teamName] = teamName;
            return true;
        }

        public bool TryRemovePlayerFromTeam(ulong userId)
        {
            return this.PlayerIdToTeamId.Remove(userId);
        }

        public bool TryRemoveTeam(string teamName, out string message)
        {
            Verify.IsNotNull(teamName, nameof(teamName));

            // If there are players on a team, we can't remove it
            (string teamId, string playerName) = this.PlayerIdToTeamId.Values
                .FirstOrDefault(pair => pair.teamId.Equals(teamName, StringComparison.OrdinalIgnoreCase));
            if (playerName != default)
            {
                message = $"Cannot remove a team because at least one player (**{playerName}**) is still on it.";
                return false;
            }

            teamName = teamName.Trim();
            if (!this.TeamIdToName.Remove(teamName))
            {
                message = $@"Cannot remove team ""{teamName}"" because it's not in the current game.";
                return false;
            }

            message = $@"Removed team ""{teamName}"".";
            return true;
        }
    }
}
