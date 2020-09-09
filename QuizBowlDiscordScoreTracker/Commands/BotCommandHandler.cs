using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    // TODO: Look into splitting this off into command handlers for each command class. The refactor may not be worth
    // the improved readability, though.
    public class BotCommandHandler
    {
        private readonly ICommandContext context;
        private readonly GameStateManager manager;
        private readonly GameState currentGame;
        private readonly ILogger logger;
        private readonly IOptionsMonitor<BotConfiguration> options;
        private readonly IDatabaseActionFactory dbActionFactory;

        public BotCommandHandler(
            ICommandContext context,
            GameStateManager manager,
            GameState currentGame,
            ILogger logger,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
        {
            this.context = context;
            this.manager = manager;
            this.currentGame = currentGame;
            this.logger = logger;
            this.options = options;
            this.dbActionFactory = dbActionFactory;
        }

        public async Task CheckPermissions()
        {
            if (!(this.context.Channel is IGuildChannel guildChannel))
            {
                return;
            }

            IGuildUser guildBotUser = await this.context.Guild.GetCurrentUserAsync();
            ChannelPermissions channelPermissions = guildBotUser.GetPermissions(guildChannel);

            StringBuilder builder = new StringBuilder();

            // This is probably impossible to hit, since we must've heard the command in the guild. But maybe we'll add
            // a version that accepts a channel argument.
            if (!channelPermissions.ViewChannel)
            {
                builder.AppendLine(
                    "> - Cannot view the channel. Add the \"Read Text Channels & See Voice Channels\" permission in " +
                    "the guild setting or \"Read Messages\" in the channel settings.");
            }

            if (!channelPermissions.SendMessages)
            {
                builder.AppendLine("> - Cannot send messages in the channel. Add the \"Send Messages\" permission to " +
                    "the role in the guild or channel settings.");
            }

            if (!channelPermissions.EmbedLinks)
            {
                builder.AppendLine("> - Cannot add embeds. Add the \"Embed Links\" permission in the guild or " +
                    "channel settings.");
            }

            ulong? voiceChannelId;
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                voiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(this.context.Channel.Id);
            }

            if (voiceChannelId.HasValue)
            {
                IVoiceChannel pairedVoiceChannel = await this.context.Guild.GetVoiceChannelAsync(voiceChannelId.Value);
                if (pairedVoiceChannel == null)
                {
                    builder.AppendLine("> - Paired voice channel no longer exists. Please use !pairChannels to " +
                        "pair this channel to a new voice channel.");
                }
                else if (pairedVoiceChannel is IGuildChannel pairedGuildChannel &&
                    !guildBotUser.GetPermissions(pairedGuildChannel).MuteMembers)
                {
                    builder.AppendLine($"> - Cannot mute reader in paired voice channel \"{pairedGuildChannel.Name}\"." +
                        " Please add the \"Mute Members\" permission to the role in the guild or channel settings.");
                }
            }

            if (builder.Length == 0)
            {
                await this.context.Channel.SendMessageAsync("All permissions are set up correctly.");
                return;
            }

            bool sendDm = !channelPermissions.ViewChannel || !channelPermissions.SendMessages;
            if (sendDm)
            {
                await this.context.User.SendMessageAsync(builder.ToString());
                return;
            }

            await this.context.Channel.SendMessageAsync(builder.ToString());
        }

        // This converts the channel pair mappings in the config file into database entries
        public async Task MapConfigToDatabase()
        {
#pragma warning disable CS0618 // Type or member is obsolete. This method is used to help users move away from this obsolete setting
            IDictionary<string, ChannelPair[]> guildChannelPairs = this.options.CurrentValue.SupportedChannels;
#pragma warning restore CS0618 // Type or member is obsolete
            if (guildChannelPairs == null)
            {
                return;
            }

            IReadOnlyCollection<IGuild> guilds = await this.context.Client.GetGuildsAsync();
            IEnumerable<(IGuild, ChannelPair[])> guildsWithPairs = guildChannelPairs
                .Join(
                    guilds,
                    kvp => kvp.Key,
                    guild => guild.Name,
                    (kvp, guild) => (guild, kvp.Value));

            List<(ulong textChannelId, ulong voiceChannelId)> pairChannels = new List<(ulong, ulong)>();
            IReadOnlyCollection<ITextChannel> textChannels = await this.context.Guild.GetTextChannelsAsync();
            IReadOnlyCollection<IVoiceChannel> voiceChannels = await this.context.Guild.GetVoiceChannelsAsync();

            using (DatabaseAction action = this.dbActionFactory.Create())
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

            await this.context.Channel.SendMessageAsync("Configurations mapped for all servers the bot is in.");
        }

        public async Task GetPairedChannel(ITextChannel textChannel)
        {
            if (textChannel == null)
            {
                this.logger.Information($"Null text channel passed in to GetPairedChannel");
                return;
            }

            ulong? voiceChannelId;
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                voiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannel.Id);
            }

            if (voiceChannelId == null)
            {
                await this.context.Channel.SendMessageAsync("Channel isn't paired");
                return;
            }

            IVoiceChannel voiceChannel = await this.context.Guild.GetVoiceChannelAsync(voiceChannelId.Value);
            string message = voiceChannel == null ?
                "The paired voice channel no longer exists" :
                @$"Paired voice channel: ""{voiceChannel.Name}""";
            await this.context.Channel.SendMessageAsync(message);
        }

        public async Task PairChannels(ITextChannel textChannel, string voiceChannelName)
        {
            if (textChannel == null || voiceChannelName == null)
            {
                this.logger.Information($"Null text channel or voice channel name passed in to PairChannels");
                return;
            }

            IReadOnlyCollection<IVoiceChannel> voiceChannels = await this.context.Guild.GetVoiceChannelsAsync();
            IVoiceChannel voiceChannel = voiceChannels
                .FirstOrDefault(channel => channel.Name.Trim().Equals(
                    voiceChannelName.Trim(), StringComparison.InvariantCultureIgnoreCase));
            if (voiceChannel == null)
            {
                this.logger.Information("Could not find voice channel with the given name");
                await this.context.Channel.SendMessageAsync("Cannot find a voice channel with that name");
                return;
            }

            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                await action.PairChannelsAsync(this.context.Guild.Id, textChannel.Id, voiceChannel.Id);
            }

            this.logger.Information($"Channels {textChannel.Id} and {voiceChannel.Id} paired successfully");
            await this.context.Channel.SendMessageAsync("Text and voice channel paired successfully");
        }

        public async Task UnpairChannel(ITextChannel textChannel)
        {
            if (textChannel == null)
            {
                this.logger.Information($"Null text channel name passed in to UnpairChannels");
                return;
            }

            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                await action.UnpairChannelAsync(textChannel.Id);
            }

            this.logger.Information($"Channel {textChannel.Id} unpaired successfully");
            await this.context.Channel.SendMessageAsync("Text and voice channel unpaired successfully");
        }

        public async Task ClearTeamRolePrefix()
        {
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                await action.ClearTeamRolePrefixAsync(this.context.Guild.Id);
            }

            this.logger.Information($"Team prefix cleared in guild {this.context.Guild.Id}");
            await this.context.Channel.SendMessageAsync("Prefix unset. Roles no longer determine who is on a team.");
        }

        public async Task GetTeamRolePrefix()
        {
            string prefix;
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                prefix = await action.GetTeamRolePrefixAsync(this.context.Guild.Id);
            }

            string message = prefix == null ? "No team prefix used" : @$"Team prefix: ""{prefix}""";
            await this.context.Channel.SendMessageAsync(message);
        }

        public async Task SetTeamRolePrefix(string prefix)
        {
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                await action.SetTeamRolePrefixAsync(this.context.Guild.Id, prefix);
            }

            this.logger.Information($"Team prefix set in guild {this.context.Guild.Id}");
            await this.context.Channel.SendMessageAsync(
                @$"Prefix set. Players who have the same role starting with ""{prefix}"" will be on the same team.");
        }

        public async Task SetReader()
        {
            IGuildUser user = await this.context.Guild.GetUserAsync(this.context.User.Id);
            if (user == null)
            {
                // If the reader doesn't exist anymore, don't start a game.
                return;
            }

            GameState state = this.currentGame;
            if (state == null && !this.manager.TryCreate(this.context.Channel.Id, out state))
            {
                // Couldn't add a new reader.
                return;
            }
            else if (state.ReaderId != null)
            {
                // We already have a reader, so do nothing.
                return;
            }

            state.ReaderId = this.context.User.Id;

            if (this.context.Channel is IGuildChannel guildChannel)
            {
                this.logger.Information(
                     "Game started in guild '{0}' in channel '{1}'", guildChannel.Guild.Name, guildChannel.Name);
            }

            // Prevent a cold start on the first buzz, and eagerly get the team prefix and channel pair
            Task[] dbTasks = new Task[2];
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                dbTasks[0] = action.GetPairedVoiceChannelIdOrNullAsync(this.context.Channel.Id);
                dbTasks[1] = action.GetTeamRolePrefixAsync(this.context.Guild.Id);
            }

            await Task.WhenAll(dbTasks);

            string message = this.options.CurrentValue.WebBaseURL == null ?
                $"{this.context.User.Mention} is the reader." :
                $"{this.context.User.Mention} is the reader. Please visit {this.options.CurrentValue.WebBaseURL}?{this.context.Channel.Id} to hear buzzes.";
            await this.context.Channel.SendMessageAsync(message);
        }

        public async Task SetNewReader(ulong newReaderId)
        {
            IGuildUser newReader = await this.context.Guild.GetUserAsync(newReaderId);
            if (newReader != null)
            {
                this.currentGame.ReaderId = newReaderId;
                await this.context.Channel.SendMessageAsync($"{newReader.Mention} is now the reader.");
                return;
            }

            if (this.context.Channel is IGuildChannel guildChannel)
            {
                this.logger.Information(
                    "New reader called in guild '{0}' in channel '{1}' with ID that could not be found: {2}",
                    guildChannel.Guild.Name,
                    guildChannel.Name,
                    newReaderId);
            }

            await this.context.Channel.SendMessageAsync($"User could not be found. Could not set the new reader.");
        }

        public Task Clear()
        {
            if (this.currentGame != null)
            {
                this.currentGame.ClearCurrentRound();
            }

            return Task.CompletedTask;
        }

        public async Task ClearAll()
        {
            if (this.currentGame != null && this.manager.TryRemove(this.context.Channel.Id))
            {
                this.currentGame.ClearAll();

                if (this.context.Channel is IGuildChannel guildChannel)
                {
                    this.logger.Information(
                        "Game ended in guild '{0}' in channel '{0}'", guildChannel.Guild.Name, guildChannel.Name);
                }

                await this.context.Channel.SendMessageAsync($"Reading over. All stats cleared.");
            }
        }

        public async Task GetScore()
        {
            if (this.currentGame?.ReaderId != null)
            {
                if (!(this.context.Channel is IGuildChannel guildChannel))
                {
                    return;
                }

                IGuildUser guildBotUser = await this.context.Guild.GetCurrentUserAsync();
                ChannelPermissions channelPermissions = guildBotUser.GetPermissions(guildChannel);
                if (!channelPermissions.EmbedLinks)
                {
                    await this.context.Channel.SendMessageAsync(
                        "This bot must have \"Embed Links\" permissions to show the score");
                    return;
                }

                IEnumerable<KeyValuePair<ulong, int>> scores = this.currentGame.GetScores();

                EmbedBuilder builder = new EmbedBuilder
                {
                    Title = scores.Take(checked(GameState.ScoresListLimit + 1)).Count() > GameState.ScoresListLimit ?
                    $"Top {GameState.ScoresListLimit} Scores" :
                    "Scores"
                };
                builder.WithColor(Color.Gold);
                foreach (KeyValuePair<ulong, int> score in scores.Take(GameState.ScoresListLimit))
                {
                    // TODO: Look into moving away from using await in the foreach loop. Maybe use AsyncEnumerable
                    // and do 2-3 lookups at once? The problem is we need the values added in order.
                    IGuildUser user = await this.context.Guild.GetUserAsync(score.Key);
                    string name = user == null ? "<Unknown>" : user.Nickname ?? user.Username;
                    builder.AddField(name, score.Value.ToString(CultureInfo.InvariantCulture));
                }

                Embed embed = builder.Build();
                await this.context.Channel.SendMessageAsync(embed: embed);
            }
        }

        public Task NextQuestion()
        {
            if (this.currentGame != null)
            {
                this.currentGame.NextQuestion();
            }

            return Task.CompletedTask;
        }

        public async Task Undo()
        {
            if (this.currentGame != null && this.currentGame.Undo(out ulong userId))
            {
                IGuildUser user = await this.context.Guild.GetUserAsync(userId);
                string name;
                string message;
                if (user == null)
                {
                    // TODO: Need to test this case
                    // Also unsure if this is really applicable. Could use status, but some people may play while
                    // appearing offline.
                    name = "<Unknown>";

                    // Need to remove player from queue too, since they cannot answer
                    // Maybe we need to find the next player in the queue?
                    this.currentGame.WithdrawPlayer(userId);
                    string nextPlayerMention = null;
                    while (this.currentGame.TryGetNextPlayer(out ulong nextPlayerId))
                    {
                        IGuildUser nextPlayerUser = await this.context.Guild.GetUserAsync(userId);
                        if (nextPlayerUser != null)
                        {
                            nextPlayerMention = nextPlayerUser.Mention;
                            break;
                        }

                        // Player isn't here, so withdraw them
                        this.currentGame.WithdrawPlayer(nextPlayerId);
                    }

                    message = nextPlayerMention != null ?
                        $"Undid scoring for {name}. {nextPlayerMention}. your answer?" :
                        $"Undid scoring for {name}.";
                }
                else
                {
                    name = user.Nickname ?? user.Username;
                    message = $"Undid scoring for {name}. {user.Mention}, your answer?";
                }

                await this.context.Channel.SendMessageAsync(message);
            }
        }
    }
}
