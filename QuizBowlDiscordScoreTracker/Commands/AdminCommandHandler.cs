using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    // Use command handler classes to simplify testing, since ModuleBase classes require lots of setup around parsing
    // parameter results and setting up dependency injection
    public class AdminCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(AdminCommandHandler));

        public AdminCommandHandler(ICommandContext context, IDatabaseActionFactory dbActionFactory)
        {
            this.Context = context;
            this.DatabaseActionFactory = dbActionFactory;
        }

        private ICommandContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        public async Task CheckPermissionsAsync(IMessageChannel messageChannel)
        {
            messageChannel ??= this.Context.Channel;
            if (!(messageChannel is IGuildChannel guildChannel))
            {
                return;
            }

            IGuildUser guildBotUser = await this.Context.Guild.GetCurrentUserAsync();
            ChannelPermissions channelPermissions = guildBotUser.GetPermissions(guildChannel);
            ChannelPermissions contextChannelPermissions = guildBotUser.GetPermissions(this.Context.Channel as IGuildChannel);

            StringBuilder builder = new StringBuilder();
            
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

            if (!channelPermissions.AttachFiles)
            {
                builder.AppendLine("> - Cannot attach files, so !exportToFile will fail. Add the \"Attach Files\" " +
                    "permission in the guild or channel settings.");
            }

            ulong? voiceChannelId;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                voiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(guildChannel.Id);
            }

            if (voiceChannelId.HasValue)
            {
                IVoiceChannel pairedVoiceChannel = await this.Context.Guild.GetVoiceChannelAsync(voiceChannelId.Value);
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
                builder.AppendLine("All permissions are set up correctly.");
            }

            // This does not need to check for ViewChannel from the context, as we are responding
            // to the command
            bool sendDm = !contextChannelPermissions.SendMessages;
            if (sendDm)
            {
                await this.Context.User.SendMessageAsync(builder.ToString());
                return;
            }

            await this.Context.Channel.SendMessageAsync(builder.ToString());
        }

        public async Task ClearReaderRolePrefixAsync()
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.ClearReaderRolePrefixAsync(this.Context.Guild.Id);
            }

            Logger.Information($"Reader prefix cleared in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync("Prefix unset. Roles no longer determine who can use !read.");
        }

        public async Task ClearTeamRolePrefixAsync()
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.ClearTeamRolePrefixAsync(this.Context.Guild.Id);
            }

            Logger.Information($"Team prefix cleared in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync("Prefix unset. Roles no longer determine who is on a team.");
        }

        public async Task DisableBonusesByDefaultAsync()
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.SetUseBonuses(this.Context.Guild.Id, false);
            }

            Logger.Information($"Use Bonuses set to false in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync(
                "Scoring bonuses will no longer be enabled for every game in this server.");
        }

        public async Task EnableBonusesByDefaultAsync()
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.SetUseBonuses(this.Context.Guild.Id, true);
            }

            Logger.Information($"Use Bonuses set to true in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync("Scoring bonuses is now enabled for every game in this server.");
        }

        public async Task GetDefaultFormatAsync()
        {
            bool useBonuses;
            string readerRolePrefix;
            string teamRolePrefix;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                // TODO: Should we make this one call so we don't need to await on all of them (and we could do it
                // with one SELECT instead of 3)?
                useBonuses = await action.GetUseBonuses(this.Context.Guild.Id);
                readerRolePrefix = await action.GetReaderRolePrefixAsync(this.Context.Guild.Id);
                teamRolePrefix = await action.GetTeamRolePrefixAsync(this.Context.Guild.Id);
            }

            Logger.Information($"getFormat called in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            EmbedBuilder builder = new EmbedBuilder()
            {
                Title = "Default Format",
                Description = "The default settings for games in this server"
            };
            builder.AddField("Require scoring bonuses?", useBonuses ? "Yes" : "No");
            builder.AddField("Reader role prefix?", readerRolePrefix == null ? "None set" : @$"Yes: ""{readerRolePrefix}""");
            builder.AddField("Team role prefix?", teamRolePrefix == null ? "None set" : @$"Yes: ""{teamRolePrefix}""");

            await this.Context.Channel.SendMessageAsync(embed: builder.Build());
        }

        public async Task GetPairedChannelAsync([Summary("Text channel mention (#textChannelName)")] ITextChannel textChannel)
        {
            if (textChannel == null)
            {
                Logger.Information($"Null text channel passed in to GetPairedChannel");
                return;
            }

            ulong? voiceChannelId;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                voiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannel.Id);
            }

            if (voiceChannelId == null)
            {
                await this.Context.Channel.SendMessageAsync("Channel isn't paired");
                return;
            }

            IVoiceChannel voiceChannel = await this.Context.Guild.GetVoiceChannelAsync(voiceChannelId.Value);
            string message = voiceChannel == null ?
                "The paired voice channel no longer exists" :
                @$"Paired voice channel: ""{voiceChannel.Name}""";
            await this.Context.Channel.SendMessageAsync(message);
        }

        public async Task GetReaderRolePrefixAsync()
        {
            string prefix;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                prefix = await action.GetReaderRolePrefixAsync(this.Context.Guild.Id);
            }

            string message = prefix == null ? "No reader prefix used" : @$"Reader prefix: ""{prefix}""";
            await this.Context.Channel.SendMessageAsync(message);
        }

        public async Task GetTeamRolePrefixAsync()
        {
            string prefix;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                prefix = await action.GetTeamRolePrefixAsync(this.Context.Guild.Id);
            }

            string message = prefix == null ? "No team prefix used" : @$"Team prefix: ""{prefix}""";
            await this.Context.Channel.SendMessageAsync(message);
        }

        public async Task PairChannelsAsync(ITextChannel textChannel, string voiceChannelName)
        {
            if (textChannel == null || voiceChannelName == null)
            {
                Logger.Information($"Null text channel or voice channel name passed in to PairChannels");
                return;
            }

            IReadOnlyCollection<IVoiceChannel> voiceChannels = await this.Context.Guild.GetVoiceChannelsAsync();
            IVoiceChannel voiceChannel = voiceChannels
                .FirstOrDefault(channel => channel.Name.Trim().Equals(
                    voiceChannelName.Trim(), StringComparison.InvariantCultureIgnoreCase));
            if (voiceChannel == null)
            {
                Logger.Information("Could not find voice channel with the given name");
                await this.Context.Channel.SendMessageAsync("Cannot find a voice channel with that name");
                return;
            }

            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.PairChannelsAsync(this.Context.Guild.Id, textChannel.Id, voiceChannel.Id);
            }

            Logger.Information(
                $"Channels {textChannel.Id} and {voiceChannel.Id} paired successfully by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync("Text and voice channel paired successfully");
        }

        public async Task SetReaderRolePrefixAsync(string prefix)
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.SetReaderRolePrefixAsync(this.Context.Guild.Id, prefix);
            }

            Logger.Information($"Reader prefix set in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync(
                @$"Prefix set. Only users who have arole starting with ""{prefix}"" will be able to use !read.");
        }

        public async Task SetTeamRolePrefixAsync(string prefix)
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.SetTeamRolePrefixAsync(this.Context.Guild.Id, prefix);
            }

            Logger.Information($"Team prefix set in guild {this.Context.Guild.Id} by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync(
                @$"Prefix set. Players who have the same role starting with ""{prefix}"" will be on the same team.");
        }

        public async Task UnpairChannelAsync(ITextChannel textChannel)
        {
            if (textChannel == null)
            {
                Logger.Information($"Null text channel name passed in to UnpairChannels");
                return;
            }

            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.UnpairChannelAsync(textChannel.Id);
            }

            Logger.Information($"Channel {textChannel.Id} unpaired successfully by user {this.Context.User.Id}");
            await this.Context.Channel.SendMessageAsync("Text and voice channel unpaired successfully");
        }
    }
}
