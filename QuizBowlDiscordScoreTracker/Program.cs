using System;
using System.IO;
using System.Threading.Tasks;
using DSharpPlus;

namespace QuizBowlDiscordScoreTracker
{
    class Program
    {
        static DiscordClient discord;

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

            // TODO: This should be stored in a more secure fashion.
            ////discord = new DiscordClient(new DiscordConfiguration()
            ////{
            ////    Token = tokens[1],
            ////    TokenType = TokenType.Bot
            ////});

            ////// bad form, should be clearing this
            ////discord.MessageCreated += async eventArgs =>
            ////{
            ////    // Maybe use Mention? and use Id for author.
            ////    if (eventArgs.Author != discord.CurrentUser)
            ////    {
            ////        //await eventArgs.Message.RespondAsync($"Sent from @{eventArgs.Author.Username}. Content: '{eventArgs.Message.Content}'.");
            ////        await eventArgs.Message.RespondAsync($"Sent from {eventArgs.Author.Mention}. Content: '{eventArgs.Message.Content}'.");
            ////    }
            ////};
            using (Bot bot = new Bot(tokens[1]))
            {
                await bot.ConnectAsync();

                // Never leave.
                await Task.Delay(-1);
            }
                
            ////TempClass tc = new TempClass();
        }
    }
}
