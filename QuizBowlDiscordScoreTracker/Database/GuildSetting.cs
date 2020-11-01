using System.Collections.Generic;

namespace QuizBowlDiscordScoreTracker.Database
{
    public class GuildSetting
    {
        // This should match the Guild ID
        public ulong GuildSettingId { get; set; }
        public string ReaderRolePrefix { get; set; }
        public string TeamRolePrefix { get; set; }
        public bool UseBonuses { get; set; }

        public int ExportCount { get; set; }
        public int LastExportDay { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only. This is an EF Core model class; the collection must be settable
        public ICollection<TextChannelSetting> TextChannels { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only
    }
}
