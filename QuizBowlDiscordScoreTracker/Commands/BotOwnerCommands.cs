using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public class BotOwnerCommands : ModuleBase
    {
        public BotOwnerCommands(IOptionsMonitor<BotConfiguration> options, IDatabaseActionFactory dbActionFactory)
        {
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
        }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        // This is a temporary command to transition between v1/v2 and v3. Few if any users will use this, so no tests
        // are currently planned (and therefore no command handler is being written).
        // This converts the channel pair mappings in the config file into database entries
        [Command("mapConfigToDatabase")]
        [Summary("Maps configuration information from config.txt to the database, so users can control their own " +
            "guild-specific settings.")]
        public async Task MapConfigToDatabaseAsync()
        {
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
    }
}
