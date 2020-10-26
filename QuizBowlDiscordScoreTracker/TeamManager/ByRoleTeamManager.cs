using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public class ByRoleTeamManager : ITeamManager
    {
        private readonly object teamIdToNameLock = new object();

        public ByRoleTeamManager(IGuild guild, string teamRolePrefix)
        {
            this.Guild = guild;
            this.TeamRolePrefix = teamRolePrefix;
            this.InitiailzeTeamIdToName();
        }

        public string JoinTeamDescription => $@"The team role prefix is ""{this.TeamRolePrefix}"", but no roles " +
            "existed with that prefix when the game started. Add roles with the team prefix, then restart the game " +
            "to play with teams.";

        private IGuild Guild { get; }

        private string TeamRolePrefix { get; }

        private IDictionary<string, string> TeamIdToName { get; set; }

        public async Task<IEnumerable<PlayerTeamPair>> GetKnownPlayers()
        {
            IReadOnlyCollection<IGuildUser> users = await this.Guild.GetUsersAsync();
            return users
                .Select(user => new Tuple<ulong, ulong, string>(
                    user.Id,
                    user.RoleIds.FirstOrDefault(id => this.TeamIdToName.ContainsKey(id.ToString(CultureInfo.InvariantCulture))),
                    user.Nickname ?? user.Username))
                .Where(kvp => kvp.Item2 != default)
                .Select(tuple => new PlayerTeamPair(
                    tuple.Item1, tuple.Item3, tuple.Item2.ToString(CultureInfo.InvariantCulture)));
        }

        public async Task<string> GetTeamIdOrNull(ulong userId)
        {
            IGuildUser user = await this.Guild.GetUserAsync(userId);
            if (user == null)
            {
                return null;
            }

            lock (this.teamIdToNameLock)
            {
                ulong matchingRoleId = user.RoleIds.FirstOrDefault(
                    id => this.TeamIdToName.ContainsKey(id.ToString(CultureInfo.InvariantCulture)));
                return matchingRoleId == default ? null : matchingRoleId.ToString(CultureInfo.InvariantCulture);
            }
        }

        public Task<IReadOnlyDictionary<string, string>> GetTeamIdToNames()
        {
            IReadOnlyDictionary<string, string> teamIdToName = (IReadOnlyDictionary<string, string>)this.TeamIdToName;
            return Task.FromResult(teamIdToName);
        }

        private void InitiailzeTeamIdToName()
        {
            lock (this.teamIdToNameLock)
            {
                this.TeamIdToName = this.Guild.Roles
                    .Where(role => role.Name.StartsWith(this.TeamRolePrefix, StringComparison.InvariantCultureIgnoreCase))
                    .ToDictionary(
                        role => role.Id.ToString(CultureInfo.InvariantCulture),
                        role => role.Name.Substring(this.TeamRolePrefix.Length).Trim());
            }
        }
    }
}
