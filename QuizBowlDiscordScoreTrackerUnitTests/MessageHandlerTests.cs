using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.TeamManager;
using Serilog;
using Format = QuizBowlDiscordScoreTracker.Format;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class MessageHandlerTests : IDisposable
    {
        private const ulong DefaultReaderId = 1;
        private const ulong DefaultPlayerId = 2;
        private const ulong DefaultSecondPlayerId = 3;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>()
        {
            DefaultReaderId,
            DefaultPlayerId,
            DefaultSecondPlayerId
        };

        private const ulong DefaultGuildId = 9;
        private const ulong DefaultChannelId = 11;
        private const ulong DefaultVoiceChannelId = 222;

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
        public async Task BuzzAndNeg()
        {
            await this.VerifyBuzzAndScore(-5);
        }
        [TestMethod]
        public async Task BuzzAndZero()
        {
            await this.VerifyBuzzAndScore(0);
        }

        [TestMethod]
        public async Task BuzzAndGet()
        {
            await this.VerifyBuzzAndScore(10);
        }

        [TestMethod]
        public async Task BuzzAndPower()
        {
            await this.VerifyBuzzAndScore(15);
        }

        [TestMethod]
        public async Task BuzzAndSuperpower()
        {
            await this.VerifyBuzzAndScore(20);
        }

        [TestMethod]
        public async Task BuzzAndNoPenalty()
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser playerUser,
                out IGuildUser readerUser,
                out IGuildTextChannel channel,
                out MessageStore messageStore);

            await handler.HandlePlayerMessage(state, playerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(playerUser.Mention);
            messageStore.Clear();

            await handler.TryScore(state, readerUser, channel, "no penalty");
            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await state.GetLastScoringSplits();
            PlayerTeamPair pair = new PlayerTeamPair(DefaultPlayerId, playerUser.Username, null);
            Assert.IsTrue(
                lastSplits.TryGetValue(pair, out LastScoringSplit split),
                "Couldn't get scoring split");
            Assert.AreEqual(0, split.Split.Points, "Unexpected number of points");
            messageStore.VerifyChannelMessages();
        }

        [TestMethod]
        public async Task OnlyReaderCanScore()
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser playerUser,
                out IGuildUser readerUser,
                out IGuildTextChannel channel,
                out MessageStore messageStore);

            await state.AddPlayer(DefaultPlayerId, "Player");

            bool scoredBuzz = await handler.TryScore(state, playerUser, channel, "no penalty");
            Assert.IsFalse(scoredBuzz, "Player shouldn't be able to give points");
            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await state.GetLastScoringSplits();
            PlayerTeamPair pair = new PlayerTeamPair(DefaultPlayerId, "Player", null);
            Assert.IsFalse(lastSplits.TryGetValue(pair, out _), "Scoring split shouldn't exist");

            scoredBuzz = await handler.TryScore(state, readerUser, channel, "10");
            Assert.IsTrue(scoredBuzz, "Buzz wasn't scored");
            lastSplits = await state.GetLastScoringSplits();
            Assert.IsTrue(
                lastSplits.TryGetValue(pair, out LastScoringSplit split),
                "Couldn't get scoring split"); ;
            Assert.AreEqual(10, split.Split.Points, "Unexpected number of points");
            messageStore.VerifyChannelMessages("**TU 2**");
        }

        [TestMethod]
        public async Task PlayerAfterWithdrawnPlayerIsPrompted()
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser firstPlayerUser,
                out _,
                out IGuildTextChannel channel,
                out MessageStore messageStore);
            IGuildUser secondPlayerUser = CommandMocks.CreateGuildUser(DefaultSecondPlayerId);

            await handler.HandlePlayerMessage(state, firstPlayerUser, channel, "buzz");
            await handler.HandlePlayerMessage(state, secondPlayerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(firstPlayerUser.Mention);
            messageStore.Clear();

            await handler.HandlePlayerMessage(state, firstPlayerUser, channel, "wd");
            messageStore.VerifyChannelMessages(secondPlayerUser.Mention);

            Assert.IsTrue(state.TryGetNextPlayer(out ulong nextPlayerId), "Couldn't get another player");
            Assert.AreEqual(DefaultSecondPlayerId, nextPlayerId, "Unexpected user prompted");
        }

        [TestMethod]
        public async Task NoMessageWhenNonPromptedPlayerWithdraws()
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser firstPlayerUser,
                out _,
                out IGuildTextChannel channel,
                out MessageStore messageStore);
            IGuildUser secondPlayerUser = CommandMocks.CreateGuildUser(DefaultSecondPlayerId);

            await handler.HandlePlayerMessage(state, firstPlayerUser, channel, "buzz");
            await handler.HandlePlayerMessage(state, secondPlayerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(firstPlayerUser.Mention);
            messageStore.Clear();

            await handler.HandlePlayerMessage(state, secondPlayerUser, channel, "wd");
            messageStore.VerifyChannelMessages();

            Assert.IsTrue(state.TryGetNextPlayer(out ulong nextPlayerId), "Couldn't get another player");
            Assert.AreEqual(DefaultPlayerId, nextPlayerId, "Unexpected user prompted");

            state.ScorePlayer(0);
            Assert.IsFalse(state.TryGetNextPlayer(out _), "Second player should've been withdrawn");
        }

        [TestMethod]
        public async Task CanBuzzAfterWithdrawl()
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser playerUser,
                out _,
                out IGuildTextChannel channel,
                out MessageStore messageStore);

            await handler.HandlePlayerMessage(state, playerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(playerUser.Mention);
            messageStore.Clear();

            await handler.HandlePlayerMessage(state, playerUser, channel, "wd");
            messageStore.VerifyChannelMessages($"{playerUser.Mention} has withdrawn.");
            messageStore.Clear();

            await handler.HandlePlayerMessage(state, playerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(playerUser.Mention);

            Assert.IsTrue(
                state.TryGetNextPlayer(out ulong nextPlayerId),
                "Couldn't get another player");
            Assert.AreEqual(DefaultPlayerId, nextPlayerId, "Unexpected user prompted");
        }

        [TestMethod]
        public async Task CanScoreBonus()
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser playerUser,
                out IGuildUser readerUser,
                out IGuildTextChannel channel,
                out MessageStore messageStore);
            state.Format = Format.TossupBonusesShootout;

            await handler.HandlePlayerMessage(state, playerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(playerUser.Mention);
            messageStore.Clear();

            bool scoredBuzz = await handler.TryScore(state, readerUser, channel, "10");
            Assert.IsTrue(scoredBuzz, "Buzz wasn't scored");
            Assert.AreEqual(PhaseStage.Bonus, state.CurrentStage, "Unexpected stage after the buzz");
            messageStore.VerifyChannelMessages("**Bonus for 1**");
            messageStore.Clear();

            // Post something that shouldn't be a bonus score, then post something that can be a bonus score
            bool bonusScored = await handler.TryScore(state, readerUser, channel, "Some non-score text");
            Assert.IsFalse(bonusScored);
            Assert.AreEqual(PhaseStage.Bonus, state.CurrentStage, "Phase should not have changed");

            bonusScored = await handler.TryScore(state, readerUser, channel, "10/10/0");
            Assert.IsTrue(bonusScored, "Bonus wasn't scored");
            Assert.AreEqual(PhaseStage.Tossup, state.CurrentStage, "Unexpected stage after the bonus was scored");
            Assert.AreEqual(2, state.PhaseNumber, "Phase number didn't change");
            messageStore.VerifyChannelMessages("**TU 2**");
        }

        private async Task VerifyBuzzAndScore(int score)
        {
            this.CreateHandler(
                out MessageHandler handler,
                out GameState state,
                out IGuildUser playerUser,
                out IGuildUser readerUser,
                out IGuildTextChannel channel,
                out MessageStore messageStore);

            await handler.HandlePlayerMessage(state, playerUser, channel, "buzz");
            messageStore.VerifyChannelMessages(playerUser.Mention);
            messageStore.Clear();

            bool scoredBuzz = await handler.TryScore(
                state, readerUser, channel, score.ToString(CultureInfo.InvariantCulture));
            Assert.IsTrue(scoredBuzz, "Buzz wasn't scored");
            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await state.GetLastScoringSplits();
            PlayerTeamPair pair = new PlayerTeamPair(DefaultPlayerId, playerUser.Username, null);
            Assert.IsTrue(
                lastSplits.TryGetValue(pair, out LastScoringSplit split),
                "Couldn't get scoring split");
            Assert.AreEqual(score, split.Split.Points, "Unexpected number of points");

            if (score > 0)
            {
                messageStore.VerifyChannelMessages("**TU 2**");
            }
        }

        private void CreateHandler(
            out MessageHandler handler,
            out GameState state,
            out IGuildUser playerUser,
            out IGuildUser readerUser,
            out IGuildTextChannel channel,
            out MessageStore messageStore)
        {
            messageStore = new MessageStore();
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            handler = new MessageHandler(
                options, dbActionFactory, CommandMocks.CreateHubContext(), new Mock<ILogger>().Object);

            playerUser = CommandMocks.CreateGuildUser(DefaultPlayerId);
            readerUser = CommandMocks.CreateGuildUser(DefaultReaderId);
            CommandMocks.CreateGuild(
                messageStore,
                DefaultIds,
                DefaultGuildId,
                DefaultChannelId,
                (mockGuild, textChannel) =>
                {
                    Mock<IVoiceChannel> mockVoiceChannel = new Mock<IVoiceChannel>();
                    mockVoiceChannel.Setup(voiceChannel => voiceChannel.Id).Returns(DefaultVoiceChannelId);
                    mockVoiceChannel.Setup(voiceChannel => voiceChannel.Name).Returns("Voice");
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
                },
                null,
                out channel);
            state = new GameState()
            {
                ReaderId = DefaultReaderId,
                TeamManager = new ByCommandTeamManager()
            };
        }
    }
}
