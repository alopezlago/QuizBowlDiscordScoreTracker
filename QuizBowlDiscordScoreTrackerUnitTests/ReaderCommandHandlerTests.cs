using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class ReaderCommandHandlerTests
    {
        private const ulong DefaultReaderId = 1;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3 });

        private const ulong DefaultChannelId = 11;
        private const ulong DefaultGuildId = 9;

        [TestMethod]
        public async Task CanSetExistingUserAsNewReader()
        {
            ulong newReaderId = GetExistingNonReaderUserId();
            string newReaderMention = $"@User_{newReaderId}";
            CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);
            currentGame.ReaderId = 0;

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
            CreateHandler(out ReaderCommandHandler handler, out GameState currentGame, out _);

            currentGame.AddPlayer(buzzer, "Player");
            await handler.ClearAsync();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(currentGame.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task ClearAllRemovesGame()
        {
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState currentGame);
            MessageStore messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore, DefaultIds, DefaultGuildId, DefaultChannelId, DefaultReaderId);

            ReaderCommandHandler handler = new ReaderCommandHandler(commandContext, manager);

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
            CreateHandler(out ReaderCommandHandler handler, out GameState currentGame, out _);

            currentGame.AddPlayer(buzzer, "Player");
            await handler.NextAsync();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(currentGame.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CanUndoWithReader()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            CreateHandler(
                out ReaderCommandHandler handler, out GameState currentGame, out MessageStore messageStore);

            currentGame.ReaderId = 0;
            currentGame.AddPlayer(buzzer, "Player");
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

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private static void CreateHandler(
            out ReaderCommandHandler handler, out GameState game, out MessageStore messageStore)
        {
            CreateHandler(DefaultIds, out handler, out game, out messageStore);
        }

        private static void CreateHandler(
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
                voiceChannelId: 9999,
                voiceChannelName: "Voice",
                userId: DefaultReaderId,
                out _);
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out game);

            handler = new ReaderCommandHandler(commandContext, manager);
        }
    }
}
