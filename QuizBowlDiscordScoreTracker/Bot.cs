using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTracker
{
    // TODO: Refactor this so that most of the methods are in a separate class that is easily testable.
    public sealed class Bot : IDisposable
    {
        private static readonly Regex BuzzRegex = new Regex("^bu?z+$", RegexOptions.IgnoreCase);

        // TODO: We may need a lock for this, and this lock would need to be accessible form BotCommands. We could wrap
        // this in an object which would do the locking for us.
        private readonly GameStateManager gameStateManager;
        private readonly BotConfiguration options;
        private readonly DiscordSocketClient client;
        private readonly IServiceProvider serviceProvider;
        private readonly IEnumerable<Regex> buzzEmojisRegex;

        [SuppressMessage("Code Quality", "IDE0069:Disposable fields should be disposed", Justification = "Dispose method is inaccessible")]
        private readonly CommandService commandService;

        private Dictionary<IGuildUser, bool> readerRejoinedMap;
        private object readerRejoinedMapLock = new object();

        public Bot(BotConfiguration options)
        {
            // this.gamesInChannel = new Dictionary<ulong, GameState>();
            this.gameStateManager = new GameStateManager();
            this.options = options ?? new BotConfiguration();
            this.buzzEmojisRegex = BuildBuzzEmojiRegexes(this.options);
            this.readerRejoinedMap = new Dictionary<IGuildUser, bool>();

            DiscordSocketConfig clientConfig = new DiscordSocketConfig()
            {
                // May not be needed
                MessageCacheSize = 1024 * 16
            };
            this.client = new DiscordSocketClient(clientConfig);
            IServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(this.client);
            serviceCollection.AddSingleton(this.gameStateManager);
            serviceCollection.AddSingleton(options);
            this.serviceProvider = serviceCollection.BuildServiceProvider();

            this.commandService = new CommandService(new CommandServiceConfig()
            {
                CaseSensitiveCommands = false,
                LogLevel = LogSeverity.Info
            });

            Task.WaitAll(this.commandService.AddModulesAsync(Assembly.GetExecutingAssembly(), this.serviceProvider));

            this.client.MessageReceived += this.OnMessageCreated;
            this.client.GuildMemberUpdated += this.OnPresenceUpdated;
            this.client.Log += this.LogMessageAsync;
        }

        public async Task ConnectAsync()
        {
            string token = this.options.BotToken;
            await this.client.LoginAsync(TokenType.Bot, token);
            await this.client.StartAsync();
        }

        private Task LogMessageAsync(LogMessage message)
        {
            if (message.Exception != null)
            {
                return Console.Error.WriteLineAsync(
                    $"Exception from Discord.Net: {message.Exception.Message}\nStack trace:\n{message.Exception.StackTrace}");
            }
            else if (message.Severity >= LogSeverity.Info)
            {
                return Console.Out.WriteLineAsync($"[{message.Severity}] Discord.Net message: {message.Message}");
            }
            
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (this.client != null)
            {
                this.client.MessageReceived -= this.OnMessageCreated;
                this.client.GuildMemberUpdated -= this.OnPresenceUpdated;
                this.client.Log -= this.LogMessageAsync;
                this.client.Dispose();
            }
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

        private static async Task<Tuple<IVoiceChannel, IGuildUser>> MuteReader(
            ITextChannel textChannel, string voiceChannelName, ulong? readerId)
        {
            IGuildUser reader = null;

            IReadOnlyCollection<IVoiceChannel> voiceChannels = await textChannel.Guild.GetVoiceChannelsAsync();
            IVoiceChannel voiceChannel = voiceChannels.FirstOrDefault(channel => channel.Name == voiceChannelName);
            if (voiceChannel == null)
            {
                return null;
            }

            reader = await textChannel.Guild.GetUserAsync(readerId.Value);
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
                    // TODO: When we move to using Serilog, log this
                    Console.Error.WriteLine(
                        $"Couldn't deafen reader because bot doesn't have Mute permission in guild {voiceChannel.GuildId}.");
                }

                return null;
            }

            return new Tuple<IVoiceChannel, IGuildUser>(voiceChannel, reader);
        }

        private async Task PromptNextPlayer(GameState state, ITextChannel textChannel)
        {
            if (state.TryGetNextPlayer(out ulong userId))
            {
                Tuple<IVoiceChannel, IGuildUser> voiceChannelReaderPair = null;
                if (this.options.TryGetVoiceChannelName(
                    textChannel.Guild.Name, textChannel.Name, out string voiceChannelName))
                {
                    voiceChannelReaderPair = await MuteReader(textChannel, voiceChannelName, state.ReaderId);
                }

                IGuildUser user = await textChannel.Guild.GetUserAsync(userId);
                await textChannel.SendMessageAsync(user.Mention);

                if (voiceChannelReaderPair != null)
                {
                    // We want to run this on a separate thread and not block the event handler
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(() => UnmuteReaderAfterDelay(voiceChannelReaderPair.Item1, voiceChannelReaderPair.Item2));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
            }
        }

        private async Task OnMessageCreated(SocketMessage message)
        {
            if (message.Author.Id == this.client.CurrentUser.Id ||
                !(message is IUserMessage userMessage && userMessage.Channel is ITextChannel channel))
            {
                return;
            }

            int argPosition = 0;
            if (userMessage.HasCharPrefix('!', ref argPosition))
            {
                ICommandContext context = new CommandContext(this.client, userMessage);
                await this.commandService.ExecuteAsync(context, argPosition, this.serviceProvider);
                return;
            }

            if (!this.options.IsTextSupportedChannel(channel.Guild.Name, channel.Name))
            {
                return;
            }

            if (!this.gameStateManager.TryGet(channel.Id, out GameState state))
            {
                return;
            }

            if (state.ReaderId == message.Author.Id)
            {
                switch (message.Content)
                {
                    case "-5":
                    case "0":
                    case "10":
                    case "15":
                    case "20":
                        state.ScorePlayer(int.Parse(message.Content, CultureInfo.InvariantCulture));
                        await PromptNextPlayer(state, channel);
                        return;
                    case "no penalty":
                        state.ScorePlayer(0);
                        await PromptNextPlayer(state, channel);
                        return;
                    default:
                        return;
                }
            }

            // Player has buzzed in
            if (this.IsBuzz(message.Content) && state.AddPlayer(message.Author.Id))
            {
                if (state.TryGetNextPlayer(out ulong nextPlayerId) && nextPlayerId == message.Author.Id)
                {
                    await PromptNextPlayer(state, channel);
                }

                return;
            }

            // Player has withdrawn
            if (message.Content.Equals("wd", StringComparison.CurrentCultureIgnoreCase) && 
                state.WithdrawPlayer(message.Author.Id))
            {
                if (state.TryGetNextPlayer(out ulong nextPlayerId))
                {
                    // If the player withdrawing is at the top of the queue, prompt the next player
                    if (nextPlayerId == message.Author.Id)
                    {
                        await PromptNextPlayer(state, channel);
                    }
                }
                else
                {
                    // If there are no players in the queue, have the bot recognize the withdrawl
                    IGuildUser messageUser = await channel.Guild.GetUserAsync(message.Author.Id);
                    await message.Channel.SendMessageAsync($"{messageUser.Mention} has withdrawn.");
                }

                return;
            }
        }

        private Task OnPresenceUpdated(SocketGuildUser oldUser, SocketGuildUser newUser)
        {
            IGuildUser user = newUser;
            if (user == null)
            {
                // Can't do anything, we don't know what game they were reading.
                return Task.CompletedTask;
            }

            // TODO: See if there's a way to write this method without a hacky GetGameChannelPairs method
            KeyValuePair<ulong, GameState>[] readingGames = this.gameStateManager.GetGameChannelPairs()
                .Where(kvp => kvp.Value.ReaderId == user.Id)
                .ToArray();

            if (readingGames.Length > 0)
            {
                lock (this.readerRejoinedMapLock)
                {
                    if (!this.readerRejoinedMap.TryGetValue(user, out bool hasRejoined) &&
                        newUser.Status == UserStatus.Offline)
                    {
                        this.readerRejoinedMap[user] = false;
                    }
                    else if (hasRejoined == false && newUser.Status != UserStatus.Offline)
                    {
                        this.readerRejoinedMap[user] = true;
                        return Task.CompletedTask;
                    }
                    else
                    {
                        return Task.CompletedTask;
                    }
                }

                // The if-statement is structured so that we can call Task.Delay later without holding onto the lock
                // We should only be here if the first condition was true
                Task t = new Task(async () =>
                {
                    await Task.Delay(this.options.WaitForRejoinMs);
                    bool rejoined = false;
                    lock (this.readerRejoinedMapLock)
                    {
                        this.readerRejoinedMap.TryGetValue(user, out rejoined);
                        this.readerRejoinedMap.Remove(user);
                    }

                    if (!rejoined)
                    {
                        Task<RestUserMessage>[] sendResetTasks = new Task<RestUserMessage>[readingGames.Length];
                        for (int i = 0; i < readingGames.Length; i++)
                        {
                            KeyValuePair<ulong, GameState> pair = readingGames[i];
                            pair.Value.ClearAll();
                            sendResetTasks[i] = (newUser.Guild.GetTextChannel(pair.Key)).SendMessageAsync(
                                $"Reader {newUser.Mention} has left. Ending the game.");
                        }

                        await Task.WhenAll(sendResetTasks);
                    }
                });

                t.Start();
                // This is a lie, but await seems to block the event handlers from receiving other events, so say that
                // we have completed.
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        private bool IsBuzz(string buzzText)
        {
            return BuzzRegex.IsMatch(buzzText) || this.buzzEmojisRegex.Any(regex => regex.IsMatch(buzzText));
        }

        private async Task UnmuteReaderAfterDelay(IVoiceChannel voiceChannel, IGuildUser reader)
        {
            await Task.Delay(this.options.MuteDelayMs);

            // Make sure the reader didn't mute themselves or leave the voice channel
            if (!reader.IsSelfMuted && reader.VoiceChannel?.Id == voiceChannel.Id)
            {
                await reader.ModifyAsync(properties => properties.Mute = false);
            }
        }
    }
}
