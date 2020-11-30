using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace QuizBowlDiscordScoreTracker
{
    public class BotConfiguration
    {
        public const string TokenKey = "BotToken";

        private static readonly JsonSerializerOptions GoogleJsonFileOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        };

        private string googleAppJsonFile;

        // File with this should look like:
        // {
        //    "waitForRejoinMs": 10000,
        //    "muteDelayMs": 500,
        //    "buzzEmojis": [":buzz:"],
        //    "webBaseUrl": "https://localhost:8080/index.html",
        //    "googleAppJsonFile": "C:\\Users\\Me\\Documents\\GoogleappName-f1111111111f.json
        // }
        // Token currently comes from a separate file, since it should eventually be encrypted and not included with
        // the config file.
        public BotConfiguration()
        {
            // Use defaults
            this.WaitForRejoinMs = 1000;
            this.MuteDelayMs = 500;
            this.DatabaseDataSource = null;
            this.DailyGuildExportLimit = 1000;
            this.DailyUserExportLimit = 50;
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

        /// <summary>
        /// The maximum number of export commands (inclusive) that can be called from a guild each day. The day is
        /// reset at midnight GMT.
        /// </summary>
        public int DailyGuildExportLimit { get; set; }

        /// <summary>
        /// The maximum number of export commands (inclusive) that can be called by a specific user each day. The day
        /// is reset at midnight GMT.
        /// </summary>
        public int DailyUserExportLimit { get; set; }

        /// <summary>
        /// The file location with information about the Google application's service account. This will refresh and
        /// override the values in GoogleAppEmail/GoogleAppPrivateKey
        /// </summary>
        public string GoogleAppJsonFile
        {
            get => this.googleAppJsonFile;
            set
            {
                if (this.googleAppJsonFile != value && File.Exists(value))
                {
                    this.googleAppJsonFile = value;
                    string credentialJson = File.ReadAllText(this.googleAppJsonFile);
                    GoogleCredentialFile credentialFileContents = JsonSerializer.Deserialize<GoogleCredentialFile>(
                        credentialJson, GoogleJsonFileOptions);
                    this.GoogleAppEmail = credentialFileContents.client_email;
                    this.GoogleAppPrivateKey = credentialFileContents.private_key;
                }
            }
        }

        /// <summary>
        /// The email account of the Google application service account
        /// </summary>
        public string GoogleAppEmail { get; set; }

        /// <summary>
        /// The private key for the Google application service credential that can access Google Sheets
        /// </summary>
        public string GoogleAppPrivateKey { get; set; }

        /// <summary>
        /// The token for the Discord bot
        /// </summary>
        public string BotToken { get; set; }

        /// <summary>
        /// The web URL for the buzzer website, that is hooked into the websockets
        /// </summary>
        public Uri WebBaseURL { get; set; }

        private class GoogleCredentialFile
        {
#pragma warning disable IDE1006 // Naming Styles. Json contract
            public string private_key { get; set; }

            public string client_email { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        }
    }
}
