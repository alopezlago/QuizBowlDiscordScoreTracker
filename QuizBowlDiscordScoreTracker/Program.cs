using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker
{
    class Program
    {
        // Following the example from https://dsharpplus.emzi0767.com/articles/first_bot.html
        static void Main(string[] args)
        {
            MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {

            ConfigOptions options = await GetConfigOptions();
            using (Bot bot = new Bot(options))
            {
                await bot.ConnectAsync();

                // Never leave.
                await Task.Delay(-1);
            }
        }

        static async Task<ConfigOptions> GetConfigOptions()
        {
            // TODO: Get the token from an encrypted file. This could be done by using DPAPI and writing a tool to help
            // convert the user access token into a token file using DPAPI. The additional entropy could be a config
            // option.
            // In preparation for this work the token is still taken from a separate file.
            string botToken = await File.ReadAllTextAsync("discordToken.txt");

            if (File.Exists("config.txt"))
            {
                string jsonOptions = await File.ReadAllTextAsync("config.txt");
                ConfigOptions options = JsonConvert.DeserializeObject<ConfigOptions>(jsonOptions);
                options.BotToken = botToken;
                return options;
            }
            else
            {
                return new ConfigOptions()
                {
                    AdminIds = new string[0],
                    WaitForRejoinMs = 10000,
                    BotToken = botToken
                };
            }
        }
    }
}
