using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker
{
    public sealed class Bot : BackgroundService
    {
        internal const GatewayIntents RequiredGatewayIntents =
            GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildVoiceStates;

        // TODO: We may need a lock for this, and this lock would need to be accessible form BotCommands. We could wrap
        // this in an object which would do the locking for us.
        private readonly GameStateManager gameStateManager;
        private readonly IOptionsMonitor<BotConfiguration> options;
        private readonly IHubContext<MonitorHub> hubContext;
        private readonly DiscordSocketClient client;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger logger;
        private readonly DiscordNetEventLogger discordNetEventLogger;
        private readonly IDisposable configurationChangeCallback;

        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Needs to be an interface for dependency injection to work")]
        private readonly IDatabaseActionFactory dbActionFactory;

        private readonly InteractionService interactionService;
        private readonly SlashCommandDispatcher slashCommandDispatcher;
        private ModuleInfo[] interactionModules;

        private bool isDisposed;

        public Bot(IOptionsMonitor<BotConfiguration> options, IHubContext<MonitorHub> hubContext)
        {
            this.gameStateManager = new GameStateManager();
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.hubContext = hubContext;
            this.dbActionFactory = new SqliteDatabaseActionFactory(this.options.CurrentValue.DatabaseDataSource);
            DiscordSocketConfig clientConfig = new DiscordSocketConfig()
            {
                GatewayIntents = RequiredGatewayIntents
            };
            this.client = new DiscordSocketClient(clientConfig);
            IServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(this.client);
            serviceCollection.AddSingleton(this.gameStateManager);
            serviceCollection.AddSingleton(this.options);
            serviceCollection.AddSingleton(this.dbActionFactory);
            serviceCollection.AddSingleton(hubContext);
            serviceCollection.AddSingleton<IFileScoresheetGenerator>(new ExcelFileScoresheetGenerator());

            IGoogleSheetsApi googleSheetsApi = new GoogleSheetsApi(this.options);
            serviceCollection.AddSingleton<IGoogleSheetsGeneratorFactory>(
                new GoogleSheetsGeneratorFactory(googleSheetsApi));

            this.interactionService = new InteractionService(this.client, new InteractionServiceConfig()
            {
                DefaultRunMode = RunMode.Sync,
                LogLevel = LogSeverity.Info,
                UseCompiledLambda = true,
            });
            this.interactionService.Log += this.OnLogAsync;
            serviceCollection.AddSingleton(this.interactionService);
            this.serviceProvider = serviceCollection.BuildServiceProvider();
            this.slashCommandDispatcher = new SlashCommandDispatcher(this.dbActionFactory);

            this.logger = Log.ForContext(this.GetType());
            this.discordNetEventLogger = new DiscordNetEventLogger(this.client, this.interactionService);
            this.client.JoinedGuild += this.OnGuildJoined;
            this.client.InteractionCreated += this.OnInteractionCreated;
            this.client.GuildMemberUpdated += this.OnGuildMemberUpdated;
            this.client.Ready += this.OnClientReady;

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
            this.interactionService.Log -= this.OnLogAsync;
            this.client.Ready -= this.OnClientReady;
            this.client.JoinedGuild -= this.OnGuildJoined;
            this.client.InteractionCreated -= this.OnInteractionCreated;
            this.client.GuildMemberUpdated -= this.OnGuildMemberUpdated;
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

        private async Task OnClientReady()
        {
            if (this.interactionModules == null)
            {
                this.interactionModules = (await this.interactionService.AddModulesAsync(
                    Assembly.GetExecutingAssembly(), this.serviceProvider)).ToArray();
            }
            await this.interactionService.AddModulesGloballyAsync(deleteMissing: true, modules: this.interactionModules);
        }

        private Task OnInteractionCreated(SocketInteraction interaction)
        {
            if (interaction is SocketSlashCommand)
            {
                _ = Task.Run(() => this.HandleInteractionAsync(interaction));
            }
            return Task.CompletedTask;
        }

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                SocketInteractionContext context = new SocketInteractionContext(this.client, interaction);
                await this.slashCommandDispatcher.ExecuteAsync(
                    context, () => this.interactionService.ExecuteCommandAsync(context, this.serviceProvider));
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Exception responding to interaction");
            }
        }

        private Task OnGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> oldUser, SocketGuildUser newUser)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (KeyValuePair<ulong, GameState> pair in this.gameStateManager.GetGameChannelPairs())
                    {
                        ITextChannel channel = newUser.Guild.GetTextChannel(pair.Key);
                        if (channel != null && pair.Value.TeamManager is IByRoleTeamManager)
                        {
                            IReadOnlyDictionary<string, string> teams = await pair.Value.TeamManager.GetTeamIdToNames();
                            ulong currentTeamRole = newUser.Roles.Select(role => role.Id).FirstOrDefault(
                                roleId => teams.ContainsKey(roleId.ToString(CultureInfo.InvariantCulture)));
                            ulong? previousTeamRole = oldUser.HasValue ? oldUser.Value.Roles.Select(role => role.Id).FirstOrDefault(
                                roleId => teams.ContainsKey(roleId.ToString(CultureInfo.InvariantCulture))) : null;
                            if (currentTeamRole == 0 || (previousTeamRole.HasValue && previousTeamRole != currentTeamRole))
                            {
                                await PromptHandler.WithdrawPlayerAsync(channel, this.client.CurrentUser.Id,
                                    pair.Value, newUser.Id, this.options, this.dbActionFactory, this.hubContext);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Exception withdrawing player after guild member update");
                }
            });
            return Task.CompletedTask;
        }

        private Task OnGuildJoined(SocketGuild guild)
        {
            if (!guild.CurrentUser.GuildPermissions.SendMessages)
            {
                return Task.CompletedTask;
            }

            return guild.DefaultChannel.SendMessageAsync(
                "Thank you for adding the QuizBowlScoreTracker bot to your server. Type in */help* to see a list of commands that the bot supports.");
        }

        private Task OnLogAsync(LogMessage logMessage)
        {
            if (logMessage.Exception != null)
            {
                this.logger.Error(logMessage.Exception, "Exception occurred in a command");
            }

            return Task.CompletedTask;
        }

    }
}
