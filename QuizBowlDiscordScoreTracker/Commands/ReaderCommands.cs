using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireReader]
    public class ReaderCommands : BotCommandBase
    {
        public ReaderCommands(GameStateManager manager, IOptionsMonitor<BotConfiguration> options) 
            : base(manager, options)
        {
        }

        [Command("setnewreader")]
        [Summary("Set another user as the reader.")]
        public Task SetNewReader([Summary("Mention of the new reader")] IGuildUser newReader)
        {
            return this.HandleCommandAsync(handler => handler.SetNewReader(newReader.Id));
        }

        [Command("stop")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task Stop()
        {
            return this.HandleCommandAsync(handler => handler.ClearAll());
        }

        [Command("end")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task End()
        {
            return this.HandleCommandAsync(handler => handler.ClearAll());
        }

        [Command("clear")]
        [Summary("Clears the player queue and answers from this question, including scores from this question.")]
        public Task Clear()
        {
            return this.HandleCommandAsync(handler => handler.Clear());
        }

        [Command("next")]
        [Summary("Clears the player queue and moves to the next question. Use this if no one answered correctly.")]
        public Task Next()
        {
            return this.HandleCommandAsync(handler => handler.NextQuestion());
        }

        [Command("undo")]
        [Summary("Undoes a scoring operation.")]
        public Task Undo()
        {
            return this.HandleCommandAsync(handler => handler.Undo());
        }
    }
}
