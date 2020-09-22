using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker
{
    public static class IGuildUserExtensionscs
    {
        // I looked into the performance of this method, tosee if we should cache the team role prefix or the roles.
        // There's a cold-start cost of about 20-25 ms on the first buzz (including adding them to the queue), and
        // afterwards it's <10ms, while prompting takes ~150-200 ms, so it's not worth speeding this part up yet
        public static async Task<ulong?> GetTeamId(this IGuildUser user, IDatabaseActionFactory dbActionFactory)
        {
            if (user == null)
            {
                return null;
            }

            Verify.IsNotNull(dbActionFactory, nameof(dbActionFactory));

            string teamRolePrefix = null;
            using (DatabaseAction action = dbActionFactory.Create())
            {
                teamRolePrefix = await action.GetTeamRolePrefixAsync(user.GuildId);
            }

            if (teamRolePrefix == null)
            {
                return null;
            }

            // TODO: What should we do if the user has multiple roles with the prefix? For now we'll just always take
            // the first one
            IEnumerable<IRole> matchingRoles = user.Guild.Roles
                .Where(role => role.Name.StartsWith(teamRolePrefix, StringComparison.InvariantCultureIgnoreCase))
                .Join(user.RoleIds, role => role.Id, id => id, (role, id) => role);
            return matchingRoles.FirstOrDefault()?.Id;
        }
    }
}
