using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;
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

        private InMemoryBotConfigurationContextFactory botConfigurationfactory;

        [TestInitialize]
        public void InitializeTest()
        {
            this.botConfigurationfactory = new InMemoryBotConfigurationContextFactory();

            // Make sure the database is initialized before running the test
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            {
                context.Database.Migrate();
            }
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
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.ReaderId = DefaultReaderId;

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(newReaderId);
            mockUser.Setup(user => user.Mention).Returns(newReaderMention);
            await handler.SetNewReaderAsync(mockUser.Object);

            Assert.AreEqual(newReaderId, currentGame.ReaderId, "Reader ID was not set correctly.");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");

            Assert.IsTrue(
                messageStore.ChannelMessages.First().Contains(newReaderMention, StringComparison.InvariantCulture),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task ClearEmptiesQueue()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(out ReaderCommandHandler handler, out GameState currentGame, out _);

            await currentGame.AddPlayer(buzzer, "Player");
            await handler.ClearAsync();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task ClearAllRemovesGame()
        {
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState currentGame);
            MessageStore messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore, DefaultIds, DefaultGuildId, DefaultChannelId, DefaultReaderId);
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);

            ReaderCommandHandler handler = new ReaderCommandHandler(commandContext, manager, dbActionFactory);

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
            this.CreateHandler(out ReaderCommandHandler handler, out GameState currentGame, out _);

            await currentGame.AddPlayer(buzzer, "Player");
            await handler.NextAsync();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CanUndoWithReader()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);

            currentGame.ReaderId = DefaultReaderId;
            await currentGame.AddPlayer(buzzer, "Player");
            currentGame.ScorePlayer(10);
            await handler.UndoAsync();

            Assert.IsTrue(
                currentGame.TryGetNextPlayer(out ulong nextPlayerId),
                "Queue should be restored, so we should have a player.");
            Assert.AreEqual(buzzer, nextPlayerId, "Incorrect player in the queue.");

            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains($"@User_{buzzer}", StringComparison.InvariantCulture),
                "Mention should be included in undo message as a prompt.");
        }

        [TestMethod]
        public async Task UndoAfterScoringBonusPromptsForBonus()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupBonusesShootout;

            currentGame.ReaderId = DefaultReaderId;
            await currentGame.AddPlayer(buzzer, "Player");
            currentGame.ScorePlayer(10);
            Assert.IsTrue(currentGame.TryScoreBonus("0"), "Couldn't score the bonus");
            await handler.UndoAsync();

            Assert.AreEqual(PhaseStage.Bonus, currentGame.CurrentStage, "We should be in the bonus stage");
            Assert.AreEqual(1, currentGame.PhaseNumber, "We should be back to the first question");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual(
                "**Bonus for TU 1**", message, "Mention should be included in undo message as a prompt.");
        }

        [TestMethod]
        public async Task SkipBuzzerNoLongerInServerOnUndo()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            ulong buzzerWhoLeft = 999999;
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);

            currentGame.ReaderId = DefaultReaderId;
            Assert.IsTrue(await currentGame.AddPlayer(buzzerWhoLeft, "Player2"), "Couldn't add initial buzz");
            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Couldn't add second buzzer");
            currentGame.ScorePlayer(10);

            await handler.UndoAsync();

            Assert.IsTrue(
                currentGame.TryGetNextPlayer(out ulong nextPlayerId),
                "Queue should be restored, so we should have a player.");
            Assert.AreEqual(buzzer, nextPlayerId, "Incorrect player in the queue.");

            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
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
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);

            currentGame.ReaderId = DefaultReaderId;
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            currentGame.TeamManager = teamManager;
            Assert.IsTrue(teamManager.TryAddTeam("Alpha", out _), "Team should've been added");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(buzzer, "Alpha"), "Should've been able to add the player");
            Assert.IsNotNull(teamManager.GetTeamIdOrNull(buzzer), "Player should have a team");

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(buzzer);
            mockUser.Setup(user => user.Nickname).Returns(nickname);

            await handler.RemovePlayerAsync(mockUser.Object);

            IEnumerable<PlayerTeamPair> players = await teamManager.GetKnownPlayers();
            Assert.IsFalse(players.Any(), "There should be no players left");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains(nickname, StringComparison.InvariantCulture),
                $"Couldn't find username in message\n{message}");
        }

        [TestMethod]
        public async Task CannotRemovePlayerNotOnTeam()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            string nickname = $"User_{buzzer}";
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);

            currentGame.ReaderId = DefaultReaderId;
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            currentGame.TeamManager = teamManager;
            Assert.IsTrue(teamManager.TryAddTeam("Alpha", out _), "Team should've been added");

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(buzzer);
            mockUser.Setup(user => user.Nickname).Returns(nickname);

            await handler.RemovePlayerAsync(mockUser.Object);

            IEnumerable<PlayerTeamPair> players = await teamManager.GetKnownPlayers();
            Assert.IsFalse(players.Any(), "There should be no players");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains($@"Couldn't remove player ""{nickname}""", StringComparison.InvariantCulture),
                $"Couldn't find failure message in message\n{message}");
        }

        [TestMethod]
        public async Task CannotRemovePlayerWithByRoleTeamManager()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            string nickname = $"User_{buzzer}";
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);

            currentGame.ReaderId = DefaultReaderId;

            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild.Setup(guild => guild.Roles).Returns(Array.Empty<IRole>());
            currentGame.TeamManager = new ByRoleTeamManager(mockGuild.Object, "Team");

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser.Setup(user => user.Id).Returns(buzzer);
            mockUser.Setup(user => user.Nickname).Returns(nickname);

            await handler.RemovePlayerAsync(mockUser.Object);

            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual("Removing players isn't supported in this mode.", message, $"Unexpected message");
        }

        [TestMethod]
        public async Task CanAddTeam()
        {
            const string teamName = "My Team";
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            ISelfManagedTeamManager teamManager = new ByCommandTeamManager();
            currentGame.TeamManager = teamManager;

            await handler.AddTeamAsync(teamName);

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(teamIdToName.ContainsKey(teamName), "Team name wasn't added");

            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual($@"Added team ""{teamName}"".", message, $"Unexpected message");
        }

        [TestMethod]
        public async Task CannotAddTeamWithByRoleTeamManager()
        {
            const string teamName = "My Team";
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild.Setup(guild => guild.Roles).Returns(Array.Empty<IRole>());
            currentGame.TeamManager = new ByRoleTeamManager(mockGuild.Object, "Team");

            await handler.AddTeamAsync(teamName);

            IReadOnlyDictionary<string, string> teamIdToName = await currentGame.TeamManager.GetTeamIdToNames();
            Assert.IsFalse(teamIdToName.ContainsKey(teamName), "Team name wasn't added");

            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual("Adding teams isn't supported in this mode.", message, $"Unexpected message");
        }

        [TestMethod]
        public async Task ChangingFormatToHaveBonusIncludesBonusInCurrentPhase()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupBonusesShootout;

            currentGame.ReaderId = DefaultReaderId;
            await currentGame.AddPlayer(buzzer, "Player");
            currentGame.ScorePlayer(10);
            Assert.AreEqual(
                PhaseStage.Bonus, currentGame.CurrentStage, "We should be in a bonus stage in the current phase");
            Assert.AreEqual(1, currentGame.PhaseNumber, "We should still be in the first phase");
        }

        [TestMethod]
        public async Task DisableBonuses()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupBonusesShootout;

            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await handler.DisableBonusesAsync();
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are no longer being tracked. Scores for the current question have been cleared.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupShootout, currentGame.Format, "Unexpected format");
            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Should've been able to add the player again");
        }

        [TestMethod]
        public async Task DisableBonusesFailsIfEnableBonusAlwaysSet()
        {
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupBonusesShootout;

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetUseBonuses(DefaultGuildId, true);
            }

            await handler.DisableBonusesAsync();
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are always tracked in this server. Run !disableBonusesAlways and restart the game to stop tracking bonuses.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupBonusesShootout, currentGame.Format, "Unexpected format");
        }

        [TestMethod]
        public async Task DisableBonusesWhenAlreadyDisabled()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupShootout;

            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await handler.DisableBonusesAsync();
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are already untracked.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupShootout, currentGame.Format, "Unexpected format");
            Assert.IsFalse(await currentGame.AddPlayer(buzzer, "Player"), "Shouldn't be able to add the player again");
        }

        [TestMethod]
        public async Task EnableBonuses()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupShootout;

            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await handler.EnableBonusesAsync();
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are now being tracked. Scores for the current question have been cleared.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupBonusesShootout, currentGame.Format, "Unexpected format");
            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Should've been able to add the player again");
        }

        [TestMethod]
        public async Task EnableBonusesWhenAlreadyEnabled()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.Format = Format.TossupBonusesShootout;

            Assert.IsTrue(await currentGame.AddPlayer(buzzer, "Player"), "Should've been able to add the player");

            await handler.EnableBonusesAsync();
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of channel messages.");
            string message = messageStore.ChannelMessages.First();
            Assert.AreEqual(
                "Bonuses are already tracked.",
                message,
                $"Unexpected message");
            Assert.AreEqual(Format.TossupBonusesShootout, currentGame.Format, "Unexpected format");
            Assert.IsFalse(await currentGame.AddPlayer(buzzer, "Player"), "Shouldn't be able to add the player again");
        }

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private void CreateHandler(
            out ReaderCommandHandler handler, out GameState game, out MessageStore messageStore)
        {
            this.CreateHandler(DefaultIds, out handler, out game, out messageStore);
        }

        private void CreateHandler(
            HashSet<ulong> existingIds,
            out ReaderCommandHandler handler,
            out GameState game,
            out MessageStore messageStore)
        {
            messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore,
                existingIds,
                DefaultGuildId,
                DefaultChannelId,
                userId: DefaultReaderId,
                updateMockGuild: null,
                out _);
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out game);
            game.TeamManager = new ByCommandTeamManager();
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);

            handler = new ReaderCommandHandler(commandContext, manager, dbActionFactory);
        }
    }
}
