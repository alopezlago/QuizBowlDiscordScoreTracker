using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using System.Threading.Tasks;

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
        public async Task SetReader(CommandContext context)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.SetReader(new DiscordCommandContextWrapper(context));
            }
        }

        [Command("setnewreader")]
        [Description("Set another user as the reader.")]
        public async Task SetNewReader(CommandContext context, DiscordMember newReader)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.SetNewReader(new DiscordCommandContextWrapper(context), newReader.Id);
            }
        }

        [Command("stop")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public async Task Stop(CommandContext context)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.ClearAll(new DiscordCommandContextWrapper(context));
            }
        }

        [Command("end")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public async Task End(CommandContext context)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.ClearAll(new DiscordCommandContextWrapper(context));
            }
        }

        [Command("score")]
        [Description("Get the top scores in the current game.")]
        public async Task GetScore(CommandContext context)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.GetScore(new DiscordCommandContextWrapper(context));
            }
        }

        [Command("clear")]
        [Description("Clears the player queue. Use this if no one answered correctly.")]
        public async Task Clear(CommandContext context)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.Clear(new DiscordCommandContextWrapper(context));
            }
        }

        [Command("undo")]
        [Description("Undoes a scoring operation.")]
        public async Task Undo(CommandContext context)
        {
            if (IsSupportedChannel(context))
            {
                await this.handler.Undo(new DiscordCommandContextWrapper(context));
            }
        }

        // We check this here instead of in the handler because we should only handle events in supported channels.
        private static bool IsSupportedChannel(CommandContext context)
        {
            ConfigOptions options = context.Dependencies.GetDependency<ConfigOptions>();
            return options.IsSupportedChannel(context.Guild.Name, context.Channel.Name);
        }
    }
}
