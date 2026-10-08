using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public static class PromptHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(PromptHandler));

        public static Task PromptNextPlayerAsync(
            IInteractionContext context,
            GameState state,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext,
            bool respondToInteraction = true)
        {
            if (!(context.Channel is ITextChannel textChannel))
            {
                return Task.CompletedTask;
            }

            return PromptNextPlayerAsync(textChannel, context.Client.CurrentUser.Id, state, options,
                dbActionFactory, hubContext, respondToInteraction ? context.Interaction : null);
        }

        public static async Task<bool> WithdrawPlayerAsync(
            ITextChannel textChannel,
            ulong botId,
            GameState state,
            ulong userId,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext)
        {
            if (!state.TryGetNextPlayer(out ulong nextPlayerId) || !await state.WithdrawPlayer(userId))
            {
                return false;
            }

            if (nextPlayerId == userId)
            {
                await PromptNextPlayerAsync(textChannel, botId, state, options, dbActionFactory, hubContext);
            }

            return true;
        }

        public static async Task PromptNextPlayerAsync(
            ITextChannel textChannel,
            ulong botId,
            GameState state,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext,
            IDiscordInteraction interaction = null)
        {

            if (!state.TryGetNextPlayer(out ulong userId))
            {
                await hubContext.Clients.Group(GroupFromChannel(textChannel)).SendAsync("Clear");
                return;
            }

            IGuildUser user = await textChannel.Guild.GetUserAsync(userId);
            string teamId = await state.TeamManager.GetTeamIdOrNull(user.Id);
            string teamNameReference = await GetTeamNameForMessage(state, teamId);
            Task sendMessage = interaction != null ?
                interaction.RespondOrFollowupAsync($"{user.Mention}{teamNameReference}") :
                textChannel.SendMessageAsync($"{user.Mention}{teamNameReference}");
            Task alertWebSocket = hubContext.Clients.Group(GroupFromChannel(textChannel))
                .SendAsync("PlayerBuzz", $"{user.Nickname ?? user.Username}{teamNameReference}");
            Task<Tuple<IVoiceChannel, IGuildUser>> getVoiceChannelReaderPair = MuteReader(
                botId, textChannel, dbActionFactory, state.ReaderId);
            await Task.WhenAll(getVoiceChannelReaderPair, sendMessage, alertWebSocket);

            Tuple<IVoiceChannel, IGuildUser> voiceChannelReaderPair = await getVoiceChannelReaderPair;
            if (voiceChannelReaderPair != null)
            {
                // We want to run this on a separate thread and not block the event handler
                _ = Task.Run(() => UnmuteReaderAfterDelayAsync(
                    voiceChannelReaderPair.Item1, voiceChannelReaderPair.Item2, options));
            }
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

        private static async Task<Tuple<IVoiceChannel, IGuildUser>> MuteReader(
            ulong botId, ITextChannel textChannel, IDatabaseActionFactory databaseActionFactory, ulong? readerId)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            ulong? voiceChannelId = null;
            using (DatabaseAction action = databaseActionFactory.Create())
            {
                voiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannel.Id);
            }

            stopwatch.Stop();
            Logger.Verbose($"Time to get paired channel: {stopwatch.ElapsedMilliseconds} ms");

            if (voiceChannelId == null)
            {
                return null;
            }

            IVoiceChannel voiceChannel = await textChannel.Guild.GetVoiceChannelAsync(voiceChannelId.Value);
            if (voiceChannel == null)
            {
                return null;
            }

            IGuildUser botUser = await textChannel.GetUserAsync(botId);
            if (!botUser.GetPermissions(voiceChannel).MuteMembers)
            {
                return null;
            }

            IGuildUser reader = await textChannel.GetUserAsync(readerId.Value);
            try
            {
                // Make sure the reader didn't mute themselves or leave the voice channel
                // reader can be null here!? Maybe it's the cache story?
                if (reader != null && !reader.IsSelfMuted && reader.VoiceChannel?.Id == voiceChannel.Id)
                {
                    await reader.ModifyAsync(properties => properties.Mute = true);
                }
            }
            catch (HttpException ex)
            {
                if (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Logger.Error(
                        $"Couldn't mute reader because bot doesn't have Mute permission in guild '{voiceChannel.Guild.Name}'");
                }

                return null;
            }

            return new Tuple<IVoiceChannel, IGuildUser>(voiceChannel, reader);
        }

        private static async Task UnmuteReaderAfterDelayAsync(
            IVoiceChannel voiceChannel, IGuildUser reader, IOptionsMonitor<BotConfiguration> options)
        {
            await Task.Delay(options.CurrentValue.MuteDelayMs);

            // Make sure the reader didn't mute themselves or leave the voice channel
            if (!reader.IsSelfMuted && reader.VoiceChannel?.Id == voiceChannel.Id)
            {
                await reader.ModifyAsync(properties => properties.Mute = false);
            }
        }
    }
}
