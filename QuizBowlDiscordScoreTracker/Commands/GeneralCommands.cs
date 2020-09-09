using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class GeneralCommands : BotCommandBase
    {
        public GeneralCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
            : base(manager, options, dbActionFactory)
        {
        }

        [Command("read")]
        [Summary("Set yourself as the reader.")]
        public Task SetReader()
        {
            return this.HandleCommandAsync(handler => handler.SetReader());
        }

        [Command("score")]
        [Summary("Get the top scores in the current game.")]
        public Task GetScore()
        {
            return this.HandleCommandAsync(handler => handler.GetScore());
        }
    }
}
