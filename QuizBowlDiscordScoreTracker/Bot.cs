using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker
{
    public sealed class Bot : BackgroundService
    {
        // TODO: We may need a lock for this, and this lock would need to be accessible form BotCommands. We could wrap
        // this in an object which would do the locking for us.
        private readonly GameStateManager gameStateManager;
        private readonly IOptionsMonitor<BotConfiguration> options;
        private readonly DiscordSocketClient client;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger logger;
        private readonly DiscordNetEventLogger discordNetEventLogger;
        private readonly IDisposable configurationChangeCallback;
        private readonly IDatabaseActionFactory dbActionFactory;
        private readonly MessageHandler messageHandler;

        [SuppressMessage("Code Quality", "CA2213:Disposable fields should be disposed", Justification = "Dispose method is inaccessible")]
        private readonly CommandService commandService;

        private readonly Dictionary<IGuildUser, bool> readerRejoinedMap;
        private readonly object readerRejoinedMapLock = new object();

        private bool isDisposed;

        public Bot(IOptionsMonitor<BotConfiguration> options, IHubContext<MonitorHub> hubContext)
        {
            this.gameStateManager = new GameStateManager();
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.dbActionFactory = new SqliteDatabaseActionFactory(this.options.CurrentValue.DatabaseDataSource);
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
            serviceCollection.AddSingleton(this.options);
            serviceCollection.AddSingleton(this.dbActionFactory);
            serviceCollection.AddSingleton<IFileScoresheetGenerator>(new ExcelFileScoresheetGenerator());
            this.serviceProvider = serviceCollection.BuildServiceProvider();

            this.commandService = new CommandService(new CommandServiceConfig()
            {
                CaseSensitiveCommands = false,
                LogLevel = LogSeverity.Info,
                DefaultRunMode = RunMode.Async,
            });
            this.commandService.Log += this.OnLogAsync;

            this.logger = Log.ForContext(this.GetType());
            this.discordNetEventLogger = new DiscordNetEventLogger(this.client, this.commandService);

            Task.WaitAll(this.commandService.AddModulesAsync(Assembly.GetExecutingAssembly(), this.serviceProvider));

            this.messageHandler = new MessageHandler(this.options, this.dbActionFactory, hubContext, this.logger);

            this.client.MessageReceived += this.OnMessageCreated;
            this.client.GuildMemberUpdated += this.OnPresenceUpdated;
            this.client.JoinedGuild += this.OnGuildJoined;

            this.configurationChangeCallback = this.options.OnChange((configuration, value) =>
            {
                this.logger.Information("Configuration has been reloaded");
            });
        }

        public override void Dispose()
        {
            if (this.isDisposed)
            {
                return;
            }

            this.isDisposed = true;
            this.client.MessageReceived -= this.OnMessageCreated;
            this.client.GuildMemberUpdated -= this.OnPresenceUpdated;
            this.discordNetEventLogger.Dispose();
            this.configurationChangeCallback.Dispose();
            this.client.Dispose();
            base.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Make sure the database exists
            using (DatabaseAction action = this.dbActionFactory.Create())
            {
                await action.MigrateAsync();
            }

            stoppingToken.ThrowIfCancellationRequested();

            // TODO: If we go to a more proper service architecture, move more of the initialization logic from the
            // constructor to here, since we could start/stop the client multiple times.
            string token = this.options.CurrentValue.BotToken;
            await this.client.LoginAsync(TokenType.Bot, token);
            stoppingToken.ThrowIfCancellationRequested();
            await this.client.StartAsync();
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            this.Dispose();
            return base.StopAsync(cancellationToken);
        }

        private Task OnGuildJoined(SocketGuild guild)
        {
            return guild.DefaultChannel.SendMessageAsync(
                "Thank you for adding the QuizBowlScoreTracker bot to your server. Post *!help* to see a list of commands that the bot supports.");
        }

        private async Task OnMessageCreated(SocketMessage message)
        {
            // Ignore messages from the bot or from non user messages (in channel or DMs).
            if (message.Author.Id == this.client.CurrentUser.Id || !(message is IUserMessage userMessage))
            {
                return;
            }

            int argPosition = 0;
            if (userMessage.HasCharPrefix('!', ref argPosition))
            {
                // Make sure the user isn't banned. Don't block unban, in case of an accidental self-ban
                if (!userMessage.Content.StartsWith("!unban", StringComparison.InvariantCultureIgnoreCase))
                {
                    using (DatabaseAction action = this.dbActionFactory.Create())
                    {
                        if (await action.GetCommandBannedAsync(message.Author.Id))
                        {
                            return;
                        }
                    }
                }

                ICommandContext context = new CommandContext(this.client, userMessage);
                await this.commandService.ExecuteAsync(context, argPosition, this.serviceProvider);
                return;
            }

            // Some commands may need to be taken in DM channels. Everything for handling buzzes and scoring should be
            // on the main channel 
            if (!(userMessage.Channel is ITextChannel channel &&
                this.gameStateManager.TryGet(channel.Id, out GameState state) &&
                userMessage.Author is IGuildUser guildUser))
            {
                return;
            }

            bool answersScored = await this.messageHandler.TryScore(state, guildUser, channel, message.Content);
            if (answersScored)
            {
                return;
            }

            // Don't block on this
            _ = Task.Run(() => this.messageHandler.HandlePlayerMessage(state, guildUser, channel, message.Content));
        }

        private Task OnLogAsync(LogMessage logMessage)
        {
            if (logMessage.Exception != null)
            {
                this.logger.Error(logMessage.Exception, "Exception occurred in a command");
            }

            return Task.CompletedTask;
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
                    await Task.Delay(this.options.CurrentValue.WaitForRejoinMs);
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
                            SocketTextChannel textChannel = newUser.Guild?.GetTextChannel(pair.Key);
                            if (textChannel != null)
                            {
                                this.logger.Verbose(
                                    "Reader left game in guild '{0}' in channel '{1}'. Ending game",
                                    textChannel.Guild.Name,
                                    textChannel.Name);
                                sendResetTasks[i] = (newUser.Guild.GetTextChannel(pair.Key)).SendMessageAsync(
                                    $"Reader {newUser.Nickname ?? newUser.Username} has left. Ending the game.");
                            }
                            else
                            {
                                // There's no channel, so return null
                                sendResetTasks[i] = Task.FromResult<RestUserMessage>(null);
                            }
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
    }
}
