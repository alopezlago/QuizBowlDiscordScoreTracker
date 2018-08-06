using System.Collections.Generic;

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

        /// <summary>
        /// The channels which the bot will listen to. It maps guild/server names to channels supported on that server.
        /// If this is null, then every channel is supported.
        /// A version which uses guild/channel IDs instead may be supported later.
        /// </summary>
        public IDictionary<string, string[]> SupportedChannels { get; set; }

        /// <summary>
        /// The emojis which represent buzzes. They should be of the form ":buzz:", which is the emoji text the user
        /// types.
        /// </summary>
        public string[] BuzzEmojis { get; set; }

        public string BotToken { get; set; }
    }
}
