using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker
{
    class Program
    {
        // Following the example from https://dsharpplus.emzi0767.com/articles/first_bot.html
        public static void Main()
        {
            MainAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private static async Task MainAsync()
        {
            BotConfiguration options = await GetConfigOptions();
            using (Bot bot = new Bot(options))
            {
                await bot.ConnectAsync();

                // Never leave.
                await Task.Delay(-1);
            }
        }

        static async Task<BotConfiguration> GetConfigOptions()
        {
            // TODO: Get the token from an encrypted file. This could be done by using DPAPI and writing a tool to help
            // convert the user access token into a token file using DPAPI. The additional entropy could be a config
            // option.
            // In preparation for this work the token is still taken from a separate file.
            string botToken = await File.ReadAllTextAsync("discordToken.txt");

            if (File.Exists("config.txt"))
            {
                string jsonOptions = await File.ReadAllTextAsync("config.txt");
                BotConfiguration options = JsonConvert.DeserializeObject<BotConfiguration>(jsonOptions);
                options.BotToken = botToken;
                return options;
            }
            else
            {
                return new BotConfiguration()
                {
                    WaitForRejoinMs = 10000,
                    MuteDelayMs = 500,
                    BotToken = botToken
                };
            }
        }
    }
}
