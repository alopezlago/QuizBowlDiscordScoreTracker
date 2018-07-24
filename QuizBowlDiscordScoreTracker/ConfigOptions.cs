namespace QuizBowlDiscordScoreTracker
{
    public class ConfigOptions
    {
        // File with this should look like:
        // {
        //    "adminIds": [ "413243442536807758", "223201443436757988"],
        //    "waitForRejoinMs": 10000
        // }
        // Token currently comes from a separate file, since it should eventually be encrypted and not included with
        // the config file.

        /// <summary>
        /// The user Ids of all of the guild/channel admins
        /// </summary>
        public string[] AdminIds { get; set; }

        /// <summary>
        /// The amount of time, in milliseconds, to wait for a reader to come back online before removing them and stopping
        /// the game.
        /// </summary>
        public int WaitForRejoinMs { get; set; }

        public string BotToken { get; set; }
    }
}
