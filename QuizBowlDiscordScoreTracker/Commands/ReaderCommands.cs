using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireReader]
    [RequireContext(ContextType.Guild)]
    public class ReaderCommands : ModuleBase
    {
        public ReaderCommands(GameStateManager manager)
        {
            this.Manager = manager;
        }

        private GameStateManager Manager { get; }

        [Command("setnewreader")]
        [Summary("Set another user as the reader.")]
        public Task SetNewReaderAsync([Summary("Mention of the new reader")] IGuildUser newReader)
        {
            return this.GetHandler().SetNewReaderAsync(newReader);
        }

        [Command("stop")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task StopAsync()
        {
            return this.GetHandler().ClearAllAsync();
        }

        [Command("end")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task EndAsync()
        {
            return this.GetHandler().ClearAllAsync();
        }

        [Command("clear")]
        [Summary("Clears the player queue and answers from this question, including scores from this question.")]
        public Task ClearAsync()
        {
            return this.GetHandler().ClearAsync();
        }

        [Command("next")]
        [Summary("Clears the player queue and moves to the next question. Use this if no one answered correctly.")]
        public Task NextAsync()
        {
            return this.GetHandler().NextAsync();
        }

        [Command("undo")]
        [Summary("Undoes a scoring operation.")]
        public Task UndoAsync()
        {
            return this.GetHandler().UndoAsync();
        }

        private ReaderCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            return new ReaderCommandHandler(this.Context, this.Manager);
        }
    }
}
