using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class BotOwnerCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(BotOwnerCommandHandler));

        public BotOwnerCommandHandler(
            ICommandContext context, IOptionsMonitor<BotConfiguration> options, IDatabaseActionFactory dbActionFactory)
        {
            this.Context = context;
            this.DatabaseActionFactory = dbActionFactory;
            this.Options = options;
        }

        private ICommandContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        public async Task BanUserAsync(ulong userId)
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.AddCommandBannedUser(userId);
            }

            Logger.Information($"User {this.Context.User.Id} banned user {userId} from using commands");
            await this.Context.Channel.SendMessageAsync($"Banned user with ID {userId} from running commands.");
        }

        public async Task MapConfigToDatabaseAsync()
        {
            Logger.Information($"{this.Context.User.Id} is calling MapConfigToDatabase");

#pragma warning disable CS0618 // Type or member is obsolete. This method is used to help users move away from this obsolete setting
            IDictionary<string, ChannelPair[]> guildChannelPairs = this.Options.CurrentValue.SupportedChannels;
#pragma warning restore CS0618 // Type or member is obsolete
            if (guildChannelPairs == null)
            {
                return;
            }

            IReadOnlyCollection<IGuild> guilds = await this.Context.Client.GetGuildsAsync();
            IEnumerable<(IGuild, ChannelPair[])> guildsWithPairs = guildChannelPairs
                .Join(
                    guilds,
                    kvp => kvp.Key,
                    guild => guild.Name,
                    (kvp, guild) => (guild, kvp.Value));

            List<(ulong textChannelId, ulong voiceChannelId)> pairChannels = new List<(ulong, ulong)>();
            IReadOnlyCollection<ITextChannel> textChannels = await this.Context.Guild.GetTextChannelsAsync();
            IReadOnlyCollection<IVoiceChannel> voiceChannels = await this.Context.Guild.GetVoiceChannelsAsync();

            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                List<Task> pairChannelTasks = new List<Task>();
                foreach ((IGuild, ChannelPair[]) guildWithPair in guildsWithPairs)
                {
                    foreach (ChannelPair pair in guildWithPair.Item2)
                    {
                        // If there's no voice channel, then don't add them to the database
                        if (string.IsNullOrEmpty(pair.Voice))
                        {
                            continue;
                        }

                        // Unfortunately, Discord can support multiple channels with the same name, so we can't just
                        // convert the text/voice channels to a dictionary of name -> ID.
                        ITextChannel textChannel = textChannels.FirstOrDefault(channel => channel.Name == pair.Text);
                        IVoiceChannel voiceChannel = voiceChannels.FirstOrDefault(channel => channel.Name == pair.Voice);
                        if (textChannel != null && voiceChannel != null)
                        {
                            pairChannels.Add((textChannel.Id, voiceChannel.Id));
                        }
                    }

                    pairChannelTasks.Add(
                        action.PairChannelsAsync(guildWithPair.Item1.Id, pairChannels.ToArray()));
                }

                await Task.WhenAll(pairChannelTasks);
            }

            await this.Context.Channel.SendMessageAsync("Configurations mapped for all servers the bot is in.");
        }

        public async Task UnbanUserAsync(ulong userId)
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.RemoveCommandBannedUser(userId);
            }

            Logger.Information($"User {this.Context.User.Id} unbanned user {userId} from using commands");
            await this.Context.Channel.SendMessageAsync($"Unbanned user with ID {userId} from running commands.");
        }
    }
}
