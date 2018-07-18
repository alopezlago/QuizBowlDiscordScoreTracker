using System;
using System.IO;
using System.Threading.Tasks;
using DSharpPlus;

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
            // TODO: Redo the tokens text file
            string[] tokens = File.ReadAllLines("discordToken.txt");
            if (tokens.Length < 2)
            {
                Console.Error.WriteLine("Error: tokens file is improperly formatted.");
                Environment.Exit(1);
            }

            using (Bot bot = new Bot(tokens[1]))
            {
                await bot.ConnectAsync();

                // Never leave.
                await Task.Delay(-1);
            }
        }
    }
}
