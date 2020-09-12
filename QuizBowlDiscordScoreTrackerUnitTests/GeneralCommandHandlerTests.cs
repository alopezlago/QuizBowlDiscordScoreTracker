using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class GeneralCommandHandlerTests : IDisposable
    {
        private const int MaxFieldsInEmbed = 20;
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
        public async Task CanSetReaderToExistingUser()
        {
            this.CreateHandler(
                out GeneralCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            await handler.SetReaderAsync();

            Assert.AreEqual(DefaultReaderId, currentGame.ReaderId, "Reader ID was not set properly.");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
            Assert.IsTrue(
                messageStore.ChannelMessages.First().Contains($"@User_{DefaultReaderId}", StringComparison.InvariantCulture),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CannotSetReaderToNonexistentUser()
        {
            // This will fail, but in our use case this would be impossible.
            ulong readerId = GetNonexistentUserId();
            this.CreateHandler(
                DefaultIds,
                readerId,
                out GeneralCommandHandler handler,
                out GameState currentGame,
                out MessageStore _);
            await handler.SetReaderAsync();

            Assert.IsNull(currentGame.ReaderId, "Reader should not be set for nonexistent user.");
        }

        [TestMethod]
        public async Task SetReaderDoesNotReplaceExistingReader()
        {
            const ulong existingReaderId = 1;
            const ulong newReaderId = 2;

            this.CreateHandler(
                DefaultIds,
                newReaderId,
                out GeneralCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            currentGame.ReaderId = existingReaderId;
            await handler.SetReaderAsync();

            Assert.AreEqual(existingReaderId, currentGame.ReaderId, "Reader ID was not overwritten.");
            Assert.AreEqual(0, messageStore.ChannelMessages.Count, "No messages should be sent.");
        }

        [TestMethod]
        public async Task GetScoreContainsPlayers()
        {
            const int points = 10;

            // Unprivileged users should be able to get the score.
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out GeneralCommandHandler handler,
                out GameState game,
                out MessageStore messageStore);

            game.ReaderId = 0;
            game.AddPlayer(buzzer, $"User_{buzzer}");
            game.ScorePlayer(points);
            await handler.GetScoreAsync();

            Assert.AreEqual(0, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            string[] lines = embed.Split(Environment.NewLine);
            string playerLine = lines.FirstOrDefault(
                line => line.StartsWith($"User_{buzzer}", StringComparison.InvariantCulture));
            Assert.IsNotNull(playerLine, "We should have a field with the user's nickname or username.");
            Assert.IsTrue(
                playerLine.Contains(points.ToString(CultureInfo.InvariantCulture), StringComparison.InvariantCulture),
                "Field should match the player's score.");
        }

        [TestMethod]
        public async Task GetScoreShowsSplits()
        {
            int[] scores = new int[] { 10, 0, -5, 0, 10, 10 };

            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;

            foreach (int score in scores)
            {
                game.AddPlayer(buzzer, "Player");
                game.ScorePlayer(score);

                if (score <= 0)
                {
                    game.NextQuestion();
                }
            }

            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
            messageStore.Clear();

            foreach (int score in Enumerable.Repeat(15, 4))
            {
                game.AddPlayer(buzzer, "Player");
                game.ScorePlayer(score);
            }

            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (4/3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split after addin powers in ""{embed}""");
            messageStore.Clear();

            foreach (int score in Enumerable.Repeat(20, 5))
            {
                game.AddPlayer(buzzer, "Player");
                game.ScorePlayer(score);
            }

            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (5/4/3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split after adding superpowers in ""{embed}""");
        }

        [TestMethod]
        public async Task SuperpowerSplitsShowPowers()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;

            game.AddPlayer(buzzer, "Player");
            game.ScorePlayer(20);

            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (1/0/0/0)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
        }

        [TestMethod]
        public async Task NoPenatliesInSplitsOnlyIfOneHappened()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;

            game.AddPlayer(buzzer, "Player");
            game.ScorePlayer(10);

            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.EndsWith(" (1/0)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
            messageStore.Clear();

            game.AddPlayer(buzzer, "Player");
            game.ScorePlayer(0);
            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (1/0) (1 no penalty buzz)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split after adding a no penalty buzz in ""{embed}""");
        }

        [TestMethod]
        public async Task GetScoreUsesLastName()
        {
            const string oldPlayerName = "Old";
            const string newPlayerName = "New";
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;

            game.AddPlayer(buzzer, oldPlayerName);
            game.ScorePlayer(10);

            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(oldPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the old player name in ""{embed}""");
            messageStore.Clear();

            game.AddPlayer(buzzer, newPlayerName);
            game.ScorePlayer(0);
            await handler.GetScoreAsync();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = messageStore.ChannelEmbeds.First();
            Assert.IsFalse(
                embed.Contains(oldPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Found the old player name in ""{embed}"", even though it shouldn't be in the message");
            Assert.IsTrue(
                embed.Contains(newPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the new player name in ""{embed}""");
        }

        [TestMethod]
        public async Task GetScoreTitleShowsLimitWhenApplicable()
        {
            HashSet<ulong> existingIds = new HashSet<ulong>();
            const ulong lastId = GameState.ScoresListLimit + 1;
            for (ulong i = 1; i <= lastId; i++)
            {
                existingIds.Add(i);
            }

            this.CreateHandler(
                existingIds, out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;
            await handler.GetScoreAsync();

            // There should be no embeds if no one has scored yet.
            messageStore.VerifyChannelEmbeds();
            messageStore.VerifyChannelMessages("No one has scored yet");

            messageStore.Clear();

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                game.AddPlayer(i, $"Player {i}");
                game.ScorePlayer(10);
            }

            await handler.GetScoreAsync();
            int embedCount = (GameState.ScoresListLimit + 1) / MaxFieldsInEmbed;
            Assert.AreEqual(
                embedCount, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after first GetScore.");
            string embed = messageStore.ChannelEmbeds.Last();

            // Get the title, which should be before the first new line
            embed = embed.Substring(0, embed.IndexOf(Environment.NewLine, StringComparison.InvariantCulture));
            Assert.IsFalse(
                embed.Contains(
                    GameState.ScoresListLimit.ToString(CultureInfo.InvariantCulture),
                    StringComparison.InvariantCulture),
                $"When the number of scorers matches the limit, the embed should not contain the scores list limit. Embed: {embed}");

            game.AddPlayer(lastId, $"Player {lastId}");
            game.ScorePlayer(-5);

            messageStore.Clear();

            await handler.GetScoreAsync();
            Assert.AreEqual(
                embedCount, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after second GetScore.");
            embed = messageStore.ChannelEmbeds.Last();

            embed = embed.Substring(0, embed.IndexOf(Environment.NewLine, StringComparison.InvariantCulture));
            Assert.IsTrue(
                embed.Contains(
                    GameState.ScoresListLimit.ToString(CultureInfo.InvariantCulture),
                    StringComparison.InvariantCulture),
                $"Title should contain the scores list limit. Embed: {embed}");
        }

        [TestMethod]
        public async Task GetScoreShowsNoMoreThanLimit()
        {
            HashSet<ulong> existingIds = new HashSet<ulong>();
            const ulong lastId = GameState.ScoresListLimit + 1;
            for (ulong i = 1; i <= lastId; i++)
            {
                existingIds.Add(i);
            }

            this.CreateHandler(
                existingIds, out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                game.AddPlayer(i, $"User_{i}");
                game.ScorePlayer(10);
            }

            await handler.GetScoreAsync();
            int embedCount = (GameState.ScoresListLimit + 1) / MaxFieldsInEmbed;
            Assert.AreEqual(
                embedCount,
                messageStore.ChannelEmbeds.Count,
                "Unexpected number of embeds sent after second GetScore.");

            // The number of partitions should be one more than the number of times the delimiter appears (e.g. a;b is
            // split into a and b, but there is one ;)
            int nicknameFields = messageStore.ChannelEmbeds.Sum(embed => embed.Split("User_").Length - 1);
            Assert.AreEqual(
                GameState.ScoresListLimit,
                nicknameFields,
                $"Number of scorers shown is not the same as the scoring limit.");
        }

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private static ulong GetNonexistentUserId()
        {
            return DefaultIds.Max() + 1;
        }

        private void CreateHandler(
            out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore)
        {
            this.CreateHandler(DefaultIds, out handler, out game, out messageStore);
        }

        private void CreateHandler(
            HashSet<ulong> existingIds,
            out GeneralCommandHandler handler,
            out GameState game,
            out MessageStore messageStore)
        {
            this.CreateHandler(existingIds, DefaultReaderId, out handler, out game, out messageStore);
        }

        private void CreateHandler(
            HashSet<ulong> existingIds,
            ulong userId,
            out GeneralCommandHandler handler,
            out GameState game,
            out MessageStore messageStore)
        {
            messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore,
                existingIds,
                DefaultGuildId,
                DefaultChannelId,
                voiceChannelId: 9999,
                voiceChannelName: "Voice",
                userId: userId,
                out _);
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out game);

            handler = new GeneralCommandHandler(commandContext, manager, options, dbActionFactory);
        }
    }
}
