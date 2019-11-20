using Discord;
using Discord.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class BotCommandHandlerTests
    {
        private const ulong DefaultReaderId = 1;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3 });

        private const ulong DefaultChannelId = 11;

        [TestMethod]
        public async Task CanSetReaderToExistingUser()
        {
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            await handler.SetReader();

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
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                readerId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            await handler.SetReader();

            Assert.IsNull(currentGame.ReaderId, "Reader should not be set for nonexistent user.");
        }

        [TestMethod]
        public async Task SetReaderDoesNotReplaceExistingReader()
        {
            const ulong existingReaderId = 1;
            const ulong newReaderId = 2;

            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                newReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            currentGame.ReaderId = existingReaderId;
            await handler.SetReader();

            Assert.AreEqual(existingReaderId, currentGame.ReaderId, "Reader ID was not overwritten.");
            Assert.AreEqual(0, messageStore.ChannelMessages.Count, "No messages should be sent.");
        }

        [TestMethod]
        public async Task CanSetExistingUserAsNewReader()
        {
            ulong newReaderId = GetExistingNonReaderUserId();
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            currentGame.ReaderId = 0;

            await handler.SetNewReader(newReaderId);

            Assert.AreEqual(newReaderId, currentGame.ReaderId, "Reader ID was not set correctly.");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");

            string newReaderMention = $"@User_{newReaderId}";
            Assert.IsTrue(
                messageStore.ChannelMessages.First().Contains(newReaderMention, StringComparison.InvariantCulture),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CannotSetNewReaderWhenReaderChoosesNonexistentUser()
        {
            ulong newReaderId = GetNonexistentUserId();
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);
            await handler.SetReader();
            messageStore.Clear();

            await handler.SetNewReader(newReaderId);

            Assert.AreEqual(DefaultReaderId, currentGame.ReaderId, "Reader ID should not have been reset.");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");

            string message = messageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains("User could not be found", StringComparison.InvariantCulture),
                $"'User could not be found' was not found in the message. Message: '{message}'.");
        }

        [TestMethod]
        public async Task ClearEmptiesQueue()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.AddPlayer(buzzer);
            await handler.Clear();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong nextPlayerId), "Queue should've been cleared.");
            Assert.IsTrue(currentGame.AddPlayer(buzzer), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task ClearAllRemovesGame()
        {
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState currentGame);
            MessageStore messageStore = new MessageStore();
            ICommandContext commandContext = CreateCommandContext(
                messageStore, DefaultIds, DefaultChannelId, DefaultReaderId);
            BotCommandHandler handler = new BotCommandHandler(commandContext, manager, currentGame);

            await handler.ClearAll();

            Assert.IsFalse(
                manager.TryGet(DefaultChannelId, out GameState game),
                "Game should have been removed from the manager.");
            Assert.AreEqual(1, messageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
        }

        [TestMethod]
        public async Task NextQuestionClears()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.AddPlayer(buzzer);
            await handler.NextQuestion();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong nextPlayerId), "Queue should've been cleared.");
            Assert.IsTrue(currentGame.AddPlayer(buzzer), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CanUndoWithReader()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;
            currentGame.AddPlayer(buzzer);
            currentGame.ScorePlayer(10);
            await handler.Undo();

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
        public async Task GetScoreContainsPlayers()
        {
            const int points = 10;

            // Unprivileged users should be able to get the score.
            ulong buzzer = GetExistingNonReaderUserId();
            CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;
            currentGame.AddPlayer(buzzer);
            currentGame.ScorePlayer(points);
            await handler.GetScore();

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
        public async Task GetScoreTitleShowsLimitWhenApplicable()
        {
            GameState existingState = new GameState();
            existingState.ReaderId = 0;

            HashSet<ulong> existingIds = new HashSet<ulong>();
            const ulong lastId = GameState.ScoresListLimit + 1;
            for (ulong i = 1; i <= lastId; i++)
            {
                existingIds.Add(i);
            }

            CreateHandler(
                existingIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;
            await handler.GetScore();

            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after first GetScore.");
            string embed = messageStore.ChannelEmbeds.Last();
            Assert.IsFalse(
                embed.Contains(
                    GameState.ScoresListLimit.ToString(CultureInfo.InvariantCulture),
                    StringComparison.InvariantCulture),
                $"On start, the title should not contain the scores list limit. Embed: {embed}");

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                currentGame.AddPlayer(i);
                currentGame.ScorePlayer(10);
            }

            await handler.GetScore();
            Assert.AreEqual(2, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after first GetScore.");
            embed = messageStore.ChannelEmbeds.Last();

            // Get the title, which should be before the first new line
            embed = embed.Substring(0, embed.IndexOf(Environment.NewLine, StringComparison.InvariantCulture));
            Assert.IsFalse(
                embed.Contains(
                    GameState.ScoresListLimit.ToString(CultureInfo.InvariantCulture),
                    StringComparison.InvariantCulture),
                $"When the number of scorers matches the limit, the embed should not contain the scores list limit. Embed: {embed}");

            currentGame.AddPlayer(lastId);
            currentGame.ScorePlayer(-5);

            await handler.GetScore();
            Assert.AreEqual(3, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after second GetScore.");
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
            GameState existingState = new GameState();
            existingState.ReaderId = 0;

            HashSet<ulong> existingIds = new HashSet<ulong>();
            const ulong lastId = GameState.ScoresListLimit + 1;
            for (ulong i = 1; i <= lastId; i++)
            {
                existingIds.Add(i);
            }

            CreateHandler(
                existingIds,
                DefaultChannelId,
                0,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                currentGame.AddPlayer(i);
                currentGame.ScorePlayer(10);
            }

            await handler.GetScore();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after second GetScore.");
            string embed = messageStore.ChannelEmbeds.Last();

            // The number of partitions should be one more than the number of times the delimiter appears (e.g. a;b is
            // split into a and b, but there is one ;)
            int nicknameFields = embed.Split("User_").Length - 1;
            Assert.AreEqual(
                GameState.ScoresListLimit,
                nicknameFields,
                $"Number of scorers shown is not the same as the scoring limit.");
        }

        private static void CreateHandler(
            HashSet<ulong> existingUserIds,
            ulong channelId,
            ulong userId,
            out BotCommandHandler handler,
            out GameState currentGame,
            out MessageStore messageStore)
        {
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out currentGame);
            messageStore = new MessageStore();
            ICommandContext commandContext = CreateCommandContext(
                messageStore, existingUserIds, channelId, userId);
            handler = new BotCommandHandler(commandContext, manager, currentGame);
        }

        private static ICommandContext CreateCommandContext(
            MessageStore messageStore, HashSet<ulong> existingUserIds, ulong channelId, ulong userId)
        {
            Mock<ICommandContext> mockCommandContext = new Mock<ICommandContext>();

            Mock<IMessageChannel> mockMessageChannel = new Mock<IMessageChannel>();
            Mock<IUserMessage> mockUserMessage = new Mock<IUserMessage>();
            mockMessageChannel
                .Setup(channel => channel.Id)
                .Returns(channelId);
            mockMessageChannel
                .Setup(channel => channel.SendMessageAsync(It.IsAny<string>(), false, null, It.IsAny<RequestOptions>()))
                .Returns<string, bool, Embed, RequestOptions>((message, isTTS, embed, options) =>
                {
                    messageStore.ChannelMessages.Add(message);
                    return Task.FromResult(mockUserMessage.Object);
                });
            mockMessageChannel
                .Setup(channel => channel.SendMessageAsync(null, false, It.IsAny<Embed>(), It.IsAny<RequestOptions>()))
                .Returns<string, bool, Embed, RequestOptions>((message, isTTS, embed, options) =>
                {
                    messageStore.ChannelEmbeds.Add(GetMockEmbedText(embed));
                    return Task.FromResult(mockUserMessage.Object);
                });

            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild
                .Setup(guild => guild.GetUserAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<ulong, CacheMode, RequestOptions>((id, cacheMode, requestOptions) =>
                {
                    if (existingUserIds?.Contains(id) == true)
                    {
                        return Task.FromResult(CreateGuildUser(id));
                    }

                    return Task.FromResult<IGuildUser>(null);
                });

            mockCommandContext
                .Setup(context => context.User)
                .Returns(CreateGuildUser(userId));
            mockCommandContext
                .Setup(context => context.Channel)
                .Returns(mockMessageChannel.Object);
            mockCommandContext
                .Setup(context => context.Guild)
                .Returns(mockGuild.Object);

            return mockCommandContext.Object;
        }

        private static IGuildUser CreateGuildUser(ulong id)
        {
            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser
                .Setup(user => user.Id)
                .Returns(id);
            mockUser
                .Setup(user => user.Mention)
                .Returns($"@User_{id}");
            mockUser
                .Setup(user => user.Username)
                .Returns($"User_{id}");
            return mockUser.Object;
        }

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private static string GetMockEmbedText(IEmbed embed)
        {
            return GetMockEmbedText(
                embed.Title, embed.Description, embed.Fields.ToDictionary(field => field.Name, field => field.Value));
        }

        private static string GetMockEmbedText(string title, string description, IDictionary<string, string> fields = null)
        {
            string fieldsText = string.Empty;
            if (fields != null)
            {
                fieldsText = string.Join(
                    Environment.NewLine, fields.Select(field => $"{field.Key}: {field.Value}"));
            }
            string embedText = fieldsText.Length > 0 ?
                $"{title}{Environment.NewLine}{description}{Environment.NewLine}{fieldsText}" :
                $"{title}{Environment.NewLine}{description}";
            return embedText;
        }

        private static ulong GetNonexistentUserId()
        {
            return DefaultIds.Max() + 1;
        }
    }
}
