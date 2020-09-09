using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace QuizBowlDiscordScoreTracker
{
    public class BotConfiguration
    {
        public const string TokenKey = "BotToken";

        // File with this should look like:
        // {
        //    "waitForRejoinMs": 10000,
        //    "muteDelayMs": 500,
        //    "buzzEmojis": [":buzz:"],
        //    "webBaseUrl": "https://localhost:8080/index.html"
        // }
        // Token currently comes from a separate file, since it should eventually be encrypted and not included with
        // the config file.
        public BotConfiguration()
        {
            // Use defaults
            this.WaitForRejoinMs = 1000;
            this.MuteDelayMs = 500;
            this.DatabaseDataSource = null;
            this.BuzzEmojis = Array.Empty<string>();
            this.BotToken = string.Empty;
            this.WebBaseURL = null;
        }

        /// <summary>
        /// The amount of time, in milliseconds, to wait for a reader to come back online before removing them and stopping
        /// the game.
        /// </summary>
        public int WaitForRejoinMs { get; set; }

        /// <summary>
        /// The amount of time, in milliseconds, to mute the reader after a buzz occurs.
        /// </summary>
        public int MuteDelayMs { get; set; }

        /// <summary>
        /// The DataSource to use for the database storing guild-specific settings. This is generally a file path, but
        /// it can be ":memory:" if you don't want to store anything on disk. If this isn't defined, use the default
        /// data source location.
        /// </summary>
        public virtual string DatabaseDataSource { get; set; }

        // TODO: Add an upgrade script with the normal bot, so teams will have their channels paired automatically.
        /// <summary>
        /// DEPRECATED. The channels which the bot will listen to. It maps guild/server names to channels supported on that server.
        /// If this is null, then every channel is supported.
        /// </summary>
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Needed for deserializer")]
        [Obsolete("Users should use the !pairChannels/!unpairChannel commands instead")]
        public IDictionary<string, ChannelPair[]> SupportedChannels { get; set; }
        /// The emojis which represent buzzes. They should be of the form ":buzz:", which is the emoji text the user
        /// types.
        /// </summary>
        // This has to be a string array because IOptionsMonitor doesn't parse the JSON array as an IEnumerable or
        // IReadonlyCollection
#pragma warning disable CA1819 // Properties should not return arrays
        public string[] BuzzEmojis { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        public string BotToken { get; set; }
        public Uri WebBaseURL { get; set; }
    }
}
