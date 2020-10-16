using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker
{
    public class MessageHandler
    {
        private static readonly Regex BuzzRegex = new Regex(
            "^\\s*bu?z+\\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WithdrawRegex = new Regex(
            "^\\s*wd\\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public MessageHandler(
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext,
            ILogger logger)
        {
            Verify.IsNotNull(options, nameof(options));

            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
            this.HubContext = hubContext;
            this.Logger = logger;

            // TODO: Rewrite this so that
            // #1: buzz emojis are server-dependent, since emojis are
            // #2: This can be updated if the config file is refreshed.
            // Alternatively, this should be a guild-dependent setting
            this.BuzzEmojisRegex = BuildBuzzEmojiRegexes(options.CurrentValue);
        }

        private IEnumerable<Regex> BuzzEmojisRegex { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IHubContext<MonitorHub> HubContext { get; }

        private ILogger Logger { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        // TODO: Investigate if it makes sense to move the logic for deciding if we have a command here

        public async Task HandlePlayerMessage(
            GameState state, IGuildUser messageAuthor, ITextChannel channel, string messageContent)
        {
            Verify.IsNotNull(state, nameof(state));
            Verify.IsNotNull(messageAuthor, nameof(messageAuthor));
            Verify.IsNotNull(channel, nameof(channel));
            Verify.IsNotNull(messageContent, nameof(messageContent));

            // Player has buzzed in
            string playerDisplayName = messageAuthor.Nickname ?? messageAuthor.Username;
            string teamId = await state.TeamManager.GetTeamIdOrNull(messageAuthor.Id);
            ulong nextPlayerId;

            if (this.IsBuzz(messageContent) && await state.AddPlayer(messageAuthor.Id, playerDisplayName))
            {
                if (state.TryGetNextPlayer(out nextPlayerId) && nextPlayerId == messageAuthor.Id)
                {
                    await this.PromptNextPlayerAsync(state, channel);
                }

                return;
            }

            // See if the player has withdrawn. We want to check if the author is at the top of the queue to see if we
            // want to send a message about the withdrawl
            if (WithdrawRegex.IsMatch(messageContent) &&
                state.TryGetNextPlayer(out nextPlayerId) &&
                await state.WithdrawPlayer(messageAuthor.Id) &&
                nextPlayerId == messageAuthor.Id)
            {
                if (state.TryGetNextPlayer(out _))
                {
                    // There's another player, so prompt them
                    await this.PromptNextPlayerAsync(state, channel);
                }
                else
                {
                    // If there are no players in the queue, have the bot recognize the withdrawl
                    IGuildUser messageUser = await channel.Guild.GetUserAsync(messageAuthor.Id);
                    string teamNameReference = await GetTeamNameForMessage(state, teamId);
                    await channel.SendMessageAsync($"{messageUser.Mention}{teamNameReference} has withdrawn.");
                }
            }
        }

        public Task<bool> TryScore(
            GameState state, IGuildUser messageAuthor, ITextChannel channel, string messageContent)
        {
            Verify.IsNotNull(state, nameof(state));
            Verify.IsNotNull(messageAuthor, nameof(messageAuthor));
            Verify.IsNotNull(channel, nameof(channel));
            Verify.IsNotNull(messageContent, nameof(messageContent));

            if (state.ReaderId != messageAuthor.Id)
            {
                return Task.FromResult(false);
            }

            if (state.PhaseNumber >= GameState.MaximumPhasesCount)
            {
                channel.SendMessageAsync($"Reached the limit for games ({GameState.MaximumPhasesCount} questions)");
                return Task.FromResult(false);
            }

            return state.CurrentStage switch
            {
                PhaseStage.Tossup => this.TryScoreBuzz(state, channel, messageContent),
                PhaseStage.Bonus => TryScoreBonus(state, channel, messageContent),

                // Can't score when the game is over
                _ => Task.FromResult(false),
            };
        }

        private static async Task<bool> TryScoreBonus(
            GameState state, IMessageChannel channel, string messageContent)
        {
            if (!state.TryScoreBonus(messageContent))
            {
                return false;
            }

            await channel.SendMessageAsync($"**TU {state.PhaseNumber}**");
            return true;
        }

        private static IEnumerable<Regex> BuildBuzzEmojiRegexes(BotConfiguration options)
        {
            if (options.BuzzEmojis == null)
            {
                return Array.Empty<Regex>();
            }

            List<Regex> result = new List<Regex>();
            StringBuilder builder = new StringBuilder();
            foreach (string buzzEmoji in options.BuzzEmojis)
            {
                builder.Append("^<");
                builder.Append(buzzEmoji);
                builder.Append("\\d+>$");
                Regex regex = new Regex(builder.ToString());
                result.Add(regex);
                builder.Clear();
            }

            return result;
        }

        private static string GroupFromChannel(ITextChannel channel)
        {
            return channel.Id.ToString(CultureInfo.InvariantCulture);
        }

        private static async Task<string> GetTeamNameForMessage(GameState state, string teamId)
        {
            return teamId != null && (await state.TeamManager.GetTeamIdToNames()).TryGetValue(teamId, out string teamName) ?
                $" ({teamName.Trim()})" :
                string.Empty;
        }

        private bool IsBuzz(string buzzText)
        {
            return BuzzRegex.IsMatch(buzzText) || this.BuzzEmojisRegex.Any(regex => regex.IsMatch(buzzText));
        }

        private async Task<Tuple<IVoiceChannel, IGuildUser>> MuteReader(ITextChannel textChannel, ulong? readerId)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            ulong? voiceChannelId = null;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                voiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannel.Id);
            }

            stopwatch.Stop();
            this.Logger.Verbose($"Time to get paired channel: {stopwatch.ElapsedMilliseconds} ms");

            if (voiceChannelId == null)
            {
                return null;
            }

            IVoiceChannel voiceChannel = await textChannel.Guild.GetVoiceChannelAsync(voiceChannelId.Value);
            if (voiceChannel == null)
            {
                return null;
            }

            IGuildUser reader = await textChannel.Guild.GetUserAsync(readerId.Value);
            try
            {
                // Make sure the reader didn't mute themselves or leave the voice channel
                if (!reader.IsSelfMuted && reader.VoiceChannel?.Id == voiceChannel.Id)
                {
                    await reader.ModifyAsync(properties => properties.Mute = true);
                }
            }
            catch (HttpException ex)
            {
                if (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
                {
                    this.Logger.Error(
                        $"Couldn't deafen reader because bot doesn't have Mute permission in guild '{voiceChannel.Guild.Name}'");
                }

                return null;
            }

            return new Tuple<IVoiceChannel, IGuildUser>(voiceChannel, reader);
        }

        private async Task PromptNextPlayerAsync(GameState state, ITextChannel textChannel)
        {
            if (!state.TryGetNextPlayer(out ulong userId))
            {
                await this.HubContext.Clients.Group(GroupFromChannel(textChannel)).SendAsync("Clear");
                return;
            }

            IGuildUser user = await textChannel.Guild.GetUserAsync(userId);
            string teamId = await state.TeamManager.GetTeamIdOrNull(user.Id);

            Task<Tuple<IVoiceChannel, IGuildUser>> getVoiceChannelReaderPair = this.MuteReader(
                textChannel, state.ReaderId);

            string teamNameReference = await GetTeamNameForMessage(state, teamId);
            Task sendMessage = textChannel.SendMessageAsync($"{user.Mention}{teamNameReference}");
            Task alertWebSocket = this.HubContext.Clients.Group(GroupFromChannel(textChannel))
                .SendAsync("PlayerBuzz", $"{user.Nickname ?? user.Username}{teamNameReference}");
            await Task.WhenAll(getVoiceChannelReaderPair, sendMessage, alertWebSocket);

            Tuple<IVoiceChannel, IGuildUser> voiceChannelReaderPair = getVoiceChannelReaderPair.Result;
            if (voiceChannelReaderPair != null)
            {
                // We want to run this on a separate thread and not block the event handler
                _ = Task.Run(() => this.UnmuteReaderAfterDelayAsync(voiceChannelReaderPair.Item1, voiceChannelReaderPair.Item2));
            }
        }

        private async Task<bool> TryScoreBuzz(
            GameState state, ITextChannel channel, string messageContent)
        {
            if (!state.TryGetNextPlayer(out _))
            {
                return false;
            }

            if (int.TryParse(messageContent, out int points))
            {
                // Go back to only accepting -5/0/10/15/20, since we need to track splits now
                switch (points)
                {
                    case -5:
                    case 0:
                    case 10:
                    case 15:
                    case 20:
                        state.ScorePlayer(points);
                        await this.PromptNextPlayerAsync(state, channel);
                        if (points > 0)
                        {
                            if (state.CurrentStage == PhaseStage.Bonus)
                            {
                                await channel.SendMessageAsync($"**Bonus for {state.PhaseNumber}**");
                            }
                            else if (state.CurrentStage == PhaseStage.Tossup)
                            {
                                await channel.SendMessageAsync($"**TU {state.PhaseNumber}**");
                            }
                        }

                        return true;
                    default:
                        break;
                }
            }
            else if (messageContent.Trim() == "no penalty")
            {
                state.ScorePlayer(0);
                await this.PromptNextPlayerAsync(state, channel);
                return true;
            }

            return false;
        }

        private async Task UnmuteReaderAfterDelayAsync(IVoiceChannel voiceChannel, IGuildUser reader)
        {
            await Task.Delay(this.Options.CurrentValue.MuteDelayMs);

            // Make sure the reader didn't mute themselves or leave the voice channel
            if (!reader.IsSelfMuted && reader.VoiceChannel?.Id == voiceChannel.Id)
            {
                await reader.ModifyAsync(properties => properties.Mute = false);
            }
        }
    }
}
