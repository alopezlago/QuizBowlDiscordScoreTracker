using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;
using QuizBowlDiscordScoreTracker.Web;
using Format = QuizBowlDiscordScoreTracker.Format;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class SlashCommandRegressionTests : IDisposable
    {
        private const ulong ChannelId = 11;
        private const ulong ReaderId = 1;
        private const ulong PlayerId = 2;
        private const ulong NextPlayerId = 3;
        private InMemoryBotConfigurationContextFactory database;
        private IDatabaseActionFactory databaseActions;
        private GameStateManager manager;
        private GameState game;
        private MessageStore messages;

        [TestInitialize]
        public void Initialize()
        {
            this.database = new InMemoryBotConfigurationContextFactory();
            using (BotConfigurationContext context = this.database.Create())
            {
                context.Database.Migrate();
            }
            this.databaseActions = CommandMocks.CreateDatabaseActionFactory(this.database);
            this.messages = new MessageStore();
            this.manager = new GameStateManager();
            this.manager.TryCreate(ChannelId, out this.game);
            this.game.ReaderId = ReaderId;
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task TeamDepartureWithdrawsPendingBuzz(bool readerRemovesPlayer, bool firstInQueue)
        {
            ByCommandTeamManager teams = new ByCommandTeamManager();
            this.game.TeamManager = teams;
            teams.TryAddTeam("Team", out _);
            teams.TryAddPlayerToTeam(PlayerId, "Player", "Team");
            teams.TryAddPlayerToTeam(NextPlayerId, "Next player", "Team");
            await this.game.AddPlayer(firstInQueue ? PlayerId : NextPlayerId, "First player");
            await this.game.AddPlayer(firstInQueue ? NextPlayerId : PlayerId, "Second player");
            IInteractionContext context = this.CreateContext(readerRemovesPlayer ? ReaderId : PlayerId, out _);

            if (readerRemovesPlayer)
            {
                await this.RunAsync(context, () => this.CreateReaderHandler(context).RemovePlayerAsync(
                    CommandMocks.CreateGuildUser(PlayerId)));
            }
            else
            {
                await this.RunAsync(context, this.CreateGeneralHandler(context).LeaveTeamAsync);
            }

            Assert.IsNull(await teams.GetTeamIdOrNull(PlayerId));
            Assert.IsTrue(this.game.TryGetNextPlayer(out ulong nextPlayer));
            Assert.AreEqual(NextPlayerId, nextPlayer);
            Assert.IsFalse(await this.game.WithdrawPlayer(PlayerId));
            string confirmation = readerRemovesPlayer ?
                "Player \"User_2\" removed from their team." : "\"User_2\" left their team.";
            if (firstInQueue)
            {
                this.messages.VerifyChannelMessages("@User_3 (Team)", confirmation);
            }
            else
            {
                this.messages.VerifyChannelMessages(confirmation);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task TeamDepartureClearsIndicatorForLastPlayer(bool readerRemovesPlayer)
        {
            ByCommandTeamManager teams = new ByCommandTeamManager();
            this.game.TeamManager = teams;
            teams.TryAddTeam("Team", out _);
            teams.TryAddPlayerToTeam(PlayerId, "Player", "Team");
            await this.game.AddPlayer(PlayerId, "Player");
            IInteractionContext context = this.CreateContext(readerRemovesPlayer ? ReaderId : PlayerId, out _);
            IHubContext<MonitorHub> hub = CommandMocks.CreateHubContext();
            if (readerRemovesPlayer)
            {
                await this.RunAsync(context, () => this.CreateReaderHandler(context, hub)
                    .RemovePlayerAsync(CommandMocks.CreateGuildUser(PlayerId)));
            }
            else
            {
                await this.RunAsync(context, this.CreateGeneralHandler(context, hub).LeaveTeamAsync);
            }

            Assert.IsFalse(this.game.TryGetNextPlayer(out _));
            Mock.Get(hub.Clients.Group("11")).Verify(proxy => proxy.SendCoreAsync(
                "Clear", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task DepartureCleanupPreservesScoredBuzzAndEligibility()
        {
            await this.game.AddPlayer(PlayerId, "Player");
            this.game.ScorePlayer(-5);
            await this.game.AddPlayer(NextPlayerId, "Next player");
            IInteractionContext context = this.CreateContext(PlayerId, out _);

            Assert.IsFalse(await PromptHandler.WithdrawPlayerAsync((ITextChannel)context.Channel,
                context.Client.CurrentUser.Id, this.game, PlayerId, CommandMocks.CreateConfigurationOptionsMonitor(),
                this.databaseActions, CommandMocks.CreateHubContext()));

            Assert.IsFalse(await this.game.AddPlayer(PlayerId, "Player"));
            Assert.AreEqual(1, (await this.game.GetPhaseScores()).First().ScoringSplitsOnActions.Count());
            Assert.IsTrue(this.game.TryGetNextPlayer(out ulong nextPlayer));
            Assert.AreEqual(NextPlayerId, nextPlayer);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ManualWithdrawalWorksAfterTeamRemoval(bool useRoles)
        {
            if (useRoles)
            {
                Mock<IByRoleTeamManager> teams = new Mock<IByRoleTeamManager>();
                teams.Setup(value => value.GetTeamIdOrNull(PlayerId)).ReturnsAsync("team");
                this.game.TeamManager = teams.Object;
                await this.game.AddPlayer(PlayerId, "Player");
                teams.Setup(value => value.GetTeamIdOrNull(PlayerId)).ReturnsAsync((string)null);
            }
            else
            {
                ByCommandTeamManager teams = new ByCommandTeamManager();
                this.game.TeamManager = teams;
                teams.TryAddTeam("Team", out _);
                teams.TryAddPlayerToTeam(PlayerId, "Player", "Team");
                await this.game.AddPlayer(PlayerId, "Player");
                teams.TryRemovePlayerFromTeam(PlayerId);
            }

            IInteractionContext context = this.CreateContext(PlayerId, out _);
            IHubContext<MonitorHub> hub = CommandMocks.CreateHubContext();

            await this.RunAsync(context, this.CreatePlayerHandler(context, hub).Withdraw);

            Assert.IsFalse(this.game.TryGetNextPlayer(out _));
            this.messages.VerifyChannelMessages("@User_2 has withdrawn.");
            Mock.Get(hub.Clients.Group("11")).Verify(proxy => proxy.SendCoreAsync(
                "Clear", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task BanAndUnbanPreserveLargeSnowflakeIds()
        {
            const ulong userId = 123456789012345678;
            IInteractionContext context = this.CreateContext(ReaderId, out _, "ban-user");
            BotOwnerSlashCommands commands = new BotOwnerSlashCommands(
                CommandMocks.CreateConfigurationOptionsMonitor(), this.databaseActions);
            ((IInteractionModuleBase)commands).SetContext(context);
            await this.RunAsync(context, () => commands.BanUserAsync("123456789012345678"));
            using (DatabaseAction action = this.databaseActions.Create())
            {
                Assert.IsTrue(await action.GetCommandBannedAsync(userId));
                Assert.IsFalse(await action.GetCommandBannedAsync(userId + 1));
            }

            context = this.CreateContext(ReaderId, out _, "unban-user");
            ((IInteractionModuleBase)commands).SetContext(context);
            await this.RunAsync(context, () => commands.UnbanUserAsync("123456789012345678"));
            using (DatabaseAction action = this.databaseActions.Create())
            {
                Assert.IsFalse(await action.GetCommandBannedAsync(userId));
            }
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("-1")]
        [DataRow("+1")]
        [DataRow("not-an-id")]
        [DataRow("18446744073709551616")]
        public async Task InvalidBanIdsGetPrivateErrorWithoutAccessingDatabase(string userId)
        {
            foreach (bool banned in new[] { true, false })
            {
                IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle);
                Mock<IDatabaseActionFactory> factory = new Mock<IDatabaseActionFactory>(MockBehavior.Strict);
                BotOwnerSlashCommands commands = new BotOwnerSlashCommands(
                    CommandMocks.CreateConfigurationOptionsMonitor(), factory.Object);
                ((IInteractionModuleBase)commands).SetContext(context);

                await this.RunAsync(context, () => banned ? commands.BanUserAsync(userId) : commands.UnbanUserAsync(userId));

                Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
                factory.Verify(value => value.Create(), Times.Never);
            }
        }

        [TestCleanup]
        public void Dispose()
        {
            this.database.Dispose();
        }

        [TestMethod]
        public void OnlyRequiredGuildIntentsAreRequested()
        {
            Assert.AreEqual(GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildVoiceStates,
                Bot.RequiredGatewayIntents);
        }

        [TestMethod]
        public async Task DispatcherAcknowledgesBeforeCheckingDatabase()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle);
            Mock<IDatabaseActionFactory> factory = new Mock<IDatabaseActionFactory>();
            factory.Setup(value => value.Create()).Returns(() =>
            {
                Assert.IsTrue(lifecycle.HasResponded);
                return new DatabaseAction(this.database.Create());
            });
            SlashCommandDispatcher dispatcher = new SlashCommandDispatcher(factory.Object);

            await dispatcher.ExecuteAsync(context, () => Task.FromResult<Discord.Interactions.IResult>(ExecuteResult.FromSuccess()));

            Assert.AreEqual(1, lifecycle.DeferCount);
            Assert.IsTrue(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        [DataRow("test-command")]
        [DataRow("buzz")]
        public async Task BannedUserCannotExecuteCommand(string commandName)
        {
            using (DatabaseAction action = this.databaseActions.Create())
            {
                await action.AddCommandBannedUser(PlayerId);
            }
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, commandName);
            bool executed = false;

            await new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context, () =>
            {
                executed = true;
                return Task.FromResult<Discord.Interactions.IResult>(ExecuteResult.FromSuccess());
            });

            Assert.IsFalse(executed);
            Assert.AreEqual("You are banned from using commands.", lifecycle.OriginalContent);
            Assert.IsFalse(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        public async Task UnbanStillExecutesOwnerPreconditionForBannedUsers()
        {
            using (DatabaseAction action = this.databaseActions.Create())
            {
                await action.AddCommandBannedUser(PlayerId);
            }
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "unban-user");
            bool executed = false;

            await new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context, () =>
            {
                executed = true;
                return Task.FromResult<Discord.Interactions.IResult>(PreconditionResult.FromError("Owner required"));
            });

            Assert.IsTrue(executed);
            Assert.AreEqual("Owner required", lifecycle.OriginalContent);
        }

        [TestMethod]
        [DataRow("test-command")]
        [DataRow("buzz")]
        public async Task FailedPreconditionGetsPrivateError(string commandName)
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, commandName);

            await new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context,
                () => Task.FromResult<Discord.Interactions.IResult>(PreconditionResult.FromError("No existing game")));

            Assert.AreEqual("No existing game", lifecycle.OriginalContent);
            Assert.IsFalse(lifecycle.OriginalResponseDeleted);
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.IsTrue(lifecycle.InitialResponseIsEphemeral);
        }

        [TestMethod]
        [DataRow("test-command")]
        [DataRow("buzz")]
        public async Task CommandExceptionGetsErrorInsteadOfStuckPlaceholder(string commandName)
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, commandName);

            await new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context,
                () => throw new InvalidOperationException("Test failure"));

            Assert.AreEqual("An error occurred while running the command.", lifecycle.OriginalContent);
            Assert.IsFalse(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        public async Task BuzzPromptStartsBeforeMuteLookup()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "buzz");
            Mock<IDatabaseActionFactory> muteFactory = new Mock<IDatabaseActionFactory>();
            muteFactory.Setup(value => value.Create()).Returns(() =>
            {
                Assert.IsTrue(lifecycle.HasResponded);
                return new DatabaseAction(this.database.Create());
            });
            PlayerCommandHandler handler = new PlayerCommandHandler(context, this.manager,
                CommandMocks.CreateConfigurationOptionsMonitor(), muteFactory.Object, CommandMocks.CreateHubContext());

            await this.RunAsync(context, handler.Buzz);

            muteFactory.Verify(value => value.Create(), Times.Once);
            Assert.IsFalse(lifecycle.InitialResponseIsEphemeral);
            this.messages.VerifyChannelMessages(CommandMocks.CreateGuildUser(PlayerId).Mention);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task QueuedOrDuplicateBuzzGetsPrivateInitialResponse(bool duplicate)
        {
            await this.game.AddPlayer(PlayerId, "First player");
            IInteractionContext context = this.CreateContext(duplicate ? PlayerId : NextPlayerId,
                out InteractionLifecycleMock lifecycle, "buzz");

            await this.RunAsync(context, this.CreatePlayerHandler(context).Buzz);

            Assert.IsTrue(lifecycle.InitialResponseIsEphemeral);
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.AreEqual(0, lifecycle.DeferCount);
            Assert.AreEqual(0, lifecycle.ModifyCount);
            Assert.AreEqual(0, lifecycle.FollowupVisibilities.Count);
            Assert.IsFalse(lifecycle.OriginalResponseDeleted);
            Assert.IsTrue(this.game.TryGetNextPlayer(out ulong nextPlayer));
            Assert.AreEqual(PlayerId, nextPlayer);
            this.messages.VerifyChannelMessages(duplicate ?
                "You've already buzzed in or someone buzzed before you" : "You've been added to the buzz queue");
        }

        [TestMethod]
        public async Task WithdrawalKeepsNextPlayerPrompt()
        {
            await this.game.AddPlayer(PlayerId, "First player");
            await this.game.AddPlayer(NextPlayerId, "Second player");
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "withdraw");

            await this.RunAsync(context, this.CreatePlayerHandler(context).Withdraw);

            this.messages.VerifyChannelMessages(CommandMocks.CreateGuildUser(NextPlayerId).Mention);
            Assert.IsFalse(lifecycle.FollowupVisibilities.Single());
            Assert.IsTrue(this.game.TryGetNextPlayer(out ulong nextPlayer));
            Assert.AreEqual(NextPlayerId, nextPlayer);
            Assert.IsTrue(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        public async Task WithdrawalWithoutBuzzGetsPrivateReply()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "withdraw");

            await this.RunAsync(context, this.CreatePlayerHandler(context).Withdraw);

            this.messages.VerifyChannelMessages("You are not in the buzz queue.");
            Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
        }

        [TestMethod]
        [DataRow("-5")]
        [DataRow("0")]
        [DataRow("no penalty")]
        public async Task IncorrectScorePromptsNextPlayerWithOneAcknowledgment(string points)
        {
            await this.game.AddPlayer(PlayerId, "First player");
            await this.game.AddPlayer(NextPlayerId, "Second player");
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "score");

            await this.RunAsync(context, () => this.CreateReaderHandler(context).Score(points));

            this.messages.VerifyChannelMessages(points == "no penalty" ? "0" : points,
                CommandMocks.CreateGuildUser(NextPlayerId).Mention);
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.AreEqual(1, lifecycle.FollowupVisibilities.Count);
            Assert.IsFalse(lifecycle.FollowupVisibilities.Single());
            Assert.IsTrue(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        [DataRow("10", false)]
        [DataRow("15", false)]
        [DataRow("20", false)]
        [DataRow("10", true)]
        [DataRow("15", true)]
        [DataRow("20", true)]
        public async Task CorrectScorePostsPointsBeforeNextPhase(string points, bool useBonuses)
        {
            if (useBonuses)
            {
                this.game.Format = Format.CreateTossupBonusesShootout(false);
            }

            await this.game.AddPlayer(PlayerId, "First player");
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "score");

            await this.RunAsync(context, () => this.CreateReaderHandler(context).Score(points));

            this.messages.VerifyChannelMessages(points, useBonuses ? "**Bonus for 1**" : "**TU 2**");
            Assert.AreEqual(useBonuses ? 1 : 2, this.game.PhaseNumber);
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.AreEqual(2, lifecycle.FollowupVisibilities.Count);
            Assert.IsTrue(lifecycle.FollowupVisibilities.All(ephemeral => !ephemeral));
        }

        [TestMethod]
        public async Task BonusScoringUsesSlashParameters()
        {
            this.game.Format = Format.CreateTossupBonusesShootout(false);
            await this.game.AddPlayer(PlayerId, "First player");
            this.game.ScorePlayer(10);
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "score");

            await this.RunAsync(context, () => this.CreateReaderHandler(context).Score("10/0/10"));

            this.messages.VerifyChannelMessages("**TU 2**");
            Assert.AreEqual(2, this.game.PhaseNumber);
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
        }

        [TestMethod]
        public async Task InvalidScoreGetsPrivateReplyWithoutChangingQueue()
        {
            await this.game.AddPlayer(PlayerId, "First player");
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "score");

            await this.RunAsync(context, () => this.CreateReaderHandler(context).Score("invalid"));

            Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
            Assert.IsTrue(this.game.TryGetNextPlayer(out ulong nextPlayer));
            Assert.AreEqual(PlayerId, nextPlayer);
        }

        [TestMethod]
        public async Task ScoreWithoutPlayerGetsPrivateReply()
        {
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "score");

            await this.RunAsync(context, () => this.CreateReaderHandler(context).Score("10"));

            this.messages.VerifyChannelMessages("No player is waiting to be scored.");
            Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
        }

        [TestMethod]
        public async Task UndoWithoutActionsGetsPrivateReply()
        {
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "undo");

            await this.RunAsync(context, this.CreateReaderHandler(context).UndoAsync);

            this.messages.VerifyChannelMessages("There is nothing to undo.");
            Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
        }

        [TestMethod]
        public async Task NextAtQuestionLimitDoesNotAdvanceOrReplyTwice()
        {
            while (this.game.PhaseNumber < GameState.MaximumPhasesCount)
            {
                this.game.NextQuestion();
            }
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "next");

            await this.RunAsync(context, this.CreateReaderHandler(context).NextAsync);

            this.messages.VerifyChannelMessages($"Reached the limit for games ({GameState.MaximumPhasesCount} questions)");
            Assert.AreEqual(GameState.MaximumPhasesCount, this.game.PhaseNumber);
            Assert.AreEqual(1, lifecycle.FollowupVisibilities.Count);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ScoreAndReportDoNotDeferOrDeleteTwice(bool report)
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle);

            await this.RunAsync(context, () => report ?
                ScoreHandler.GetGameReportAsync(context, this.manager) : ScoreHandler.GetScoreAsync(context, this.manager));

            Assert.AreEqual(1, lifecycle.DeferCount);
            Assert.IsTrue(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        public async Task FileResponseUsesPublicFollowupAfterDefer()
        {
            IInteractionContext context = this.CreateContext(ReaderId, out InteractionLifecycleMock lifecycle, "export-to-file");
            using (MemoryStream stream = new MemoryStream())
            {
                await this.RunAsync(context, () => context.Interaction.RespondOrFollowupWithFileAsync(stream, "scores.xlsx"));
            }

            Assert.AreEqual(1, this.messages.Files.Count);
            Assert.IsFalse(lifecycle.FollowupVisibilities.Single());
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
        }

        [TestMethod]
        public async Task PlayerPreconditionAwaitsTeamLookupAndAllowsSoloGames()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out _);
            using (ServiceProvider services = new ServiceCollection().AddSingleton(this.manager).BuildServiceProvider())
            {
                RequirePlayerAttribute attribute = new RequirePlayerAttribute();
                Assert.IsTrue((await attribute.CheckRequirementsAsync(context, null, services)).IsSuccess);

                ByCommandTeamManager teams = new ByCommandTeamManager();
                this.game.TeamManager = teams;
                Assert.IsTrue((await attribute.CheckRequirementsAsync(context, null, services)).IsSuccess);
                teams.TryAddTeam("Team", out _);
                Assert.IsFalse((await attribute.CheckRequirementsAsync(context, null, services)).IsSuccess);
                teams.TryAddPlayerToTeam(PlayerId, "Player", "Team");
                Assert.IsTrue((await attribute.CheckRequirementsAsync(context, null, services)).IsSuccess);
            }
        }

        [TestMethod]
        [DataRow(ReaderId)]
        [DataRow(80UL)]
        public async Task ReaderAndBotCannotBuzz(ulong userId)
        {
            IInteractionContext context = this.CreateContext(userId, out _);
            using (ServiceProvider services = new ServiceCollection().AddSingleton(this.manager).BuildServiceProvider())
            {
                Assert.IsFalse((await new RequirePlayerAttribute().CheckRequirementsAsync(context, null, services)).IsSuccess);
            }
        }

        [TestMethod]
        public async Task HelpAndGameplayModulesRegisterAsSlashCommands()
        {
            using (DiscordSocketClient client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = Bot.RequiredGatewayIntents
            }))
            using (InteractionService service = new InteractionService(client))
            using (ServiceProvider services = new ServiceCollection()
                .AddSingleton(service)
                .AddSingleton(this.manager)
                .AddSingleton(this.databaseActions)
                .AddSingleton(CommandMocks.CreateConfigurationOptionsMonitor())
                .AddSingleton(CommandMocks.CreateHubContext())
                .AddSingleton(Mock.Of<IFileScoresheetGenerator>())
                .AddSingleton(Mock.Of<IGoogleSheetsGeneratorFactory>())
                .BuildServiceProvider())
            {
                await service.AddModulesAsync(typeof(HelpCommand).Assembly, services);
                string[] names = service.SlashCommands.Select(command => command.Name).ToArray();
                CollectionAssert.Contains(names, "help");
                CollectionAssert.Contains(names, "buzz");
                CollectionAssert.Contains(names, "withdraw");
                CollectionAssert.Contains(names, "score");
                CollectionAssert.Contains(names, "get-score");
                Assert.AreEqual(names.Length, names.Distinct().Count());
                Assert.AreEqual("command-name", service.SlashCommands.Single(command => command.Name == "help").Parameters.Single().Name);
                foreach (string name in new[] { "ban-user", "unban-user" })
                {
                    Assert.AreEqual(typeof(string), service.SlashCommands.Single(command => command.Name == name).Parameters.Single().ParameterType);
                }
                SlashCommandInfo withdraw = service.SlashCommands.Single(command => command.Name == "withdraw");
                Assert.IsFalse(withdraw.Preconditions.Concat(withdraw.Module.Preconditions)
                    .Any(precondition => precondition is RequirePlayerAttribute));
                Assert.IsTrue(service.SlashCommands.Single(command => command.Name == "buzz").Preconditions
                    .Any(precondition => precondition is RequirePlayerAttribute));
            }
        }

        [TestMethod]
        public async Task BuzzExecutesBeforeAnyDiscordResponse()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "buzz");
            Mock<IDatabaseActionFactory> factory = new Mock<IDatabaseActionFactory>();
            factory.Setup(value => value.Create()).Returns(() =>
            {
                Assert.IsFalse(lifecycle.HasResponded);
                return new DatabaseAction(this.database.Create());
            });

            await new SlashCommandDispatcher(factory.Object).ExecuteAsync(context, async () =>
            {
                Assert.IsFalse(lifecycle.HasResponded);
                await this.CreatePlayerHandler(context).Buzz();
                return ExecuteResult.FromSuccess();
            });

            Assert.IsTrue(this.game.TryGetNextPlayer(out ulong nextPlayer));
            Assert.AreEqual(PlayerId, nextPlayer);
            this.messages.VerifyChannelMessages(CommandMocks.CreateGuildUser(PlayerId).Mention);
            Assert.IsFalse(lifecycle.InitialResponseIsEphemeral);
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.AreEqual(0, lifecycle.DeferCount);
            Assert.AreEqual(0, lifecycle.ModifyCount);
            Assert.AreEqual(0, lifecycle.FollowupVisibilities.Count);
            Assert.IsFalse(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        public async Task SlowBuzzKeepsPromptPublicAndDeletesOnlyPrivatePlaceholder()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "buzz");
            lifecycle.Interaction.SetupGet(value => value.CreatedAt).Returns(DateTimeOffset.UtcNow.AddSeconds(-2));

            await this.RunAsync(context, this.CreatePlayerHandler(context).Buzz);

            this.messages.VerifyChannelMessages("Processing buzz...", "@User_2");
            Assert.IsTrue(lifecycle.InitialResponseIsEphemeral);
            Assert.IsFalse(lifecycle.FollowupVisibilities.Single());
            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.AreEqual(0, lifecycle.DeferCount);
            Assert.AreEqual(0, lifecycle.ModifyCount);
            Assert.IsTrue(lifecycle.OriginalResponseDeleted);
        }

        [TestMethod]
        public async Task SlowBannedBuzzGetsOnlyPrivateResponses()
        {
            using (DatabaseAction action = this.databaseActions.Create())
            {
                await action.AddCommandBannedUser(PlayerId);
            }
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "buzz");
            lifecycle.Interaction.SetupGet(value => value.CreatedAt).Returns(DateTimeOffset.UtcNow.AddSeconds(-2));
            bool executed = false;

            await new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context, () =>
            {
                executed = true;
                return Task.FromResult<Discord.Interactions.IResult>(ExecuteResult.FromSuccess());
            });

            Assert.IsFalse(executed);
            Assert.IsTrue(lifecycle.InitialResponseIsEphemeral);
            Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
            Assert.IsTrue(lifecycle.OriginalResponseDeleted);
            this.messages.VerifyChannelMessages("Processing buzz...", "You are banned from using commands.");
        }

        [TestMethod]
        public async Task BuzzExceptionDoesNotOverwritePublicPrompt()
        {
            IInteractionContext context = this.CreateContext(PlayerId, out InteractionLifecycleMock lifecycle, "buzz");

            await new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context, async () =>
            {
                await this.CreatePlayerHandler(context).Buzz();
                throw new InvalidOperationException("Failure after public prompt");
            });

            Assert.AreEqual("@User_2", lifecycle.OriginalContent);
            Assert.IsFalse(lifecycle.InitialResponseIsEphemeral);
            Assert.IsTrue(lifecycle.FollowupVisibilities.Single());
            Assert.AreEqual(0, lifecycle.ModifyCount);
            Assert.IsFalse(lifecycle.OriginalResponseDeleted);
            this.messages.VerifyChannelMessages("@User_2", "An error occurred while running the command.");
        }

        [TestMethod]
        public async Task FallbackWaitsForInFlightResponseWithoutAcknowledgingTwice()
        {
            InteractionLifecycleMock lifecycle = new InteractionLifecycleMock(this.messages);
            TaskCompletionSource completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lifecycle.InitialResponseCompletion = completion.Task;
            Task response = lifecycle.Interaction.Object.RespondOrFollowupAsync("Public prompt");
            Task<bool> fallback = lifecycle.Interaction.Object.TryRespondAsync("Processing buzz...", ephemeral: true);
            Assert.IsFalse(fallback.IsCompleted);

            completion.SetResult();
            await response;
            Assert.IsFalse(await fallback);

            Assert.AreEqual(1, lifecycle.InitialResponseCount);
            Assert.IsFalse(lifecycle.InitialResponseIsEphemeral);
            this.messages.VerifyChannelMessages("Public prompt");
        }

        private IInteractionContext CreateContext(
            ulong userId, out InteractionLifecycleMock lifecycle, string commandName = "test-command")
        {
            IInteractionContext context = CommandMocks.CreateInteractionContext(this.messages,
                new HashSet<ulong> { ReaderId, PlayerId, NextPlayerId }, 9, ChannelId, userId, null, out _);
            lifecycle = new InteractionLifecycleMock(this.messages, commandName);
            Mock.Get(context).SetupGet(value => value.Interaction).Returns(lifecycle.Interaction.Object);
            return context;
        }

        private GeneralCommandHandler CreateGeneralHandler(
            IInteractionContext context, IHubContext<MonitorHub> hubContext = null)
        {
            return new GeneralCommandHandler(context, this.manager, CommandMocks.CreateConfigurationOptionsMonitor(),
                this.databaseActions, hubContext ?? CommandMocks.CreateHubContext());
        }

        private PlayerCommandHandler CreatePlayerHandler(
            IInteractionContext context, IHubContext<MonitorHub> hubContext = null)
        {
            return new PlayerCommandHandler(context, this.manager, CommandMocks.CreateConfigurationOptionsMonitor(),
                this.databaseActions, hubContext ?? CommandMocks.CreateHubContext());
        }

        private ReaderCommandHandler CreateReaderHandler(
            IInteractionContext context, IHubContext<MonitorHub> hubContext = null)
        {
            return new ReaderCommandHandler(context, this.manager, CommandMocks.CreateConfigurationOptionsMonitor(),
                this.databaseActions, hubContext ?? CommandMocks.CreateHubContext(), Mock.Of<IFileScoresheetGenerator>(),
                Mock.Of<IGoogleSheetsGeneratorFactory>());
        }

        private Task RunAsync(IInteractionContext context, Func<Task> command)
        {
            return new SlashCommandDispatcher(this.databaseActions).ExecuteAsync(context, async () =>
            {
                await command();
                return ExecuteResult.FromSuccess();
            });
        }
    }
}
