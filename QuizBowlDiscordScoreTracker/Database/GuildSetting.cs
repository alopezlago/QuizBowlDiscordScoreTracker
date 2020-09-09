using System.Collections.Generic;

namespace QuizBowlDiscordScoreTracker.Database
{
    public class GuildSetting
    {
        // This should match the Guild ID
        public ulong GuildSettingId { get; set; }
        public string TeamRolePrefix { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only. This is an EF Core model class; the collection must be settable
        public ICollection<TextChannelSetting> TextChannels { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only
    }
}
