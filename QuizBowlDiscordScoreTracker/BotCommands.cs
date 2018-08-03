using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    public class BotCommands
    {
        private readonly BotCommandHandler handler;

        public BotCommands()
        {
            this.handler = new BotCommandHandler();
        }

        [Command("read")]
        [Description("Set yourself as the reader.")]
        public Task SetReader(CommandContext context)
        {
            return this.handler.SetReader(new DiscordCommandContextWrapper(context));
        }

        [Command("setnewreader")]
        [Description("Set another user as the reader.")]
        public Task SetNewReader(CommandContext context, DiscordMember newReader)
        {
            return this.handler.SetNewReader(new DiscordCommandContextWrapper(context), newReader.Id);
        }

        [Command("stop")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public Task Stop(CommandContext context)
        {
            return this.handler.ClearAll(new DiscordCommandContextWrapper(context));
        }

        [Command("end")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public Task End(CommandContext context)
        {
            return this.handler.ClearAll(new DiscordCommandContextWrapper(context));
        }

        [Command("score")]
        [Description("Get the top scores in the current game.")]
        public Task GetScore(CommandContext context)
        {
            return this.handler.GetScore(new DiscordCommandContextWrapper(context));
        }

        [Command("clear")]
        [Description("Clears the player queue. Use this if no one answered correctly.")]
        public Task Clear(CommandContext context)
        {
            return this.handler.Clear(new DiscordCommandContextWrapper(context));
        }
    }
}
