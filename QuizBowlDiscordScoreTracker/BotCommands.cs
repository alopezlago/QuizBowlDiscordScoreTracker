using System.Collections.Generic;
using System.Linq;
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

        // We check this here instead of in the handler because we should only handle events in supported channels.
        private static bool IsSupportedChannel(CommandContext context)
        {
            ConfigOptions options = context.Dependencies.GetDependency<ConfigOptions>();
            IDictionary<string, string[]> supportedChannelsMap = options.SupportedChannels;
            if (supportedChannelsMap == null)
            {
                return true;
            }

            // TODO: We may want to convert supportedChannels into a Dictionary in the constructor so we can do these
            // lookups more efficiently. In general there shouldn't be too many supported channels per guild so
            // this shouldn't be bad performance-wise.
            // TODO: For now this is case-sensitive; I don't know if Discord cares about casing in its channels.
            return supportedChannelsMap.TryGetValue(context.Guild.Name, out string[] supportedChannels) &&
                supportedChannels.Contains(context.Channel.Name);
        }
    }
}
