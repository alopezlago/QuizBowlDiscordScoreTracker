namespace QuizBowlDiscordScoreTracker.Database
{
    public class UserSetting
    {
        // This should match the user ID
        public ulong UserSettingId { get; set; }

        public int ExportCount { get; set; }
        public int LastExportDay { get; set; }
        public bool CommandBanned { get; set; }
    }
}
