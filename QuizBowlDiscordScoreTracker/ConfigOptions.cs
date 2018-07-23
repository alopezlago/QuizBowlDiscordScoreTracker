namespace QuizBowlDiscordScoreTracker
{
    public class ConfigOptions
    {
        // File with this should look like:
        // {
        //    "AdminIds": [ "413243442536807758", "223201443436757988"]
        // }
        // Token currently comes from a separate file, since it should eventually be encrypted and not included with
        // the config file.
        public string[] AdminIds { get; set; }

        public string BotToken { get; set; }
    }
}
