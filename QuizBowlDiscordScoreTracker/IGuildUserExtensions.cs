using System;
using System.Linq;
using Discord;

namespace QuizBowlDiscordScoreTracker
{
    public static class IGuildUserExtensions
    {
        public static bool CanRead(this IGuildUser user, IGuild guild, string readerRolePrefix)
        {
            return user != null && guild != null && 
                (readerRolePrefix == null || guild.Roles
                .Where(role => role.Name.StartsWith(readerRolePrefix, StringComparison.InvariantCultureIgnoreCase))
                .Select(role => role.Id)
                .Intersect(user.RoleIds)
                .Any());
        }
    }
}
