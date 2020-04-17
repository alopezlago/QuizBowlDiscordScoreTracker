using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
        //    "supportedChannels": {
        //        "server1": [{ "text": "channel1", "voice": "Voice Channel" }],
        //        "server2": [{ "text": "packet", "voice": "Packet Voice" }, { text: "channel2" }],
        //    }
        // }
        // Token currently comes from a separate file, since it should eventually be encrypted and not included with
        // the config file.
        public BotConfiguration()
        {
            // Use defaults
            this.WaitForRejoinMs = 1000;
            this.MuteDelayMs = 500;
            this.BuzzEmojis = Array.Empty<string>();
            this.SupportedChannels = new Dictionary<string, ChannelPair[]>();
            this.BotToken = string.Empty;
        }

        /// <summary>
        /// The amount of time, in milliseconds, to wait for a reader to come back online before removing them and stopping
        /// the game.
        /// </summary>
        public int WaitForRejoinMs { get; set; }

        public int MuteDelayMs { get; set; }

        /// <summary>
        /// The channels which the bot will listen to. It maps guild/server names to channels supported on that server.
        /// If this is null, then every channel is supported.
        /// A version which uses guild/channel IDs instead may be supported later.
        /// </summary>
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Needed for deserializer")]
        public IDictionary<string, ChannelPair[]> SupportedChannels { get; set; }

        /// <summary>
        /// The emojis which represent buzzes. They should be of the form ":buzz:", which is the emoji text the user
        /// types.
        /// </summary>
        // This has to be a string array because IOptionsMonitor doesn't parse the JSON array as an IEnumerable or
        // IReadonlyCollection
#pragma warning disable CA1819 // Properties should not return arrays
        public string[] BuzzEmojis { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        public string BotToken { get; set; }

        public bool IsTextSupportedChannel(string guildName, string channelName)
        {
            if (this.SupportedChannels == null)
            {
                return true;
            }

            // We could convert supportedChannels into a Dictionary in the constructor so we can do these
            // lookups more efficiently. In general there shouldn't be too many supported channels per guild so
            // this shouldn't be bad performance-wise.
            return this.SupportedChannels.TryGetValue(guildName, out ChannelPair[] supportedChannels) &&
                supportedChannels.Select(pair => pair.Text).Contains(channelName);
        }

        public bool TryGetVoiceChannelName(string guildName, string textChannelName, out string voiceChannelName)
        {
            if (!this.SupportedChannels.TryGetValue(guildName, out ChannelPair[] supportedChannels))
            {
                voiceChannelName = null;
                return false;
            }

            ChannelPair channelPair = supportedChannels.FirstOrDefault(pair => pair.Text == textChannelName);
            voiceChannelName = channelPair.Voice;
            return voiceChannelName != null;
        }
    }
}
