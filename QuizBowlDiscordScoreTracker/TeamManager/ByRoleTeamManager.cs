using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;

namespace QuizBowlDiscordScoreTracker.TeamManager
{
    public class ByRoleTeamManager : ITeamManager, IByRoleTeamManager
    {
        private readonly object teamIdToNameLock = new object();

        public ByRoleTeamManager(IGuildChannel channel, string teamRolePrefix)
        {
            Verify.IsNotNull(channel, nameof(channel));

            this.Guild = channel.Guild;
            this.Channel = channel;
            this.TeamRolePrefix = teamRolePrefix;
            this.InitiailzeTeamIdToName();
        }

        public string JoinTeamDescription => $@"The team role prefix is ""{this.TeamRolePrefix}"", but no roles " +
            "existed with that prefix when the game started. Add roles with the team prefix, then restart the game " +
            "to play with teams.";

        private IGuild Guild { get; }

        private IGuildChannel Channel { get; }

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

        public string ReloadTeamRoles()
        {
            this.InitiailzeTeamIdToName();
            return $@"Team roles reloaded. There are now {this.TeamIdToName.Count} team(s)";
        }

        private void InitiailzeTeamIdToName()
        {
            lock (this.teamIdToNameLock)
            {
                OverwritePermissions? everyonePermissions = this.Channel.GetPermissionOverwrite(this.Guild.EveryoneRole);
                PermValue everyoneViewPermissions = everyonePermissions?.ViewChannel ?? PermValue.Inherit;
                PermValue everyoneSendPermissions = everyonePermissions?.SendMessages ?? PermValue.Inherit;

                this.TeamIdToName = this.Guild.Roles
                    .Where(role => role.Name.StartsWith(this.TeamRolePrefix, StringComparison.InvariantCultureIgnoreCase))
                    .Where(role =>
                    {
                        // Players need both View and Send permissions to play, so make sure either the role or the
                        // everyone role has it
                        OverwritePermissions? permissions = this.Channel.GetPermissionOverwrite(role);
                        if (!permissions.HasValue)
                        {
                            // No specific permissions, so inherit it from everyone
                            return everyonePermissions?.ViewChannel != PermValue.Deny &&
                                everyonePermissions?.SendMessages != PermValue.Deny;
                        }

                        OverwritePermissions permissionsValue = permissions.Value;
                        PermValue viewPermissions = permissionsValue.ViewChannel != PermValue.Inherit ?
                            permissionsValue.ViewChannel :
                            everyoneViewPermissions;
                        PermValue sendPermissions = permissionsValue.SendMessages != PermValue.Inherit ?
                            permissionsValue.SendMessages :
                            everyoneSendPermissions;

                        return viewPermissions != PermValue.Deny && sendPermissions != PermValue.Deny;
                    })
                    .ToDictionary(
                        role => role.Id.ToString(CultureInfo.InvariantCulture),
                        role => role.Name.Substring(this.TeamRolePrefix.Length).Trim());
            }
        }
    }
}
