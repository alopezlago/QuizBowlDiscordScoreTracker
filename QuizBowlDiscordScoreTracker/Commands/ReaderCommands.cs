using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireReader]
    public class ReaderCommands : BotCommandBase
    {
        public ReaderCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
            : base(manager, options, dbActionFactory)
        {
        }

        [Command("setnewreader")]
        [Summary("Set another user as the reader.")]
        public Task SetNewReaderAsync([Summary("Mention of the new reader")] IGuildUser newReader)
        {
            return this.HandleCommandAsync(handler => handler.SetNewReaderAsync(newReader.Id));
        }

        [Command("stop")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task StopAsync()
        {
            return this.HandleCommandAsync(handler => handler.ClearAllAsync());
        }

        [Command("end")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task EndAsync()
        {
            return this.HandleCommandAsync(handler => handler.ClearAllAsync());
        }

        [Command("clear")]
        [Summary("Clears the player queue and answers from this question, including scores from this question.")]
        public Task ClearAsync()
        {
            return this.HandleCommandAsync(handler => handler.ClearAsync());
        }

        [Command("next")]
        [Summary("Clears the player queue and moves to the next question. Use this if no one answered correctly.")]
        public Task NextAsync()
        {
            return this.HandleCommandAsync(handler => handler.NextQuestion());
        }

        [Command("undo")]
        [Summary("Undoes a scoring operation.")]
        public Task UndoAsync()
        {
            return this.HandleCommandAsync(handler => handler.UndoAsync());
        }
    }
}
