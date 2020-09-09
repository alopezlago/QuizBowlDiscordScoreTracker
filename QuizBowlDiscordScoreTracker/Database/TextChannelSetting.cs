using System.ComponentModel.DataAnnotations.Schema;

namespace QuizBowlDiscordScoreTracker.Database
{
    public class TextChannelSetting
    {
        // This should match the TextChannel ID
        public ulong TextChannelSettingId { get; set; }
        public ulong? VoiceChannelId { get; set; }

        // This should match the channel's ID
        public ulong? TeamMessageId { get; set; }

        [ForeignKey("GuildSetting")]
        public ulong GuildSettingId { get; set; }
    }
}
