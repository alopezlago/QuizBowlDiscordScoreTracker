using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace QuizBowlDiscordScoreTracker
{
    public static class Program
    {
        // 100 MB file limit
        private const long maxLogfileSize = 1024 * 1024 * 100;

        // Following the example from https://dsharpplus.emzi0767.com/articles/first_bot.html
        public static void Main()
        {
            MainAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private static async Task MainAsync()
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine("logs", "bot.log"),
                    fileSizeLimitBytes: maxLogfileSize,
                    retainedFileCountLimit: 10);
            Log.Logger = loggerConfiguration.CreateLogger();

            BotConfiguration options;
            try
            {
                options = await GetConfigOptions();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read configuration.");
                throw;
            }

            using (Bot bot = new Bot(options))
            {
                await bot.ConnectAsync();

                // Never leave.
                await Task.Delay(-1);
            }
        }

        private static async Task<BotConfiguration> GetConfigOptions()
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
