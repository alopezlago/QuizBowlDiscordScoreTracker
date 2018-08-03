using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class BotCommandHandlerTests
    {
        private const ulong DefaultReaderId = 2;
        private const ulong DefaultAdminId = 1;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3 });

        [TestMethod]
        public async Task CanSetReaderToExistingUser()
        {
            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = GetDefaultIds(),
                State = null,
                UserId = DefaultReaderId
            };

            BotCommandHandler handler = new BotCommandHandler();
            await handler.SetReader(context);

            Assert.IsNotNull(context.State, "State should not be null after setting the reader.");
            Assert.AreEqual(DefaultReaderId, context.State.ReaderId, "Reader ID was not set properly.");
            Assert.AreEqual(1, context.SentMessages.Count, "Unexpected number of messages sent.");
            Assert.IsTrue(
                context.SentMessages.First().Contains(context.UserMention),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CannotSetReaderToNonexistentUser()
        {
            // This will fail, but in our use case this would be impossible.
            ulong readerId = GetNonexistentUserId();
            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = GetDefaultIds(),
                State = null,
                UserId = readerId
            };

            BotCommandHandler handler = new BotCommandHandler();
            await handler.SetReader(context);

            Assert.IsNull(context.State, "State should not be created when the reader does not exist.");
        }

        [TestMethod]
        public async Task SetReaderDoesNotReplaceExistingReader()
        {
            const ulong existingReaderId = 1;
            const ulong newReaderId = 2;

            GameState existingState = new GameState();
            existingState.ReaderId = existingReaderId;

            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = GetDefaultIds(),
                State = existingState,
                UserId = newReaderId
            };

            BotCommandHandler handler = new BotCommandHandler();
            await handler.SetReader(context);

            Assert.AreEqual(existingReaderId, context.State.ReaderId, "Reader ID was not overwritten.");
            Assert.AreEqual(0, context.SentMessages.Count, "No messages should be sent.");
        }

        [TestMethod]
        public async Task CanSetNewReaderWhenReaderChoosesExistingUser()
        {
            ulong newReaderId = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithReader(
                async (handler, context) => await handler.SetNewReader(context, newReaderId));

            Assert.AreEqual(newReaderId, newContext.State.ReaderId, "Reader ID was not set correctly.");
            Assert.AreEqual(1, newContext.SentMessages.Count, "Unexpected number of messages sent.");

            string newReaderMention = await newContext.GetUserMention(newReaderId);
            string message = newContext.SentMessages.First();
            Assert.IsTrue(
                message.Contains(newReaderMention),
                $"Message should include the Mention of the user. Message: '{message}'.");
        }

        [TestMethod]
        public async Task CanSetNewReaderWhenAdminChoosesExistingUser()
        {
            ulong newReaderId = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithAdmin(
                async (handler, context) => await handler.SetNewReader(context, newReaderId));

            Assert.AreEqual(newReaderId, newContext.State.ReaderId, "Reader ID was not set correctly.");
            Assert.AreEqual(1, newContext.SentMessages.Count, "Unexpected number of messages sent.");

            string newReaderMention = await newContext.GetUserMention(newReaderId);
            Assert.IsTrue(
                newContext.SentMessages.First().Contains(newReaderMention),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CannotSetNewReaderWhenUnprivilegedUserChoosesExistingUser()
        {
            ulong newReaderId = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithUnprivilegedUser(
                async (handler, context) => await handler.SetNewReader(context, newReaderId));

            Assert.AreEqual(DefaultReaderId, newContext.State.ReaderId, "Reader ID should not have been reset.");
            Assert.AreEqual(0, newContext.SentMessages.Count, "Unexpected number of messages sent.");
        }

        [TestMethod]
        public async Task CannotSetNewReaderWhenReaderChoosesNonexistentUser()
        {
            ulong newReaderId = GetNonexistentUserId();
            MockCommandContextWrapper newContext = await RunWithReader(
                async (handler, context) => await handler.SetNewReader(context, newReaderId));

            Assert.AreEqual(DefaultReaderId, newContext.State.ReaderId, "Reader ID should not have been reset.");
            Assert.AreEqual(1, newContext.SentMessages.Count, "Unexpected number of messages sent.");

            string message = newContext.SentMessages.First();
            Assert.IsTrue(
                message.Contains("User could not be found"),
                $"'User could not be found' was not found in the message. Message: '{message}'.");
        }

        [TestMethod]
        public async Task CanClearWithReader()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithReader(async (handler, context) =>
            {
                context.State.AddPlayer(buzzer);
                await handler.Clear(context);
            });

            Assert.IsFalse(newContext.State.TryGetNextPlayer(out ulong nextPlayerId), "Queue should've been cleared.");
            Assert.IsTrue(newContext.State.AddPlayer(buzzer), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CanClearWithAdmin()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithAdmin(async (handler, context) =>
            {
                context.State.AddPlayer(buzzer);
                await handler.Clear(context);
            });

            Assert.IsFalse(newContext.State.TryGetNextPlayer(out ulong nextPlayerId), "Queue should've been cleared.");
            Assert.IsTrue(newContext.State.AddPlayer(buzzer), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CannotClearWithUnprivilegedUser()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithUnprivilegedUser(async (handler, context) =>
            {
                context.State.AddPlayer(buzzer);
                await handler.Clear(context);
            });

            Assert.IsTrue(
                newContext.State.TryGetNextPlayer(out ulong nextPlayerId),
                "Queue should not have been cleared.");
            Assert.AreEqual(buzzer, nextPlayerId, "Next player in the queue should have been the buzzer.");
        }

        [TestMethod]
        public async Task CanClearAllWithReader()
        {
            MockCommandContextWrapper newContext = await RunWithReader(
                async (handler, context) => await handler.ClearAll(context));
            Assert.IsNull(newContext.State, "Game state should have been cleared completely.");
            Assert.AreEqual(1, newContext.SentMessages.Count, "Unexpected number of messages sent.");
        }

        [TestMethod]
        public async Task CanClearAllWithAdmin()
        {
            MockCommandContextWrapper newContext = await RunWithAdmin(
                async (handler, context) => await handler.ClearAll(context));
            Assert.IsNull(newContext.State, "Game state should have been cleared completely.");
            Assert.AreEqual(1, newContext.SentMessages.Count, "Unexpected number of messages sent.");
        }

        [TestMethod]
        public async Task CannotClearAllWithUnprivilegedUser()
        {
            MockCommandContextWrapper newContext = await RunWithUnprivilegedUser(
                async (handler, context) => await handler.ClearAll(context));
            Assert.IsNotNull(newContext.State, "Game state should have remained the same.");
            Assert.AreEqual(0, newContext.SentMessages.Count, "Unexpected number of messages sent.");
        }

        [TestMethod]
        public async Task GetScoreContainsPlayers()
        {
            const int points = 10;

            // Unprivileged users should be able to get the score.
            ulong buzzer = GetExistingNonReaderUserId();
            MockCommandContextWrapper newContext = await RunWithUnprivilegedUser(async (handler, context) =>
            {
                context.State.AddPlayer(buzzer);
                context.State.ScorePlayer(points);
                await handler.GetScore(context);
            });

            Assert.AreEqual(0, newContext.SentMessages.Count, "Unexpected number of messages sent.");
            Assert.AreEqual(1, newContext.SentEmbeds.Count, "Unexpected number of embeds sent.");

            string nickname = await newContext.GetUserNickname(buzzer);
            DiscordEmbed embed = newContext.SentEmbeds.First();
            DiscordEmbedField field = embed.Fields.FirstOrDefault(f => f.Name == nickname);
            Assert.IsNotNull(field, "We should have a field with the user's nickname.");
            Assert.AreEqual(points.ToString(), field.Value, "Field should match the player's score.");
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

            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = existingIds,
                State = existingState,
                UserId = 1
            };

            BotCommandHandler handler = new BotCommandHandler();
            await handler.GetScore(context);

            Assert.AreEqual(1, context.SentEmbeds.Count, "Unexpected number of embeds sent after first GetScore.");
            DiscordEmbed embed = context.SentEmbeds.Last();
            Assert.IsFalse(
                embed.Title.Contains(GameState.ScoresListLimit.ToString()),
                $"On start, the title should not contain the scores list limit. Title: {embed.Title}");

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                context.State.AddPlayer(i);
                context.State.ScorePlayer(10);
            }

            await handler.GetScore(context);
            Assert.AreEqual(2, context.SentEmbeds.Count, "Unexpected number of embeds sent after first GetScore.");
            embed = context.SentEmbeds.Last();
            Assert.IsFalse(
                embed.Title.Contains(GameState.ScoresListLimit.ToString()),
                $"When the nubmer of scorers matches the limit, the title should not contain the scores list limit. Title: {embed.Title}");

            context.State.AddPlayer(lastId);
            context.State.ScorePlayer(-5);

            await handler.GetScore(context);
            Assert.AreEqual(3, context.SentEmbeds.Count, "Unexpected number of embeds sent after second GetScore.");
            embed = context.SentEmbeds.Last();
            Assert.IsTrue(
                embed.Title.Contains(GameState.ScoresListLimit.ToString()),
                $"Title should contain the scores list limit. Title: {embed.Title}");
        }

        private async Task<MockCommandContextWrapper> RunWithReader(
            Func<BotCommandHandler, MockCommandContextWrapper, Task> asyncAction)
        {
            GameState existingState = new GameState();
            existingState.ReaderId = DefaultReaderId;

            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = GetDefaultIds(),
                State = existingState,
                UserId = DefaultReaderId,
                CanPerformReaderActions = true
            };

            BotCommandHandler handler = new BotCommandHandler();
            await asyncAction(handler, context);
            return context;
        }

        private async Task<MockCommandContextWrapper> RunWithAdmin(
            Func<BotCommandHandler, ICommandContextWrapper, Task> asyncAction)
        {
            GameState existingState = new GameState();
            existingState.ReaderId = DefaultReaderId;

            ConfigOptions options = new ConfigOptions()
            {
                AdminIds = new string[] { DefaultAdminId.ToString() }
            };

            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = GetDefaultIds(),
                State = existingState,
                UserId = DefaultAdminId,
                Options = options,
                CanPerformReaderActions = true
            };

            BotCommandHandler handler = new BotCommandHandler();
            await asyncAction(handler, context);
            return context;
        }

        private async Task<MockCommandContextWrapper> RunWithUnprivilegedUser(
            Func<BotCommandHandler, ICommandContextWrapper, Task> asyncAction)
        {
            GameState existingState = new GameState();
            existingState.ReaderId = DefaultReaderId;

            HashSet<ulong> existingIds = GetDefaultIds();
            ulong userId = existingIds.Except(new ulong[] { DefaultReaderId }).First();

            MockCommandContextWrapper context = new MockCommandContextWrapper()
            {
                ExistingUserIds = GetDefaultIds(),
                State = existingState,
                UserId = userId,
                CanPerformReaderActions = false
            };

            BotCommandHandler handler = new BotCommandHandler();
            await asyncAction(handler, context);
            return context;
        }

        private static HashSet<ulong> GetDefaultIds()
        {
            return new HashSet<ulong>(DefaultIds);
        }

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private static ulong GetNonexistentUserId()
        {
            return DefaultIds.Max() + 1;
        }
    }
}
