using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Serilog;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class BotCommandHandlerTests : IDisposable
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
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                readerId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore _);
            await handler.SetReader();

            Assert.IsNull(currentGame.ReaderId, "Reader should not be set for nonexistent user.");
        }

        [TestMethod]
        public async Task SetReaderDoesNotReplaceExistingReader()
        {
            const ulong existingReaderId = 1;
            const ulong newReaderId = 2;

            this.CreateHandler(
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
            this.CreateHandler(
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
            this.CreateHandler(
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
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore _);

            currentGame.AddPlayer(buzzer, "Player");
            await handler.Clear();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(currentGame.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task ClearAllRemovesGame()
        {
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState currentGame);
            MessageStore messageStore = new MessageStore();
            ICommandContext commandContext = CreateCommandContext(
                messageStore, DefaultIds, DefaultChannelId, DefaultReaderId);


            BotCommandHandler handler = new BotCommandHandler(
                commandContext,
                manager,
                currentGame,
                Mock.Of<ILogger>(),
                CreateConfigurationOptionsMonitor(),
                this.CreateDatabaseActionFactory());

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
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore _);

            currentGame.AddPlayer(buzzer, "Player");
            await handler.NextQuestion();

            Assert.IsFalse(currentGame.TryGetNextPlayer(out ulong _), "Queue should've been cleared.");
            Assert.IsTrue(currentGame.AddPlayer(buzzer, "Player"), "We should be able to add the buzzer again.");
        }

        [TestMethod]
        public async Task CanUndoWithReader()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;
            currentGame.AddPlayer(buzzer, "Player");
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
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;
            currentGame.AddPlayer(buzzer, $"User_{buzzer}");
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
        public async Task GetScoreShowsSplits()
        {
            int[] scores = new int[] { 10, 0, -5, 0, 10, 10 };

            ulong buzzer = GetExistingNonReaderUserId();
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;

            foreach (int score in scores)
            {
                currentGame.AddPlayer(buzzer, "Player");
                currentGame.ScorePlayer(score);

                if (score <= 0)
                {
                    currentGame.NextQuestion();
                }
            }

            await handler.GetScore();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
            messageStore.Clear();

            foreach (int score in Enumerable.Repeat(15, 4))
            {
                currentGame.AddPlayer(buzzer, "Player");
                currentGame.ScorePlayer(score);
            }

            await handler.GetScore();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (4/3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split after addin powers in ""{embed}""");
            messageStore.Clear();

            foreach (int score in Enumerable.Repeat(20, 5))
            {
                currentGame.AddPlayer(buzzer, "Player");
                currentGame.ScorePlayer(score);
            }

            await handler.GetScore();
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
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;

            currentGame.AddPlayer(buzzer, "Player");
            currentGame.ScorePlayer(20);

            await handler.GetScore();
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
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;

            currentGame.AddPlayer(buzzer, "Player");
            currentGame.ScorePlayer(10);

            await handler.GetScore();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.EndsWith(" (1/0)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
            messageStore.Clear();

            currentGame.AddPlayer(buzzer, "Player");
            currentGame.ScorePlayer(0);
            await handler.GetScore();
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
                DefaultIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;

            currentGame.AddPlayer(buzzer, oldPlayerName);
            currentGame.ScorePlayer(10);

            await handler.GetScore();
            messageStore.VerifyChannelMessages();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(oldPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the old player name in ""{embed}""");
            messageStore.Clear();

            currentGame.AddPlayer(buzzer, newPlayerName);
            currentGame.ScorePlayer(0);
            await handler.GetScore();
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
                existingIds,
                DefaultChannelId,
                DefaultReaderId,
                out BotCommandHandler handler,
                out GameState currentGame,
                out MessageStore messageStore);

            currentGame.ReaderId = 0;
            await handler.GetScore();

            // There should be no embeds if no one has scored yet.
            messageStore.VerifyChannelEmbeds();
            messageStore.VerifyChannelMessages("No one has scored yet");

            messageStore.Clear();

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                currentGame.AddPlayer(i, $"Player {i}");
                currentGame.ScorePlayer(10);
            }

            await handler.GetScore();
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

            currentGame.AddPlayer(lastId, $"Player {lastId}");
            currentGame.ScorePlayer(-5);

            messageStore.Clear();

            await handler.GetScore();
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
                currentGame.AddPlayer(i, $"User_{i}");
                currentGame.ScorePlayer(10);
            }

            await handler.GetScore();
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

        [TestMethod]
        public async Task SetTeamRole()
        {
            const string prefix = "Team #";
            const string newPrefix = "New Team #";
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                0,
                out BotCommandHandler handler,
                out GameState _,
                out MessageStore messageStore);

            await handler.SetTeamRolePrefix(prefix);
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            messageStore.Clear();

            await handler.GetTeamRolePrefix();
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after getting the team role");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\"");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different");

            messageStore.Clear();

            await handler.SetTeamRolePrefix(newPrefix);
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(newPrefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\" after update");

            messageStore.Clear();

            await handler.GetTeamRolePrefix();
            Assert.AreEqual(
                1,
                messageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\" after update");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different after update");
        }

        [TestMethod]
        public async Task ClearTeamRole()
        {
            const string prefix = "Team #";
            this.CreateHandler(
                DefaultIds,
                DefaultChannelId,
                0,
                out BotCommandHandler handler,
                out GameState _,
                out MessageStore messageStore);

            await handler.SetTeamRolePrefix(prefix);
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            messageStore.Clear();

            await handler.ClearTeamRolePrefix();
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            string clearMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                clearMessage.Contains("unset", StringComparison.InvariantCulture),
                @$"""unset"" not in message ""{clearMessage}"" after update");

            messageStore.Clear();

            await handler.GetTeamRolePrefix();
            Assert.AreEqual(
                1,
                messageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.AreEqual("No team prefix used", getMessage, $"The team role prefix was not cleared");
        }

        [TestMethod]
        public async Task PairChannels()
        {
            const string voiceChannelName = "Packet Voice";
            const ulong voiceChannelId = DefaultChannelId + 10;
            this.CreateHandler(
                DefaultChannelId,
                voiceChannelId,
                voiceChannelName,
                DefaultReaderId,
                out BotCommandHandler handler,
                out MessageStore messageStore,
                out IGuildTextChannel textChannel);

            await handler.PairChannels(textChannel, voiceChannelName);

            // TODO: Check the exact string once this issue is fixed:
            // https://github.com/alopezlago/QuizBowlDiscordScoreTracker/issues/23
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains("success", StringComparison.InvariantCulture),
                @$"Pairing message doesn't mention ""success"". Message: {setMessage}");
            messageStore.Clear();

            await handler.GetPairedChannel(textChannel);

            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(voiceChannelName, StringComparison.InvariantCulture),
                $"Voice channel name not found in get message. Message: {getMessage}");
        }

        [TestMethod]
        public async Task UnpairChannel()
        {
            const string voiceChannelName = "Packet Voice";
            const ulong voiceChannelId = DefaultChannelId + 10;
            this.CreateHandler(
                DefaultChannelId,
                voiceChannelId,
                voiceChannelName,
                DefaultReaderId,
                out BotCommandHandler handler,
                out MessageStore messageStore,
                out IGuildTextChannel textChannel);

            await handler.PairChannels(textChannel, voiceChannelName);

            // TODO: Check the exact string once this issue is fixed:
            // https://github.com/alopezlago/QuizBowlDiscordScoreTracker/issues/23
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains("success", StringComparison.InvariantCultureIgnoreCase),
                @$"Pairing message doesn't mention ""success"". Message: {setMessage}");
            messageStore.Clear();

            await handler.UnpairChannel(textChannel);

            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains("unpair", StringComparison.InvariantCultureIgnoreCase),
                @$"Unpairing message doesn't mention ""unpaired"". Message: {getMessage}");
        }

        private void CreateHandler(
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
            handler = new BotCommandHandler(
                commandContext,
                manager,
                currentGame,
                Mock.Of<ILogger>(),
                CreateConfigurationOptionsMonitor(),
                this.CreateDatabaseActionFactory());
        }

        private void CreateHandler(
            ulong textChannelId,
            ulong voiceChannelId,
            string voiceChannelName,
            ulong userId,
            out BotCommandHandler handler,
            out MessageStore messageStore,
            out IGuildTextChannel textChannel)
        {
            // We need a local copy for the mocked methods
            MessageStore localMessageStore = new MessageStore();
            messageStore = localMessageStore;

            Mock<ICommandContext> mockCommandContext = new Mock<ICommandContext>();
            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild.Setup(guild => guild.Id).Returns(777);

            Mock<IVoiceChannel> mockVoiceChannel = new Mock<IVoiceChannel>();
            mockVoiceChannel.Setup(voiceChannel => voiceChannel.Id).Returns(voiceChannelId);
            mockVoiceChannel.Setup(voiceChannel => voiceChannel.Name).Returns(voiceChannelName);
            mockGuild
                .Setup(guild => guild.GetVoiceChannelAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(Task.FromResult(mockVoiceChannel.Object));

            List<IVoiceChannel> voiceChannels = new List<IVoiceChannel>()
            {
                mockVoiceChannel.Object
            };
            mockGuild
                .Setup(guild => guild.GetVoiceChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(Task.FromResult<IReadOnlyCollection<IVoiceChannel>>(voiceChannels));

            Mock<IUserMessage> mockUserMessage = new Mock<IUserMessage>();
            Mock<IGuildTextChannel> mockMessageChannel = new Mock<IGuildTextChannel>();
            mockMessageChannel
                .Setup(channel => channel.Id)
                .Returns(textChannelId);
            mockMessageChannel
                .Setup(channel => channel.SendMessageAsync(It.IsAny<string>(), false, null, It.IsAny<RequestOptions>()))
                .Returns<string, bool, Embed, RequestOptions>((message, isTTS, embed, options) =>
                {
                    localMessageStore.ChannelMessages.Add(message);
                    return Task.FromResult(mockUserMessage.Object);
                });
            mockMessageChannel
                .Setup(channel => channel.SendMessageAsync(null, false, It.IsAny<Embed>(), It.IsAny<RequestOptions>()))
                .Returns<string, bool, Embed, RequestOptions>((message, isTTS, embed, options) =>
                {
                    localMessageStore.ChannelEmbeds.Add(GetMockEmbedText(embed));
                    return Task.FromResult(mockUserMessage.Object);
                });
            mockMessageChannel
                .Setup(channel => channel.Name)
                .Returns("gameChannel");
            mockMessageChannel
                .Setup(channel => channel.Guild)
                .Returns(mockGuild.Object);

            textChannel = mockMessageChannel.Object;

            mockCommandContext
                .Setup(context => context.User)
                .Returns(CreateGuildUser(userId));
            mockCommandContext
                .Setup(context => context.Channel)
                .Returns(mockMessageChannel.Object);
            mockCommandContext
                .Setup(context => context.Guild)
                .Returns(mockGuild.Object);

            GameStateManager manager = new GameStateManager();
            handler = new BotCommandHandler(
                mockCommandContext.Object,
                manager,
                null,
                Mock.Of<ILogger>(),
                CreateConfigurationOptionsMonitor(),
                this.CreateDatabaseActionFactory());
        }

        private static ICommandContext CreateCommandContext(
            MessageStore messageStore, HashSet<ulong> existingUserIds, ulong channelId, ulong userId)
        {
            Mock<ICommandContext> mockCommandContext = new Mock<ICommandContext>();

            Mock<IGuildTextChannel> mockMessageChannel = new Mock<IGuildTextChannel>();
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
            mockMessageChannel
                .Setup(channel => channel.Name)
                .Returns("gameChannel");

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
            mockGuild.Setup(guild => guild.Id).Returns(DefaultGuildId);

            Mock<IGuildUser> mockBotUser = new Mock<IGuildUser>();
            mockBotUser
                .Setup(user => user.GetPermissions(It.IsAny<IGuildChannel>()))
                .Returns(new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true));
            mockGuild
                .Setup(guild => guild.GetCurrentUserAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(Task.FromResult(mockBotUser.Object));

            mockMessageChannel
                .Setup(channel => channel.Guild)
                .Returns(mockGuild.Object);

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

        private static IOptionsMonitor<BotConfiguration> CreateConfigurationOptionsMonitor()
        {
            Mock<IOptionsMonitor<BotConfiguration>> mockOptionsMonitor = new Mock<IOptionsMonitor<BotConfiguration>>();
            Mock<BotConfiguration> mockConfiguration = new Mock<BotConfiguration>();
            mockConfiguration
                .Setup(config => config.DatabaseDataSource)
                .Returns("memory&cached=true");

            // We can't set the WebURL directly without making it virtual or adding an interface for BotConfiguration
            mockOptionsMonitor.Setup(options => options.CurrentValue).Returns(mockConfiguration.Object);
            return mockOptionsMonitor.Object;
        }

        private IDatabaseActionFactory CreateDatabaseActionFactory()
        {
            // TODO: See how we can dispose this correctly

            Mock<IDatabaseActionFactory> mockDbActionFactory = new Mock<IDatabaseActionFactory>();
            mockDbActionFactory
                .Setup(dbActionFactory => dbActionFactory.Create())
                .Returns(() =>
                {
                    return new DatabaseAction(this.botConfigurationfactory.Create());
                });
            return mockDbActionFactory.Object;
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
