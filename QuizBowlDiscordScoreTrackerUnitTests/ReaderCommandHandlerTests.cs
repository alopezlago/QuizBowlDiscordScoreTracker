using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;
using Format = QuizBowlDiscordScoreTracker.Format;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class ReaderCommandHandlerTests : IDisposable
    {
        private const ulong DefaultReaderId = 1;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3 });

        private const ulong DefaultChannelId = 11;
        private const ulong DefaultGuildId = 9;
        private const ulong DefaultReaderRoleId = 1001;
        private const string DefaultReaderRoleName = "Readers";

        private InMemoryBotConfigurationContextFactory botConfigurationfactory;

        private ReaderCommandHandler Handler { get; set; }

        private GameState Game { get; set; }

        private IGoogleSheetsGeneratorFactory GoogleSheetsGeneratorFactory { get; set; }

        private MessageStore MessageStore { get; set; }

        [TestInitialize]
        public void InitializeTest()
        {
            this.botConfigurationfactory = new InMemoryBotConfigurationContextFactory();

            // Make sure the database is initialized before running the test
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            {
                context.Database.Migrate();
            }

            // Clear out the old fields
            this.Handler = null;
            this.Game = null;
            this.GoogleSheetsGeneratorFactory = null;
            this.MessageStore = null;
        }

        [TestCleanup]
        public void Dispose()
        {
            this.botConfigurationfactory.Dispose();
        }

        [TestMethod]
        public async Task CanSetExistingUserAsNewReader()
        {
            ulong newReaderId = GetExistingNonReaderUserId();
            string newReaderMention = $"@User_{newReaderId}";
            this.InitializeHandler();

            this.Game.ReaderId = DefaultReaderId;

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(newReaderId);
            mockUser.Setup(user => user.Mention).Returns(newReaderMention);
            await this.Handler.SetNewReaderAsync(mockUser.Object);

            Assert.AreEqual(newReaderId, this.Game.ReaderId, "Reader ID was not set correctly.");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages sent.");

            Assert.IsTrue(
                this.MessageStore.ChannelMessages.First().Contains(newReaderMention, StringComparison.InvariantCulture),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CanSetUserWithReaderRoleAsNewReader()
        {
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetReaderRolePrefixAsync(DefaultGuildId, "Reader");
            }

            ulong newReaderId = GetExistingNonReaderUserId();
            string newReaderMention = $"@User_{newReaderId}";
            this.InitializeHandler();
            this.Game.ReaderId = DefaultReaderId;

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(newReaderId);
            mockUser.Setup(user => user.Mention).Returns(newReaderMention);
            mockUser.Setup(user => user.RoleIds).Returns(new ulong[] { DefaultReaderRoleId });
            await this.Handler.SetNewReaderAsync(mockUser.Object);

            Assert.AreEqual(newReaderId, this.Game.ReaderId, "Reader ID was not set correctly.");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages sent.");

            string expectedMessage = $"{newReaderMention} is now the reader.";
            this.MessageStore.VerifyChannelMessages(expectedMessage);
        }

        [TestMethod]
        public async Task CannotSetUserWithoutReaderRoleAsNewReader()
        {
            const string readerRolePrefix = "Reader";
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetReaderRolePrefixAsync(DefaultGuildId, readerRolePrefix);
            }

            ulong newReaderId = GetExistingNonReaderUserId();
            string newReaderMention = $"@User_{newReaderId}";
            this.InitializeHandler();
            this.Game.ReaderId = DefaultReaderId;

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(newReaderId);
            mockUser.Setup(user => user.Mention).Returns(newReaderMention);
            mockUser.Setup(user => user.RoleIds).Returns(new ulong[] { DefaultReaderRoleId + 1 });
            await this.Handler.SetNewReaderAsync(mockUser.Object);

            Assert.AreEqual(DefaultReaderId, this.Game.ReaderId, "Reader ID was updated incorrectly.");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages sent.");

            string expectedMessage = $@"Cannot set {newReaderMention} as the reader because they do not have a role with the reader prefix ""{readerRolePrefix}""";
            this.MessageStore.VerifyChannelMessages(expectedMessage);
        }

        [TestMethod]
        public async Task ClearEmptiesQueue()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();

            await this.Game.AddPlayer(buzzer, "Player");
            await this.Handler.ClearAsync();

            Assert.IsFalse(this.Game.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");

            this.MessageStore.VerifyChannelMessages("Current cycle cleared of all buzzes.");
        }

        [TestMethod]
        public async Task ClearAllRemovesGame()
        {
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out _);
            MessageStore messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore, DefaultIds, DefaultGuildId, DefaultChannelId, DefaultReaderId);
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            IFileScoresheetGenerator scoresheetGenerator = (new Mock<IFileScoresheetGenerator>()).Object;
            IGoogleSheetsGeneratorFactory googleSheetsGeneratorFactory = (new Mock<IGoogleSheetsGeneratorFactory>()).Object;

            ReaderCommandHandler handler = new ReaderCommandHandler(
                commandContext, manager, options, dbActionFactory, scoresheetGenerator, googleSheetsGeneratorFactory);

            await handler.ClearAllAsync();

            Assert.IsFalse(
                manager.TryGet(DefaultChannelId, out _),
                "Game should have been removed from the manager.");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
        }

        [TestMethod]
        public async Task NextQuestionClears()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();

            await this.Game.AddPlayer(buzzer, "Player");
            await this.Handler.NextAsync();

            Assert.IsFalse(this.Game.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CanUndoWithReader()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();

            this.Game.ReaderId = DefaultReaderId;
            await this.Game.AddPlayer(buzzer, "Player");
            this.Game.ScorePlayer(10);
            await this.Handler.UndoAsync();

            Assert.IsTrue(
                this.Game.TryGetNextPlayer(out ulong nextPlayerId),
                "Queue should be restored, so we should have a player.");
            Assert.AreEqual(buzzer, nextPlayerId, "Incorrect player in the queue.");

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains($"@User_{buzzer}", StringComparison.InvariantCulture),
                "Mention should be included in undo message as a prompt.");
        }

        [TestMethod]
        public async Task UndoAfterScoringBonusPromptsForBonus()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.Format = Format.TossupBonusesShootout;

            this.Game.ReaderId = DefaultReaderId;
            await this.Game.AddPlayer(buzzer, "Player");
            this.Game.ScorePlayer(10);
            Assert.IsTrue(this.Game.TryScoreBonus("0"), "Couldn't score the bonus");
            await this.Handler.UndoAsync();

            Assert.AreEqual(PhaseStage.Bonus, this.Game.CurrentStage, "We should be in the bonus stage");
            Assert.AreEqual(1, this.Game.PhaseNumber, "We should be back to the first question");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                "**Bonus for TU 1**", message, "Mention should be included in undo message as a prompt.");
        }

        [TestMethod]
        public async Task SkipBuzzerNoLongerInServerOnUndo()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            ulong buzzerWhoLeft = 999999;
            this.InitializeHandler();

            this.Game.ReaderId = DefaultReaderId;
            Assert.IsTrue(await this.Game.AddPlayer(buzzerWhoLeft, "Player2"), "Couldn't add initial buzz");
            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Couldn't add second buzzer");
            this.Game.ScorePlayer(10);

            await this.Handler.UndoAsync();

            Assert.IsTrue(
                this.Game.TryGetNextPlayer(out ulong nextPlayerId),
                "Queue should be restored, so we should have a player.");
            Assert.AreEqual(buzzer, nextPlayerId, "Incorrect player in the queue.");

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                $"Undid scoring for <Unknown>. @User_{buzzer}, your answer?",
                message,
                "Mention should be included in undo message as a prompt.");
        }

        [TestMethod]
        public async Task CanRemovePlayer()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            string nickname = $"User_{buzzer}";
            this.InitializeHandler();

            this.Game.ReaderId = DefaultReaderId;
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            this.Game.TeamManager = teamManager;
            Assert.IsTrue(teamManager.TryAddTeam("Alpha", out _), "Team should've been added");
            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(buzzer, nickname, "Alpha"), "Should've been able to add the player");
            Assert.IsNotNull(teamManager.GetTeamIdOrNull(buzzer), "Player should have a team");

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(buzzer);
            mockUser.Setup(user => user.Nickname).Returns(nickname);

            await this.Handler.RemovePlayerAsync(mockUser.Object);

            IEnumerable<PlayerTeamPair> players = await teamManager.GetKnownPlayers();
            Assert.IsFalse(players.Any(), "There should be no players left");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains(nickname, StringComparison.InvariantCulture),
                $"Couldn't find username in message\n{message}");
        }

        [TestMethod]
        public async Task CannotRemovePlayerNotOnTeam()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            string nickname = $"User_{buzzer}";
            this.InitializeHandler();

            this.Game.ReaderId = DefaultReaderId;
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            this.Game.TeamManager = teamManager;
            Assert.IsTrue(teamManager.TryAddTeam("Alpha", out _), "Team should've been added");

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(buzzer);
            mockUser.Setup(user => user.Nickname).Returns(nickname);

            await this.Handler.RemovePlayerAsync(mockUser.Object);

            IEnumerable<PlayerTeamPair> players = await teamManager.GetKnownPlayers();
            Assert.IsFalse(players.Any(), "There should be no players");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains($@"Couldn't remove player ""{nickname}""", StringComparison.InvariantCulture),
                $"Couldn't find failure message in message\n{message}");
        }

        [TestMethod]
        public async Task CannotRemovePlayerWithByRoleTeamManager()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            string nickname = $"User_{buzzer}";
            this.InitializeHandler();

            this.Game.ReaderId = DefaultReaderId;

            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild.Setup(guild => guild.Roles).Returns(Array.Empty<IRole>());
            this.Game.TeamManager = new ByRoleTeamManager(mockGuild.Object, "Team");

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(buzzer);
            mockUser.Setup(user => user.Nickname).Returns(nickname);

            await this.Handler.RemovePlayerAsync(mockUser.Object);

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual("Removing players isn't supported in this mode.", message, $"Unexpected message");
        }

        [TestMethod]
        public async Task CanAddTeam()
        {
            const string teamName = "My Team";
            this.InitializeHandler();
            ISelfManagedTeamManager teamManager = new ByCommandTeamManager();
            this.Game.TeamManager = teamManager;

            await this.Handler.AddTeamAsync(teamName);

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(teamIdToName.ContainsKey(teamName), "Team name wasn't added");

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual($@"Added team ""{teamName}"".", message, $"Unexpected message");
        }

        [TestMethod]
        public async Task CannotAddTeamWithByRoleTeamManager()
        {
            const string teamName = "My Team";
            this.InitializeHandler();
            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild.Setup(guild => guild.Roles).Returns(Array.Empty<IRole>());
            this.Game.TeamManager = new ByRoleTeamManager(mockGuild.Object, "Team");

            await this.Handler.AddTeamAsync(teamName);

            IReadOnlyDictionary<string, string> teamIdToName = await this.Game.TeamManager.GetTeamIdToNames();
            Assert.IsFalse(teamIdToName.ContainsKey(teamName), "Team name wasn't added");

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual("Adding teams isn't supported in this mode.", message, $"Unexpected message");
        }

        [TestMethod]
        public async Task ChangingFormatToHaveBonusIncludesBonusInCurrentPhase()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.Format = Format.TossupBonusesShootout;

            this.Game.ReaderId = DefaultReaderId;
            await this.Game.AddPlayer(buzzer, "Player");
            this.Game.ScorePlayer(10);
            Assert.AreEqual(
                PhaseStage.Bonus, this.Game.CurrentStage, "We should be in a bonus stage in the current phase");
            Assert.AreEqual(1, this.Game.PhaseNumber, "We should still be in the first phase");
        }

        [TestMethod]
        public async Task DisableBonuses()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.Format = Format.TossupBonusesShootout;

            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await this.Handler.DisableBonusesAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are no longer being tracked. Scores for the current question have been cleared.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupShootout, this.Game.Format, "Unexpected format");
            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Should've been able to add the player again");
        }

        [TestMethod]
        public async Task DisableBonusesSucceedsIfEnableBonusSetByDefault()
        {
            this.InitializeHandler();
            this.Game.Format = Format.TossupBonusesShootout;

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetUseBonuses(DefaultGuildId, true);
            }

            await this.Handler.DisableBonusesAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are no longer being tracked for this game only. Run !disableBonusesByDefault to stop tracking bonuses on this server by default.\nScores for the current question have been cleared.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupShootout, this.Game.Format, "Unexpected format");
        }

        [TestMethod]
        public async Task DisableBonusesWhenAlreadyDisabled()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.Format = Format.TossupShootout;

            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await this.Handler.DisableBonusesAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are already untracked.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupShootout, this.Game.Format, "Unexpected format");
            Assert.IsFalse(await this.Game.AddPlayer(buzzer, "Player"), "Shouldn't be able to add the player again");
        }

        [TestMethod]
        public async Task EnableBonuses()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.Format = Format.TossupShootout;

            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await this.Handler.EnableBonusesAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are now being tracked. Scores for the current question have been cleared.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupBonusesShootout, this.Game.Format, "Unexpected format");
            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Should've been able to add the player again");
        }

        [TestMethod]
        public async Task EnableBonusesWhenAlreadyEnabled()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.Format = Format.TossupBonusesShootout;

            Assert.IsTrue(await this.Game.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await this.Handler.EnableBonusesAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are already tracked.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupBonusesShootout, this.Game.Format, "Unexpected format");
            Assert.IsFalse(await this.Game.AddPlayer(buzzer, "Player"), "Shouldn't be able to add the player again");
        }

        [TestMethod]
        public async Task RemoveTeamSucceeds()
        {
            const string teamName = "Alpha";
            this.InitializeHandler();

            await this.Handler.AddTeamAsync(teamName);
            IReadOnlyDictionary<string, string> teamIdToNames = await this.Game.TeamManager.GetTeamIdToNames();
            Assert.AreEqual(1, teamIdToNames.Count, "Unexpected number of teams after a team was added");
            this.MessageStore.Clear();

            await this.Handler.RemoveTeamAsync(teamName);
            teamIdToNames = await this.Game.TeamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToNames.Count, "Unexpected number of teams after removal");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(@$"Removed team ""{teamName}"".", message, "Unexpected message");
        }

        [TestMethod]
        public async Task RemoveTeamFailsWhenPlayerScored()
        {
            const ulong playerId = 2;
            const string playerName = "Alice";
            const string teamName = "Alpha";
            this.InitializeHandler();

            await this.Handler.AddTeamAsync(teamName);
            ByCommandTeamManager teamManager = this.Game.TeamManager as ByCommandTeamManager;
            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(playerId, playerName, teamName),
                "Couldn't add the player to the team");
            Assert.IsTrue(await this.Game.AddPlayer(playerId, playerName), "Couldn't buzz in for the player");
            this.Game.ScorePlayer(10);

            Mock<IGuildUser> playerUser = new Mock<IGuildUser>();
            playerUser.Setup(user => user.Id).Returns(playerId);
            await this.Handler.RemovePlayerAsync(playerUser.Object);

            bool hasPlayers = (await teamManager.GetKnownPlayers()).Any();
            Assert.IsFalse(hasPlayers, "Player should've been removed");

            this.MessageStore.Clear();

            await this.Handler.RemoveTeamAsync(teamName);
            IReadOnlyDictionary<string, string> teamIdToNames = await this.Game.TeamManager.GetTeamIdToNames();
            Assert.AreEqual(1, teamIdToNames.Count, "Unexpected number of teams after removal");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.AreEqual(
                @$"Unable to remove the team. **{playerName}** has already been scored, so the player cannot be removed without affecting the score.",
                message,
                "Unexpected message");
        }

        [TestMethod]
        public async Task ExportToFileSucceeds()
        {
            const string streamText = "scoresheet";
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IFileScoresheetGenerator> mockScoresheetGenerator = new Mock<IFileScoresheetGenerator>();

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(streamText)))
            {
                Task<IResult<Stream>> result = Task.FromResult<IResult<Stream>>(new SuccessResult<Stream>(stream));
                mockScoresheetGenerator
                    .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(result);

                this.InitializeHandler(
                    options,
                    mockScoresheetGenerator.Object);
                await this.Handler.ExportToFileAsync();

                string readerName = $"User_{DefaultReaderId}";
                mockScoresheetGenerator
                    .Verify(generator => generator.TryCreateScoresheet(this.Game, readerName, It.IsAny<string>()),
                    Times.Once());
                this.MessageStore.VerifyChannelMessages();
                Assert.AreEqual(1, this.MessageStore.Files.Count, "Unexpected number of file attachments");
                (Stream resultStream, string filename, string text) = this.MessageStore.Files.First();
                Assert.AreEqual($"Scoresheet_{readerName}_1.xlsx", filename, "Unexpected filename");

                resultStream.Position = 0;
                Assert.AreEqual(streamText.Length, resultStream.Length, "Unexpected stream length");
                byte[] resultBytes = new byte[streamText.Length];
                resultStream.Read(resultBytes, 0, resultBytes.Length);
                string resultString = Encoding.UTF8.GetString(resultBytes);
                Assert.AreEqual(streamText, resultString, "Unexpected result from the stream");
            }
        }

        [TestMethod]
        public async Task ExportToFileWhenGeneratorFails()
        {
            const string errorMessage = "Error!";
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IFileScoresheetGenerator> mockScoresheetGenerator = new Mock<IFileScoresheetGenerator>();

            Task<IResult<Stream>> result = Task.FromResult<IResult<Stream>>(new FailureResult<Stream>(errorMessage));
            mockScoresheetGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(result);

            this.InitializeHandler(
                options,
                mockScoresheetGenerator.Object);
            await this.Handler.ExportToFileAsync();

            string readerName = $"User_{DefaultReaderId}";
            mockScoresheetGenerator
                .Verify(generator => generator.TryCreateScoresheet(this.Game, readerName, It.IsAny<string>()),
                Times.Once());
            this.MessageStore.VerifyChannelMessages($"Export failed. Error: {errorMessage}");
        }

        [TestMethod]
        public async Task ExportToFileUserLimit()
        {
            const int userLimit = 2;
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            options.CurrentValue.DailyUserExportLimit = userLimit;
            Mock<IFileScoresheetGenerator> mockScoresheetGenerator = new Mock<IFileScoresheetGenerator>();

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("scoresheet")))
            {
                Task<IResult<Stream>> result = Task.FromResult<IResult<Stream>>(new SuccessResult<Stream>(stream));
                mockScoresheetGenerator
                    .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(result);

                this.InitializeHandler(
                    options,
                    mockScoresheetGenerator.Object);

                for (int i = 0; i < userLimit; i++)
                {
                    await this.Handler.ExportToFileAsync();
                }

                this.MessageStore.VerifyChannelMessages();
                this.MessageStore.Clear();

                await this.Handler.ExportToFileAsync();

                string readerName = $"User_{DefaultReaderId}";
                Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
                string message = this.MessageStore.ChannelMessages.First();
                Assert.IsTrue(
                    message.Contains("The user has already exceeded", StringComparison.InvariantCultureIgnoreCase),
                    $"Couldn't find information on the user limit in the message '{message}'");
                Assert.AreEqual(0, this.MessageStore.Files.Count, "No files should've been attached");
            }
        }

        [TestMethod]
        public async Task ExportToFileGuildLimit()
        {
            const int guildLimit = 2;
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            options.CurrentValue.DailyGuildExportLimit = guildLimit;
            Mock<IFileScoresheetGenerator> mockScoresheetGenerator = new Mock<IFileScoresheetGenerator>();

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("scoresheet")))
            {
                Task<IResult<Stream>> result = Task.FromResult<IResult<Stream>>(new SuccessResult<Stream>(stream));
                mockScoresheetGenerator
                    .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(result);

                this.InitializeHandler(
                    options,
                    mockScoresheetGenerator.Object);

                for (int i = 0; i < guildLimit; i++)
                {
                    await this.Handler.ExportToFileAsync();
                }

                this.MessageStore.VerifyChannelMessages();
                this.MessageStore.Clear();

                await this.Handler.ExportToFileAsync();

                string readerName = $"User_{DefaultReaderId}";
                Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
                string message = this.MessageStore.ChannelMessages.First();
                Assert.IsTrue(
                    message.Contains("The server has already exceeded", StringComparison.InvariantCultureIgnoreCase),
                    $"Couldn't find information on the user limit in the message '{message}'");
                Assert.AreEqual(0, this.MessageStore.Files.Count, "No files should've been attached");
            }
        }

        [TestMethod]
        public async Task ExportToUCSDFails()
        {
            const string errorMessage = "Too many teams";

            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new FailureResult<string>(errorMessage)))
                .Verifiable();

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);
            await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", 1);

            mockFactory.Verify();
            this.MessageStore.VerifyChannelMessages(errorMessage);
        }

        [TestMethod]
        public async Task ExportToUCSDSucceeds()
        {
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)))
                .Verifiable();

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);
            await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", 1);

            mockFactory.Verify();
            this.MessageStore.VerifyChannelMessages("Game written to the scoresheet Round 1");

            // Make sure it succeeds at the limit (15) too
            this.MessageStore.Clear();
            await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", 15);

            this.MessageStore.VerifyChannelMessages("Game written to the scoresheet Round 15");
        }

        [TestMethod]
        public async Task ExportToUCSDUserLimit()
        {
            const int userLimit = 2;
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            options.CurrentValue.DailyUserExportLimit = userLimit;
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)));

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);

            for (int i = 0; i < userLimit; i++)
            {
                await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", i + 1);
                this.MessageStore.VerifyChannelMessages($"Game written to the scoresheet Round {i + 1}");
                this.MessageStore.Clear();
            }

            await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", userLimit + 1);

            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Exactly(userLimit));

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains("The user has already exceeded", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find information on the user limit in the message '{message}'");
        }

        [TestMethod]
        public async Task ExportToUCSDGuildLimit()
        {
            const int guildLimit = 2;
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            options.CurrentValue.DailyGuildExportLimit = guildLimit;
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)));

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);

            for (int i = 0; i < guildLimit; i++)
            {
                await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", i + 1);
                this.MessageStore.VerifyChannelMessages($"Game written to the scoresheet Round {i + 1}");
                this.MessageStore.Clear();
            }

            await this.Handler.ExportToUCSD("https://localhost/sheetsUrl", guildLimit + 1);

            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Exactly(guildLimit));

            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            string message = this.MessageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains("The server has already exceeded", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find information on the user limit in the message '{message}'");
        }

        [TestMethod]
        public async Task ExportToUCSDWithBadUrlFails()
        {
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)));

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);
            await this.Handler.ExportToUCSD("this is a bad URL", 1);

            this.MessageStore.VerifyChannelMessages(
                "The link to the Google Sheet wasn't understandable. Be sure to copy the full URL from the address bar.");
            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Never);
        }

        [TestMethod]
        public async Task ExportToUCSDWithRoundBelowOneFails()
        {
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)));

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);
            await this.Handler.ExportToUCSD("https://localhost/sheets", 0);

            this.MessageStore.VerifyChannelMessages(
                "The round is out of range. The round number must be between 1 and 15 (inclusive).");
            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Never);
        }

        [TestMethod]
        public async Task ExportToUCSDWithRoundAboveFifteenFails()
        {
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryCreateScoresheet(It.IsAny<GameState>(), It.IsAny<Uri>(), It.IsAny<string>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)));

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(GoogleSheetsType.UCSD))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(options, mockFactory.Object);
            await this.Handler.ExportToUCSD("https://localhost/sheets", 16);

            this.MessageStore.VerifyChannelMessages(
                "The round is out of range. The round number must be between 1 and 15 (inclusive).");
            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Never);
        }

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private void InitializeHandler()
        {
            this.InitializeHandler(DefaultIds);
        }

        private void InitializeHandler(HashSet<ulong> existingIds)
        {
            this.MessageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                this.MessageStore,
                existingIds,
                DefaultGuildId,
                DefaultChannelId,
                userId: DefaultReaderId,
                updateMockGuild: UpdateMockGuild,
                out _);
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState game);
            game.TeamManager = new ByCommandTeamManager();
            this.Game = game;

            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            IFileScoresheetGenerator scoresheetGenerator = (new Mock<IFileScoresheetGenerator>()).Object;
            this.GoogleSheetsGeneratorFactory = (new Mock<IGoogleSheetsGeneratorFactory>()).Object;

            this.Handler = new ReaderCommandHandler(
                commandContext,
                manager,
                options,
                dbActionFactory,
                scoresheetGenerator,
                this.GoogleSheetsGeneratorFactory);
        }

        private void InitializeHandler(
            IOptionsMonitor<BotConfiguration> options, IFileScoresheetGenerator scoresheetGenerator)
        {
            this.MessageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                this.MessageStore,
                DefaultIds,
                DefaultGuildId,
                DefaultChannelId,
                userId: DefaultReaderId,
                updateMockGuild: UpdateMockGuild,
                out _);
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState game);
            game.TeamManager = new ByCommandTeamManager();
            this.Game = game;

            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            this.GoogleSheetsGeneratorFactory = (new Mock<IGoogleSheetsGeneratorFactory>()).Object;

            this.Handler = new ReaderCommandHandler(
                commandContext,
                manager,
                options,
                dbActionFactory,
                scoresheetGenerator,
                this.GoogleSheetsGeneratorFactory);
        }

        private void InitializeHandler(
            IOptionsMonitor<BotConfiguration> options, IGoogleSheetsGeneratorFactory googleSheetsGeneratorFactory)
        {
            this.MessageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                this.MessageStore,
                DefaultIds,
                DefaultGuildId,
                DefaultChannelId,
                userId: DefaultReaderId,
                updateMockGuild: UpdateMockGuild,
                out _);
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState game);
            game.TeamManager = new ByCommandTeamManager();
            this.Game = game;

            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            IFileScoresheetGenerator scoresheetGenerator = (new Mock<IFileScoresheetGenerator>()).Object;
            this.GoogleSheetsGeneratorFactory = googleSheetsGeneratorFactory;

            this.Handler = new ReaderCommandHandler(
                commandContext,
                manager,
                options,
                dbActionFactory,
                scoresheetGenerator,
                this.GoogleSheetsGeneratorFactory);
        }

        private static void UpdateMockGuild(Mock<IGuild> mockGuild, ITextChannel textChannel)
        {
            Mock<IRole> mockReaderRole = new Mock<IRole>();
            mockReaderRole.Setup(role => role.Id).Returns(DefaultReaderRoleId);
            mockReaderRole.Setup(role => role.Name).Returns(DefaultReaderRoleName);
            mockGuild.Setup(guild => guild.Roles).Returns(new IRole[] { mockReaderRole.Object });
        }
    }
}
