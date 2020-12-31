using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
using QuizBowlDiscordScoreTracker.TeamManager;
using Format = QuizBowlDiscordScoreTracker.Format;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class GeneralCommandHandlerTests : IDisposable
    {
        private const ulong DefaultReaderId = 10;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3, DefaultReaderId });

        private const ulong DefaultChannelId = 11;
        private const ulong DefaultGuildId = 9;
        private const ulong FirstTeamRoleId = 1001;
        private const string FirstTeamName = "Alpha";
        private const ulong SecondTeamRoleId = FirstTeamRoleId + 1;
        private const string SecondTeamName = "Beta";
        private const ulong ThirdTeamRoleId = FirstTeamRoleId + 2;
        private const string ThirdTeamName = "Gamma";

        private const ulong DefaultReaderRoleId = 1001;
        private const string DefaultReaderRoleName = "Readers";

        private InMemoryBotConfigurationContextFactory BotConfigurationfactory { get; set; }

        private GeneralCommandHandler Handler { get; set; }

        private GameState Game { get; set; }

        private MessageStore MessageStore { get; set; }

        [TestInitialize]
        public void InitializeTest()
        {
            this.BotConfigurationfactory = new InMemoryBotConfigurationContextFactory();

            // Make sure the database is initialized before running the test
            using (BotConfigurationContext context = this.BotConfigurationfactory.Create())
            {
                context.Database.Migrate();
            }

            // Clear out the old fields
            this.Handler = null;
            this.Game = null;
            this.MessageStore = null;
        }

        [TestCleanup]
        public void Dispose()
        {
            this.BotConfigurationfactory.Dispose();
        }

        [TestMethod]
        public async Task CanSetReaderToExistingUser()
        {
            this.InitializeHandler();
            await this.Handler.SetReaderAsync();

            Assert.AreEqual(DefaultReaderId, this.Game.ReaderId, "Reader ID was not set properly.");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
            Assert.IsTrue(
                this.MessageStore.ChannelMessages.First().Contains($"@User_{DefaultReaderId}", StringComparison.InvariantCulture),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CannotSetReaderToNonexistentUser()
        {
            // This will fail, but in our use case this would be impossible.
            ulong readerId = GetNonexistentUserId();
            this.InitializeHandler(
                DefaultIds,
                readerId);
            await this.Handler.SetReaderAsync();

            Assert.IsNull(this.Game.ReaderId, "Reader should not be set for nonexistent user.");
        }

        [TestMethod]
        public async Task CanSetReaderToUserWithReaderRole()
        {
            this.InitializeHandler(DefaultIds, DefaultReaderId, TeamManagerType.Solo, (mockGuildUser) =>
            {
                mockGuildUser.Setup(user => user.RoleIds).Returns(new ulong[] { DefaultReaderRoleId });
            });

            using (BotConfigurationContext context = this.BotConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetReaderRolePrefixAsync(
                    DefaultGuildId, DefaultReaderRoleName.Substring(0, DefaultReaderRoleName.Length - 1));
            }

            await this.Handler.SetReaderAsync();

            Assert.AreEqual(DefaultReaderId, this.Game.ReaderId, "Reader ID was not set properly.");
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
            Assert.IsTrue(
                this.MessageStore.ChannelMessages.First().Contains($"@User_{DefaultReaderId}", StringComparison.InvariantCulture),
                "Message should include the Mention of the user.");
        }

        [TestMethod]
        public async Task CannotSetReaderToUserWithoutReaderRole()
        {
            string readerRolePrefix = DefaultReaderRoleName.Substring(0, DefaultReaderRoleName.Length - 1);

            this.InitializeHandler(DefaultIds, DefaultReaderId, TeamManagerType.Solo, (mockGuildUser) =>
            {
                mockGuildUser.Setup(user => user.RoleIds).Returns(Array.Empty<ulong>());
            });

            using (BotConfigurationContext context = this.BotConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetReaderRolePrefixAsync(DefaultGuildId, readerRolePrefix);
            }

            await this.Handler.SetReaderAsync();

            Assert.IsNull(this.Game.ReaderId, "Reader should not be set for nonexistent user.");

            this.MessageStore.VerifyChannelMessages(
                @$"@User_{DefaultReaderId} can't read because they don't have a role starting with the prefix ""{readerRolePrefix}"".");
        }

        [TestMethod]
        public async Task SetReaderDoesNotReplaceExistingReader()
        {
            const ulong existingReaderId = 1;
            const ulong newReaderId = 2;

            this.InitializeHandler(
                DefaultIds,
                newReaderId);
            this.Game.ReaderId = existingReaderId;
            await this.Handler.SetReaderAsync();

            Assert.AreEqual(existingReaderId, this.Game.ReaderId, "Reader ID was not overwritten.");
            Assert.AreEqual(0, this.MessageStore.ChannelMessages.Count, "No messages should be sent.");
        }

        [TestMethod]
        public async Task GetScoreContainsPlayers()
        {
            const int points = 10;

            // Unprivileged users should be able to get the score.
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();

            this.Game.ReaderId = 0;
            await this.Game.AddPlayer(buzzer, $"User_{buzzer}");
            this.Game.ScorePlayer(points);
            await this.Handler.GetScoreAsync();

            Assert.AreEqual(0, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages sent.");
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            string[] lines = embed.Split(Environment.NewLine);
            string playerLine = lines.FirstOrDefault(
                line => line.Contains($"User\\_{buzzer}", StringComparison.InvariantCulture));
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
            this.InitializeHandler();
            this.Game.ReaderId = 0;

            foreach (int score in scores)
            {
                await this.Game.AddPlayer(buzzer, "Player");
                this.Game.ScorePlayer(score);

                if (score <= 0)
                {
                    this.Game.NextQuestion();
                }
            }

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
            this.MessageStore.Clear();

            foreach (int score in Enumerable.Repeat(15, 4))
            {
                await this.Game.AddPlayer(buzzer, "Player");
                this.Game.ScorePlayer(score);
            }

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (4/3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split after addin powers in ""{embed}""");
            this.MessageStore.Clear();

            foreach (int score in Enumerable.Repeat(20, 5))
            {
                await this.Game.AddPlayer(buzzer, "Player");
                this.Game.ScorePlayer(score);
            }

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (5/4/3/1) (2 no penalty buzzes)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split after adding superpowers in ""{embed}""");
        }

        [TestMethod]
        public async Task SuperpowerSplitsShowPowers()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.ReaderId = 0;

            await this.Game.AddPlayer(buzzer, "Player");
            this.Game.ScorePlayer(20);

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(" (1/0/0/0)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
        }

        [TestMethod]
        public async Task NoPenatliesInSplitsOnlyIfOneHappened()
        {
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            await this.Game.AddPlayer(buzzer, "Player");
            this.Game.ScorePlayer(10);

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.EndsWith(" (1/0)", StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the correct split in ""{embed}""");
            this.MessageStore.Clear();

            await this.Game.AddPlayer(buzzer, "Player");
            this.Game.ScorePlayer(0);
            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = this.MessageStore.ChannelEmbeds.First();
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
            this.InitializeHandler();
            this.Game.ReaderId = 0;

            await this.Game.AddPlayer(buzzer, oldPlayerName);
            this.Game.ScorePlayer(10);

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(oldPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the old player name in ""{embed}""");
            this.MessageStore.Clear();

            await this.Game.AddPlayer(buzzer, newPlayerName);
            this.Game.ScorePlayer(0);
            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsFalse(
                embed.Contains(oldPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Found the old player name in ""{embed}"", even though it shouldn't be in the message");
            Assert.IsTrue(
                embed.Contains(newPlayerName, StringComparison.InvariantCultureIgnoreCase),
                @$"Could not find the new player name in ""{embed}""");
        }

        [TestMethod]
        public async Task GetScoreShowsBonusStats()
        {
            const string playerName = "Player";
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            this.Game.Format = Format.TossupBonusesShootout;

            await this.Game.AddPlayer(buzzer, playerName);
            this.Game.ScorePlayer(10);
            Assert.IsTrue(this.Game.TryScoreBonus("10/0/0"), "First bonus should be scored");
            await this.Game.AddPlayer(buzzer, playerName);
            this.Game.ScorePlayer(15);
            Assert.IsTrue(this.Game.TryScoreBonus("10/10/0"), "Second bonus should be scored");

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("🥇 Player: **55** (1/1/0)", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find correct individual stats in embed\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    "Bonuses heard: 2    Points: 30    PPB: 15.00", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find bonus stats in embed\n{embed}");
        }

        [TestMethod]
        public async Task BonusStatsRoundedToNearestHundreth()
        {
            const string playerName = "Player";
            ulong buzzer = GetExistingNonReaderUserId();
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            this.Game.Format = Format.TossupBonusesShootout;

            await this.Game.AddPlayer(buzzer, playerName);
            this.Game.ScorePlayer(10);
            Assert.IsTrue(this.Game.TryScoreBonus("0"), "First bonus should be scored");

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(
                    "PPB: 0.00", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find correct PPB after the first bonus in embed\n{embed}");
            this.MessageStore.Clear();

            await this.Game.AddPlayer(buzzer, playerName);
            this.Game.ScorePlayer(10);
            Assert.IsTrue(this.Game.TryScoreBonus("10/0/0"), "Second bonus should be scored");
            await this.Game.AddPlayer(buzzer, playerName);
            this.Game.ScorePlayer(10);
            Assert.IsTrue(this.Game.TryScoreBonus("10/0/0"), "Third bonus should be scored");

            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(
                    "PPB: 6.67", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find correct PPB after the third bonus in embed\n{embed}");
            this.MessageStore.Clear();

            await this.Game.AddPlayer(buzzer, playerName);
            this.Game.ScorePlayer(10);
            Assert.IsTrue(this.Game.TryScoreBonus("30"), "Fourth bonus should be scored");
            await this.Handler.GetScoreAsync();
            this.MessageStore.VerifyChannelMessages();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent.");

            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(
                    "PPB: 12.50", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find correct PPB after the fourth bonus in embed\n{embed}");
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

            this.InitializeHandler();
            this.Game.ReaderId = 0;
            await this.Handler.GetScoreAsync();

            // There should be no embeds if no one has scored yet.
            this.MessageStore.VerifyChannelEmbeds();
            this.MessageStore.VerifyChannelMessages("No one has scored yet");

            this.MessageStore.Clear();

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                await this.Game.AddPlayer(i, $"Player {i}");
                this.Game.ScorePlayer(10);
            }

            await this.Handler.GetScoreAsync();
            int embedCount = (GameState.ScoresListLimit + 1) / EmbedBuilder.MaxFieldCount;
            Assert.AreEqual(
                embedCount, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after first GetScore.");
            string embed = this.MessageStore.ChannelEmbeds.Last();

            // Get the title, which should be before the first new line
            embed = embed.Substring(0, embed.IndexOf(Environment.NewLine, StringComparison.InvariantCulture));
            Assert.IsFalse(
                embed.Contains(
                    GameState.ScoresListLimit.ToString(CultureInfo.InvariantCulture),
                    StringComparison.InvariantCulture),
                $"When the number of scorers matches the limit, the embed should not contain the scores list limit. Embed: {embed}");

            await this.Game.AddPlayer(lastId, $"Player {lastId}");
            this.Game.ScorePlayer(-5);

            this.MessageStore.Clear();

            await this.Handler.GetScoreAsync();
            Assert.AreEqual(
                embedCount, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of embeds sent after second GetScore.");
            embed = this.MessageStore.ChannelEmbeds.Last();

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

            this.InitializeHandler();
            this.Game.ReaderId = 0;

            // We want to go to the point where the number of players equals the limit, where we still show the
            // original title
            for (ulong i = 1; i < lastId; i++)
            {
                await this.Game.AddPlayer(i, $"User_{i}");
                this.Game.ScorePlayer(10);
            }

            await this.Handler.GetScoreAsync();
            int embedCount = (GameState.ScoresListLimit + 1) / EmbedBuilder.MaxFieldCount;
            Assert.AreEqual(
                embedCount,
                this.MessageStore.ChannelEmbeds.Count,
                "Unexpected number of embeds sent after second GetScore.");

            // The number of partitions should be one more than the number of times the delimiter appears (e.g. a;b is
            // split into a and b, but there is one ;)
            int nicknameFields = this.MessageStore.ChannelEmbeds.Sum(embed => embed.Split("User\\_").Length - 1);
            Assert.AreEqual(
                GameState.ScoresListLimit,
                nicknameFields,
                $"Number of scorers shown is not the same as the scoring limit.");
        }

        [TestMethod]
        public async Task MedalsAppearForTopThree()
        {
            const int playersCount = 5;
            int firstId = (int)GetNonexistentUserId();

            int[] players = Enumerable.Range(firstId, playersCount).ToArray();
            this.InitializeHandler();
            this.Game.ReaderId = 0;

            for (int i = 0; i < players.Length; i++)
            {
                ulong player = (ulong)players[i];
                string playerName = $"Player {player}";
                for (int j = 0; j <= i; j++)
                {
                    await this.Game.AddPlayer(player, playerName);
                    this.Game.ScorePlayer(10);
                }

                await this.Handler.GetScoreAsync();
                Assert.AreEqual(
                    1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds sent after {i} players");
                string embed = this.MessageStore.ChannelEmbeds.First();
                this.MessageStore.Clear();

                int medalIndex = 0;
                foreach (string medal in GeneralCommandHandler.Medals.Take(i + 1))
                {
                    Assert.IsTrue(
                        embed.Contains(medal, StringComparison.OrdinalIgnoreCase),
                        $"Could not find {medal} after scoring {i} players. Messge:\n{embed}");

                    string playerWithMedal = $"{medal} Player {i - medalIndex + firstId}";
                    Assert.IsTrue(
                        embed.Contains(playerWithMedal, StringComparison.OrdinalIgnoreCase),
                        $"Could not find \"{playerWithMedal}\" after scoring {i} players. Messge:\n{embed}");
                    medalIndex++;
                }

                foreach (string medal in GeneralCommandHandler.Medals.Skip(i + 1))
                {
                    Assert.IsFalse(
                        embed.Contains(medal, StringComparison.OrdinalIgnoreCase),
                        $"Found {medal} after scoring {i} players, which shouldn't have happened. Messge:\n{embed}");
                }
            }
        }

        [TestMethod]
        public async Task MedalsSameForTiedPlayers()
        {
            const int playersCount = 5;
            int firstId = (int)GetNonexistentUserId();

            ulong[] players = Enumerable.Range(firstId, playersCount).Select(id => (ulong)id).ToArray();
            this.InitializeHandler();
            this.Game.ReaderId = 0;

            // All tied for 1st
            for (int i = 0; i < 3; i++)
            {
                await this.Game.AddPlayer(players[i], $"Player {i}");
                this.Game.ScorePlayer(10);
            }

            await this.Game.AddPlayer(players[3], $"Player 3");
            this.Game.ScorePlayer(0);

            await this.Handler.GetScoreAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds after the first round of scoring");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.AreEqual(
                4,
                embed.Split(GeneralCommandHandler.Medals[0]).Length,
                $"There weren't three 1st place medals in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[1], StringComparison.OrdinalIgnoreCase),
                $"2nd place medal appeared when it shouldn't have in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[2], StringComparison.OrdinalIgnoreCase),
                $"3rd place medal appeared when it shouldn't have in\n{embed}");

            // One tied for 1st, two tied for 2nd
            await this.Game.AddPlayer(players[0], "Player 0");
            this.Game.ScorePlayer(10);
            await this.Handler.GetScoreAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelEmbeds.Count,
                $"Unexpected number of embeds sent after the second round of scoring");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();

            Assert.AreEqual(
                2,
                embed.Split(GeneralCommandHandler.Medals[0]).Length,
                $"There wasn't 1 1st place medals after the 2nd round of scoring in\n{embed}");
            Assert.AreEqual(
                3,
                embed.Split(GeneralCommandHandler.Medals[1]).Length,
                $"There weren't two 2nd place medals after the 2nd round of scoring in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[2], StringComparison.OrdinalIgnoreCase),
                $"3rd place medal appeared when it shouldn't have after the 2nd round of scoring in\n{embed}");

            // Two tied for 1st, one for third
            await this.Game.AddPlayer(players[1], "Player 1");
            this.Game.ScorePlayer(10);
            await this.Handler.GetScoreAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelEmbeds.Count,
                $"Unexpected number of embeds sent after the third round of scoring");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();

            Assert.AreEqual(
                3,
                embed.Split(GeneralCommandHandler.Medals[0]).Length,
                $"There weren't 2 1st place medals after the 3rd round of scoring in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[1], StringComparison.OrdinalIgnoreCase),
                $"2nd place medal appeared when it shouldn't have after the 3rd round of scoring in\n{embed}");
            Assert.AreEqual(
                2,
                embed.Split(GeneralCommandHandler.Medals[2]).Length,
                $"There wasn't 1 3rd place medals after the 3rd round of scoring in\n{embed}");

            // Two tied for 1st, two for third
            await this.Game.AddPlayer(players[3], "Player 3");
            this.Game.ScorePlayer(10);
            await this.Handler.GetScoreAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelEmbeds.Count,
                $"Unexpected number of embeds sent after the fourth round of scoring");
            embed = this.MessageStore.ChannelEmbeds.First();

            Assert.AreEqual(
                3,
                embed.Split(GeneralCommandHandler.Medals[0]).Length,
                $"There weren't 2 1st place medals after the 4th round of scoring in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[1], StringComparison.OrdinalIgnoreCase),
                $"2nd place medal appeared when it shouldn't have after the 4th round of scoring in\n{embed}");
            Assert.AreEqual(
                3,
                embed.Split(GeneralCommandHandler.Medals[2]).Length,
                $"There weren't 2 3rd place medals after the 4th round of scoring in\n{embed}");
        }

        [TestMethod]
        public async Task MedalsUseBonusScores()
        {
            const int playersCount = 5;
            int firstId = (int)GetNonexistentUserId();

            ulong[] players = Enumerable.Range(firstId, playersCount).Select(id => (ulong)id).ToArray();
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            this.Game.Format = Format.TossupBonusesShootout;

            // All tied for 1st
            for (int i = 0; i < 3; i++)
            {
                await this.Game.AddPlayer(players[i], $"Player {i}");
                this.Game.ScorePlayer(10);
                Assert.IsTrue(this.Game.TryScoreBonus("30"), $"Couldn't score the bonus for player {i}");
            }

            // This player should have a higher individual score, but a lower bonus one
            await this.Game.AddPlayer(players[3], $"Player 3");
            this.Game.ScorePlayer(15);
            Assert.IsTrue(this.Game.TryScoreBonus("0"), "Couldn't score the bonus for player 3");

            await this.Handler.GetScoreAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds after the first round of scoring");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.AreEqual(
                4,
                embed.Split(GeneralCommandHandler.Medals[0]).Length,
                $"There weren't three 1st place medals in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[1], StringComparison.OrdinalIgnoreCase),
                $"2nd place medal appeared when it shouldn't have in\n{embed}");
            Assert.IsFalse(
                embed.Contains(GeneralCommandHandler.Medals[2], StringComparison.OrdinalIgnoreCase),
                $"3rd place medal appeared when it shouldn't have in\n{embed}");
            Assert.IsFalse(
                embed.Contains("🥇 Player 3", StringComparison.InvariantCultureIgnoreCase),
                "Player 3 shouldn't have a medal, since their total score is lower than the others");
        }

        [TestMethod]
        public async Task GetGameReportWithNoRounds()
        {
            this.InitializeHandler();
            this.Game.ReaderId = DefaultReaderId;
            await this.Handler.GetGameReportAsync();

            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("No questions read", StringComparison.OrdinalIgnoreCase),
                "No questions message not found");
        }

        [TestMethod]
        public async Task GetGameReportSkipLastRoundWithNoBuzzes()
        {
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            await this.Game.AddPlayer(1, "Player");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();

            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("Question 1", StringComparison.OrdinalIgnoreCase), "Question 1 report wasn't found");
            Assert.IsFalse(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase), "Question 2 was found, but shouldn't be");
        }

        [TestMethod]
        public async Task GetGameReportQuestionWentDead()
        {
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            await this.Game.AddPlayer(1, "Player");
            this.Game.ScorePlayer(0);
            this.Game.NextQuestion();

            await this.Handler.GetGameReportAsync();

            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("Question went dead", StringComparison.OrdinalIgnoreCase),
                "Question 1 wasn't reported as dead");
        }

        // TODO: See if breaking this up into 5 different tests is preferable. We lose some coverage around the leaders
        // changing, but the unit is much smaller
        [TestMethod]
        public async Task GetGameReportAllBuzzesReported()
        {
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            await this.Game.AddPlayer(1, "Player 1");
            this.Game.ScorePlayer(-5);
            await this.Game.AddPlayer(2, "Player 2");
            this.Game.ScorePlayer(0);
            await this.Game.AddPlayer(3, "Player 3");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("Question 1", StringComparison.OrdinalIgnoreCase), "Question 1 report wasn't found");

            Regex playerOneReport = new Regex("Negged by .*Player 1.*(0/1)");
            Assert.IsTrue(playerOneReport.IsMatch(embed), $"Couldn't find player 1's neg in\n{embed}");
            Regex playerTwoReport = new Regex("Incorrectly answered by .*Player 2.*(0/0)");
            Assert.IsTrue(playerTwoReport.IsMatch(embed), $"Couldn't find player 2's incorrect answer in\n{embed}");
            Regex playerThreeReport = new Regex("Correctly answered by .*Player 3.*(1/0)");
            Assert.IsTrue(playerThreeReport.IsMatch(embed), $"Couldn't find player 3's correct answer in\n{embed}");
            Regex playerThreeLead = new Regex(".*Player 3.* is in the lead.");
            Assert.IsTrue(playerThreeLead.IsMatch(embed), $"Couldn't find correct player 3 as the leader in\n{embed}");

            await this.Game.AddPlayer(4, "Player 4");
            this.Game.ScorePlayer(15);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("Question 1", StringComparison.OrdinalIgnoreCase),
                "Question 1 report wasn't found in the second call");
            Assert.IsTrue(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase), "Question 2 report wasn't found");
            Regex playerFourReport = new Regex("Powered by .*Player 4.*(1/0/0)");
            Assert.IsTrue(playerFourReport.IsMatch(embed), $"Couldn't find player 4's power in\n{embed}");
            Regex playerFourLead = new Regex(".*Player 4.* is in the lead.");
            Assert.IsTrue(playerFourLead.IsMatch(embed), $"Couldn't find correct player 4 as the leader in\n{embed}");

            await this.Game.AddPlayer(5, "Player 5");
            this.Game.ScorePlayer(20);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase),
                "Question 2 report wasn't found in the third call");
            Assert.IsTrue(
                embed.Contains("Question 3", StringComparison.OrdinalIgnoreCase), "Question 3 report wasn't found");
            Regex playerFiveReport = new Regex("Superpowered by .*Player 5.*(1/0/0/0)");
            Assert.IsTrue(playerFiveReport.IsMatch(embed), $"Couldn't find player 5's superpower in\n{embed}");
            Regex playerFiveLead = new Regex(".*Player 5.* is in the lead.");
            Assert.IsTrue(playerFiveLead.IsMatch(embed), $"Couldn't find correct player 5 as the leader in\n{embed}");
        }

        [TestMethod]
        public async Task GetGameReportMultipleLeaders()
        {
            this.InitializeHandler();
            this.Game.ReaderId = 0;
            await this.Game.AddPlayer(1, "Player 1");
            this.Game.ScorePlayer(10);
            await this.Game.AddPlayer(2, "Player 2");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase), "Question 3 report wasn't found");
            Regex leadReport = new Regex(".*Player 1.*,.*Player 2.* are in the lead.");
            Assert.IsTrue(leadReport.IsMatch(embed), $"Couldn't find the expected lead report in\n{embed}");
        }

        [TestMethod]
        public async Task GetGameReportMultipleLeadersWithOthers()
        {
            this.InitializeHandler();
            this.Game.ReaderId = DefaultReaderId;
            for (int i = 0; i < GeneralCommandHandler.MaxLeadersShown; i++)
            {
                ulong number = (ulong)i;
                await this.Game.AddPlayer(number, $"Player {number}");
                this.Game.ScorePlayer(10);
            }

            // No "other" at the limit
            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsFalse(
                embed.Contains("other are in the lead", StringComparison.OrdinalIgnoreCase),
                $"\"other\" was found unexpectedly in\n{embed}");

            // Once we are past the limit, show other
            ulong otherLimit = GeneralCommandHandler.MaxLeadersShown;
            await this.Game.AddPlayer(otherLimit, $"Player {otherLimit}");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("other are in the lead", StringComparison.OrdinalIgnoreCase),
                $"1 \"other\" wasn't found in\n {embed}");

            // Once we're two players past the limit, show "others", since there's a count
            ulong lastNumber = otherLimit + 1;
            await this.Game.AddPlayer(lastNumber, $"Player {lastNumber}");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("others are in the lead", StringComparison.OrdinalIgnoreCase),
                $"2 \"others\" wasn't found in {embed}");
        }

        [TestMethod]
        public async Task GetGameReportWithTeamsByCommandMultipleLeadersWithOthers()
        {
            await this.SetDefaultTeamRolePrefix();

            this.InitializeHandler(DefaultIds, managerType: TeamManagerType.ByCommand);
            this.Game.ReaderId = DefaultReaderId;
            ByCommandTeamManager teamManager = this.Game.TeamManager as ByCommandTeamManager;
            Assert.IsTrue(teamManager.TryAddTeam("Alpha", out _), "Couldn't add team");
            teamManager.TryAddPlayerToTeam(0, "Player 0", "Alpha");

            for (int i = 0; i < GeneralCommandHandler.MaxLeadersShown; i++)
            {
                ulong number = (ulong)i;
                await this.Game.AddPlayer(number, $"Player {number}");
                this.Game.ScorePlayer(10);
            }

            // No "..." at the limit
            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsFalse(
                embed.Contains("...", StringComparison.OrdinalIgnoreCase),
                $"\"...\" was found unexpectedly in\n{embed}");

            // Once we are past the limit, show "..."
            ulong otherLimit = GeneralCommandHandler.MaxLeadersShown;
            await this.Game.AddPlayer(otherLimit, $"Player {otherLimit}");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("...", StringComparison.OrdinalIgnoreCase),
                $"\"...\" wasn't found in\n {embed}");
        }

        [TestMethod]
        public async Task GetGameReportWithTeamsByRoleMultipleLeadersWithOthers()
        {
            await this.SetDefaultTeamRolePrefix();

            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return new ulong[] { FirstTeamRoleId + mockUser.Object.Id };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;

            for (int i = 0; i < GeneralCommandHandler.MaxLeadersShown; i++)
            {
                ulong number = (ulong)i;
                await this.Game.AddPlayer(number, $"Player {number}");
                this.Game.ScorePlayer(10);
            }

            // No "..." at the limit
            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsFalse(
                embed.Contains("...", StringComparison.OrdinalIgnoreCase),
                $"\"...\" was found unexpectedly in\n{embed}");

            // Once we are past the limit, show "..."
            ulong otherLimit = GeneralCommandHandler.MaxLeadersShown;
            await this.Game.AddPlayer(otherLimit, $"Player {otherLimit}");
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();
            Assert.IsTrue(
                embed.Contains("...", StringComparison.OrdinalIgnoreCase),
                $"\"...\" wasn't found in\n {embed}");
        }

        [TestMethod]
        public async Task GameReportWithTeamsLeadersSortedByScore()
        {
            await this.SetDefaultTeamRolePrefix();

            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return new ulong[]
                            {
                                mockUser.Object.Id == 2 ? SecondTeamRoleId : FirstTeamRoleId
                            };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;
            await this.Game.AddPlayer(1, "Player1");
            this.Game.ScorePlayer(10);
            await this.Game.AddPlayer(2, "Player2");
            this.Game.ScorePlayer(20);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();

            Assert.IsTrue(embed.Contains(
                $"**{SecondTeamName}** 20, **{FirstTeamName}** 10", StringComparison.InvariantCultureIgnoreCase),
                $"Sorted score wasn't found in\n{embed}");

            await this.Game.AddPlayer(3, "Player3");
            this.Game.ScorePlayer(15);
            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds after the 4th question");
            embed = this.MessageStore.ChannelEmbeds.First();

            Assert.IsTrue(embed.Contains(
                $"**{FirstTeamName}** 25, **{SecondTeamName}** 20", StringComparison.InvariantCultureIgnoreCase),
                $"Sorted score wasn't found after the 4th question i\n{embed}");
        }

        [TestMethod]
        public async Task GameReportWithMoreThanTwoTeams()
        {
            const int firstScore = 10;
            const int secondScore = 15;
            const int thirdScore = 20;
            await this.SetDefaultTeamRolePrefix();

            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return mockUser.Object.Id switch
                            {
                                1 => new ulong[] { FirstTeamRoleId },
                                2 => new ulong[] { SecondTeamRoleId },
                                _ => new ulong[] { ThirdTeamRoleId },
                            };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;
            await this.Game.AddPlayer(1, "Player1");
            this.Game.ScorePlayer(firstScore);
            await this.Game.AddPlayer(2, "Player2");
            this.Game.ScorePlayer(secondScore);
            await this.Game.AddPlayer(3, "Player3");
            this.Game.ScorePlayer(thirdScore);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains($"**{ThirdTeamName}** {thirdScore}, **{SecondTeamName}** {secondScore}, " +
                    $"**{FirstTeamName}** {firstScore}", StringComparison.InvariantCultureIgnoreCase),
                $"Three teams weren't found in\n{embed}");
        }

        [TestMethod]
        public async Task GameReportWithTeamsIncludesPlayers()
        {
            await this.SetDefaultTeamRolePrefix();

            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return new ulong[]
                            {
                                mockUser.Object.Id == 1 ?FirstTeamRoleId : SecondTeamRoleId
                            };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;
            await this.Game.AddPlayer(1, "Player1");
            this.Game.ScorePlayer(10);
            await this.Game.AddPlayer(1, "Player1");
            this.Game.ScorePlayer(-5);
            await this.Game.AddPlayer(2, "Player2");
            this.Game.ScorePlayer(15);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();

            Assert.IsTrue(
                embed.Contains(
                    "**Question 1**: > Correctly answered by **Player1** (Alpha) (0/1/0)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find first player's buzz in embed\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    "**Question 2**: > Negged by **Player1** (Alpha) (0/1/1)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find first player's neg in embed\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    "> Powered by **Player2** (Beta) (1/0/0)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find second player's power in embed\n{embed}");

            // Make sure superpowers and no penalties appear too
            await this.Game.AddPlayer(1, "Player1");
            this.Game.ScorePlayer(20);
            await this.Game.AddPlayer(2, "Player2");
            this.Game.ScorePlayer(0);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = this.MessageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains(
                    "**Question 3**: > Superpowered by **Player1** (Alpha) (1/0/1/1)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find first player's superpower in embed\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    "**Question 4**: > Incorrectly answered by **Player2** (Beta) (0/1/0/0)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find second player's no penalty buzz in embed\n{embed}");
        }

        [TestMethod]
        public async Task GameReportSamePlayerDifferentTeams()
        {
            const string playerName = "Player1";
            await this.SetDefaultTeamRolePrefix();

            int scoreCounter = 0;
            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return scoreCounter switch
                            {
                                0 => Array.Empty<ulong>(),
                                1 => new ulong[] { FirstTeamRoleId },
                                _ => new ulong[] { SecondTeamRoleId },
                            };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(10);
            scoreCounter++;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(15);
            scoreCounter++;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(-5);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();

            Assert.IsTrue(embed.Contains(
                $"**{FirstTeamName}** 15, **{playerName}** 10, **{SecondTeamName}** -5",
                StringComparison.InvariantCultureIgnoreCase),
                $"Score wasn't found in\n{embed}");

            Assert.IsTrue(
                embed.Contains(
                    "**Player1** (0/1/0)", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find first player's buzz in embed\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    "**Player1** (Alpha) (1/0/0)", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find first player's neg in embed\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    "**Player1** (Beta) (0/0/1)", StringComparison.InvariantCultureIgnoreCase),
                $"Couldn't find second player's power in embed\n{embed}");
        }

        [TestMethod]
        public async Task GameReportIndividualInLead()
        {
            const string playerName = "Player1";
            await this.SetDefaultTeamRolePrefix();

            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    // No user is on a team, but the team prefix is set
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(Array.Empty<ulong>());
                });
            this.Game.ReaderId = DefaultReaderId;
            this.Game.TeamManager = new SoloOnlyTeamManager();

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(10);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();

            Assert.IsTrue(
                embed.Contains(
                    "**Player1** is in the lead", StringComparison.InvariantCultureIgnoreCase),
                $"Player1 wasn't mentioned in the leaders message in\n{embed}");
        }

        [TestMethod]
        public async Task GetScoreSamePlayerDifferentTeams()
        {
            const string playerName = "Player1";
            await this.SetDefaultTeamRolePrefix();

            int scoreCounter = 0;
            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return scoreCounter switch
                            {
                                0 => Array.Empty<ulong>(),
                                1 => new ulong[] { FirstTeamRoleId },
                                _ => new ulong[] { SecondTeamRoleId },
                            };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(10);
            scoreCounter++;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(15);
            scoreCounter++;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(-5);

            await this.Handler.GetScoreAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();
            this.MessageStore.Clear();

            Regex firstTeamLine = new Regex($"Alpha.*\\(15\\).*{playerName}.*15 \\(1/0/0\\)", RegexOptions.IgnoreCase);
            System.Text.RegularExpressions.Match firstTeamMatch = firstTeamLine.Match(embed);
            Assert.IsTrue(firstTeamMatch.Success, $"Couldn't find first team stats in embed\n{embed}");
            Regex individualLine = new Regex(
                $"{playerName}.*\\(10\\).*{playerName}.*10 \\(0/1/0\\)", RegexOptions.IgnoreCase);
            System.Text.RegularExpressions.Match individualMatch = individualLine.Match(embed);
            Assert.IsTrue(individualMatch.Success, $"Couldn't find first team stats in embed\n{embed}");
            Regex secondTeamLine = new Regex($"Beta.*\\(-5\\).*{playerName}.*-5 \\(0/0/1\\)", RegexOptions.IgnoreCase);
            System.Text.RegularExpressions.Match secondTeamMatch = secondTeamLine.Match(embed);
            Assert.IsTrue(secondTeamMatch.Success, $"Couldn't find first team stats in embed\n{embed}");

            // Verify the ordering
            Assert.IsTrue(
                firstTeamMatch.Index < individualMatch.Index,
                $"First team score wasn't before the individual score ({firstTeamMatch.Index} >= {individualMatch.Index})");
            Assert.IsTrue(
                individualMatch.Index < secondTeamMatch.Index,
                $"Individual score wasn't before the second team score ({individualMatch.Index} >= {secondTeamMatch.Index})");
        }

        [TestMethod]
        public async Task GameReportNoTeamsWithBonuses()
        {
            const string firstPlayerName = "Player1";
            const string secondPlayerName = "Player2";
            const string firstBonusSplit = "10/10/0";
            const string secondBonusSplit = "0/0/10";

            this.InitializeHandler();
            this.Game.ReaderId = DefaultReaderId;
            this.Game.TeamManager = new SoloOnlyTeamManager();
            this.Game.Format = Format.TossupBonusesShootout;

            await this.Game.AddPlayer(1, firstPlayerName);
            this.Game.ScorePlayer(10);
            this.Game.TryScoreBonus(firstBonusSplit);
            await this.Game.AddPlayer(2, secondPlayerName);
            this.Game.ScorePlayer(15);
            this.Game.TryScoreBonus(secondBonusSplit);

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();

            int firstSplitIndex = embed.IndexOf(
                $"Scored 20 on the bonus ({firstBonusSplit}).",
                StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(firstSplitIndex > 0, "First bonus score wasn't mentioned in the report in\n{embed}");
            int secondSplitIndex = embed.IndexOf(
                $"Scored 10 on the bonus ({secondBonusSplit}).",
                StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(secondSplitIndex > 0, "Second bonus score wasn't mentioned in the report in\n{embed}");
            Assert.IsTrue(
                secondSplitIndex > firstSplitIndex,
                $"Second split ({secondSplitIndex}) should appear before the first split ({firstSplitIndex}) in\n{embed}");

            // Also make sure that the tossup splits show up
            Assert.IsTrue(
                embed.Contains(
                    $"**Question 1**: > Correctly answered by **{firstPlayerName}** (0/1/0)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"First player buzz not in the report\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    $"**Question 2**: > Powered by **{secondPlayerName}** (1/0/0)",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Second player buzz not in the report\n{embed}");

            // Make sure the leader message includes the bonus score
            Assert.IsTrue(
                embed.Contains("**Player1** is in the lead.", StringComparison.InvariantCultureIgnoreCase),
                $"Player1 wasn't in the lead even though they had more points. Embed:\n{embed}");
        }

        [TestMethod]
        public async Task GameReportWithTeamsAndBonuses()
        {
            const string playerName = "Player1";
            const string firstBonusSplit = "10/10/0";
            const string secondBonusSplit = "0/10/0";

            await this.SetDefaultTeamRolePrefix();

            int scoreCounter = 0;
            this.InitializeHandlerWithTeamsByRole(
                (mockUser) =>
                {
                    mockUser
                        .Setup(user => user.RoleIds)
                        .Returns(() =>
                        {
                            return scoreCounter switch
                            {
                                0 => Array.Empty<ulong>(),
                                1 => new ulong[] { FirstTeamRoleId },
                                _ => new ulong[] { SecondTeamRoleId },
                            };
                        });
                });
            this.Game.ReaderId = DefaultReaderId;
            this.Game.Format = Format.TossupBonusesShootout;

            await this.Game.AddPlayer(1, playerName);
            this.Game.ScorePlayer(10);
            this.Game.TryScoreBonus(firstBonusSplit);
            scoreCounter++;

            await this.Game.AddPlayer(2, playerName);
            this.Game.ScorePlayer(15);
            this.Game.TryScoreBonus(secondBonusSplit);
            scoreCounter++;

            await this.Handler.GetGameReportAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = this.MessageStore.ChannelEmbeds.First();

            int firstSplitIndex = embed.IndexOf(
                $"Scored 20 on the bonus ({firstBonusSplit}).",
                StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(firstSplitIndex > 0, "First bonus score wasn't mentioned in the report in\n{embed}");
            int secondSplitIndex = embed.IndexOf(
                $"Scored 10 on the bonus ({secondBonusSplit}).",
                StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(secondSplitIndex > 0, "Second bonus score wasn't mentioned in the report in\n{embed}");
            Assert.IsTrue(
                secondSplitIndex > firstSplitIndex,
                $"Second split ({secondSplitIndex}) should appear before the first split ({firstSplitIndex}) in\n{embed}");
            Assert.IsTrue(
                embed.Contains(
                    $"Top scores: **{playerName}** 30, **{FirstTeamName}** 25",
                    StringComparison.InvariantCultureIgnoreCase),
                $"Top scores is incorrect in\n{embed}");
        }

        [TestMethod]
        public async Task JoinTeamSucceeds()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId, TeamManagerType.ByCommand);
            ByCommandTeamManager teamManager = this.Game.TeamManager as ByCommandTeamManager;
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeamName, out _), "Couldn't add team");

            Assert.IsNull(await teamManager.GetTeamIdOrNull(userId), "User shouldn't be on a team yet");
            await this.Handler.JoinTeamAsync(FirstTeamName);
            Assert.AreEqual(FirstTeamName, await teamManager.GetTeamIdOrNull(userId), "User didn't join the team");
            this.MessageStore.VerifyChannelMessages($@"@User_{userId} is on team ""{FirstTeamName}""");
        }

        [TestMethod]
        public async Task JoinTeamCaseInsensitiveSucceeds()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId, TeamManagerType.ByCommand);
            ByCommandTeamManager teamManager = this.Game.TeamManager as ByCommandTeamManager;
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeamName, out _), "Couldn't add team");

            Assert.IsNull(await teamManager.GetTeamIdOrNull(userId), "User shouldn't be on a team yet");
            string upperFirstTeamName = FirstTeamName.ToUpper(CultureInfo.InvariantCulture);
            await this.Handler.JoinTeamAsync(upperFirstTeamName);
            Assert.AreEqual(FirstTeamName, await teamManager.GetTeamIdOrNull(userId), "User didn't join the team");
            this.MessageStore.VerifyChannelMessages($@"@User_{userId} is on team ""{FirstTeamName}""");
        }

        [TestMethod]
        public async Task JoinNonexistentTeamFails()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId, TeamManagerType.ByCommand);

            await this.Handler.JoinTeamAsync(SecondTeamName);
            Assert.IsNull(await this.Game.TeamManager.GetTeamIdOrNull(userId), "User shouldn't be on a team");
            this.MessageStore.VerifyChannelMessages(
                $@"Couldn't join team ""{SecondTeamName}"". Make sure it is not misspelled.");
        }

        [TestMethod]
        public async Task JoinTeamWithByRoleTeamManagerFails()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId);
            Mock<ITeamManager> mockTeamManager = new Mock<ITeamManager>();
            this.Game.TeamManager = mockTeamManager.Object;

            await this.Handler.JoinTeamAsync(FirstTeamName);

            mockTeamManager.VerifyNoOtherCalls();
            this.MessageStore.VerifyChannelMessages("Joining teams isn't supported in this mode.");
        }

        [TestMethod]
        public async Task LeaveTeamSucceeds()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId, TeamManagerType.ByCommand);
            ByCommandTeamManager teamManager = this.Game.TeamManager as ByCommandTeamManager;
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeamName, out _), "Couldn't add team");
            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(userId, $"User_{userId}", FirstTeamName), "Couldn't add player");
            Assert.AreEqual(
                FirstTeamName,
                await teamManager.GetTeamIdOrNull(userId),
                "User isn't on the right team before joining");

            await this.Handler.LeaveTeamAsync();
            Assert.IsNull(await teamManager.GetTeamIdOrNull(userId), "User didn't leave a team");
            this.MessageStore.VerifyChannelMessages($@"""User_{userId}"" left their team.");
        }

        [TestMethod]
        public async Task LeaveTeamFailsWhenNotOnATeam()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId, TeamManagerType.ByCommand);
            ByCommandTeamManager teamManager = this.Game.TeamManager as ByCommandTeamManager;
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeamName, out _), "Couldn't add team");

            await this.Handler.LeaveTeamAsync();
            Assert.IsNull(await teamManager.GetTeamIdOrNull(userId), "User should not be on a team");
            this.MessageStore.VerifyChannelMessages($@"""User_{userId}"" isn't on a team.");
        }

        [TestMethod]
        public async Task LeaveTeamWithByRoleTeamManagerFails()
        {
            const ulong userId = 1;
            this.InitializeHandler(DefaultIds, userId);
            Mock<ITeamManager> mockTeamManager = new Mock<ITeamManager>();
            this.Game.TeamManager = mockTeamManager.Object;

            await this.Handler.LeaveTeamAsync();

            mockTeamManager.VerifyNoOtherCalls();
            this.MessageStore.VerifyChannelMessages("Leaving teams isn't supported in this mode.");
        }

        [TestMethod]
        public async Task GetTeamsSucceeds()
        {
            this.InitializeHandler();
            Mock<ITeamManager> mockTeamManager = new Mock<ITeamManager>();

            IReadOnlyDictionary<string, string> teamIdToName = new Dictionary<string, string>()
            {
                { "1", FirstTeamName },
                { "2", SecondTeamName },
                { "3", ThirdTeamName }
            };
            mockTeamManager.Setup(manager => manager.GetTeamIdToNames()).Returns(Task.FromResult(teamIdToName));
            this.Game.TeamManager = mockTeamManager.Object;

            await this.Handler.GetTeamsAsync();
            this.MessageStore.VerifyChannelMessages($"Teams: {FirstTeamName}, {SecondTeamName}, {ThirdTeamName}");
        }

        [TestMethod]
        public async Task GetTeamsSucceedsWithManyTeams()
        {
            this.InitializeHandler();
            Mock<ITeamManager> mockTeamManager = new Mock<ITeamManager>();

            Dictionary<string, string> teamIdToName = new Dictionary<string, string>();
            for (int i = 0; i < GeneralCommandHandler.MaxTeamsShown; i++)
            {
                string id = i.ToString(CultureInfo.InvariantCulture);
                string teamName = $"Team {string.Empty.PadLeft(i, 'A')}";
                teamIdToName[id] = teamName;
            }

            string lastTeam = $"Team {string.Empty.PadLeft(GeneralCommandHandler.MaxTeamsShown - 1, 'A')}";

            mockTeamManager
                .Setup(manager => manager.GetTeamIdToNames())
                .Returns(Task.FromResult((IReadOnlyDictionary<string, string>)teamIdToName));
            this.Game.TeamManager = mockTeamManager.Object;

            await this.Handler.GetTeamsAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            string message = this.MessageStore.ChannelMessages.First();
            this.MessageStore.Clear();

            Assert.IsFalse(
                message.Contains("other", StringComparison.InvariantCultureIgnoreCase),
                $@"Message contains ""other"", but it's not at the limit. Message\n{message}");
            Assert.IsTrue(
                 message.EndsWith(lastTeam, StringComparison.InvariantCultureIgnoreCase),
                 $"Last team isn't found in the message\n{message}");

            string overTheLimitId = GeneralCommandHandler.MaxTeamsShown.ToString(CultureInfo.InvariantCulture);
            string overTheLimitTeamName = $"Team {string.Empty.PadLeft(GeneralCommandHandler.MaxTeamsShown, 'A')}";
            teamIdToName[overTheLimitId] = overTheLimitTeamName;
            await this.Handler.GetTeamsAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            message = this.MessageStore.ChannelMessages.First();
            this.MessageStore.Clear();

            Assert.IsTrue(
                message.Contains("other", StringComparison.InvariantCultureIgnoreCase),
                $@"""other"" is not in message\n{message}");
            Assert.IsTrue(
                 message.Contains(lastTeam, StringComparison.InvariantCultureIgnoreCase),
                 $"Last team isn't found in the second message\n{message}");
            Assert.IsFalse(
                message.Contains(overTheLimitTeamName, StringComparison.InvariantCultureIgnoreCase),
                $"Team over the limit was found in the message\n{message}");

            teamIdToName["another team"] = "another team";
            await this.Handler.GetTeamsAsync();
            Assert.AreEqual(1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages");
            message = this.MessageStore.ChannelMessages.First();
            Assert.IsTrue(
                message.Contains("others", StringComparison.InvariantCultureIgnoreCase),
                $@"""others"" is not in message\n{message}");
        }

        [TestMethod]
        public async Task GetTeamsMessageWhenNoTeamsExist()
        {
            const string joinTeamDescription = "The join team description";
            this.InitializeHandler();
            Mock<ITeamManager> mockTeamManager = new Mock<ITeamManager>();

            IReadOnlyDictionary<string, string> teamIdToName = new Dictionary<string, string>(0);
            mockTeamManager.Setup(manager => manager.GetTeamIdToNames()).Returns(Task.FromResult(teamIdToName));
            mockTeamManager.Setup(manager => manager.JoinTeamDescription).Returns(joinTeamDescription);
            this.Game.TeamManager = mockTeamManager.Object;

            await this.Handler.GetTeamsAsync();
            this.MessageStore.VerifyChannelMessages(joinTeamDescription);
        }

        private static ulong GetExistingNonReaderUserId(ulong readerId = DefaultReaderId)
        {
            return DefaultIds.Except(new ulong[] { readerId }).First();
        }

        private static ulong GetNonexistentUserId()
        {
            return DefaultIds.Max() + 1;
        }

        private void InitializeHandler()
        {
            this.InitializeHandler(DefaultIds);
        }

        private void InitializeHandlerWithTeamsByRole(Action<Mock<IGuildUser>> updateUser)
        {
            this.InitializeHandler(
                DefaultIds,
                DefaultReaderId,
                TeamManagerType.ByRole,
                updateUser);
        }

        private void InitializeHandler(
            HashSet<ulong> existingIds,
            ulong userId = DefaultReaderId,
            TeamManagerType managerType = TeamManagerType.Solo)
        {
            this.InitializeHandler(existingIds, userId, managerType, (mockUser) => { });
        }

        private void InitializeHandler(
            HashSet<ulong> existingIds,
            ulong userId,
            TeamManagerType teamManagerType,
            Action<Mock<IGuildUser>> updateUser)
        {
            this.MessageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                this.MessageStore,
                existingIds,
                DefaultGuildId,
                DefaultChannelId,
                userId: userId,
                updateMockGuild: (mockGuild, textChannel) =>
                {
                    Mock<IRole> mockRole = new Mock<IRole>();
                    mockRole.Setup(role => role.Id).Returns(FirstTeamRoleId);
                    mockRole.Setup(role => role.Name).Returns($"Team {FirstTeamName}");
                    Mock<IRole> mockRole2 = new Mock<IRole>();
                    mockRole2.Setup(role => role.Id).Returns(SecondTeamRoleId);
                    mockRole2.Setup(role => role.Name).Returns($"Team {SecondTeamName}");
                    Mock<IRole> mockRole3 = new Mock<IRole>();
                    mockRole3.Setup(role => role.Id).Returns(ThirdTeamRoleId);
                    mockRole3.Setup(role => role.Name).Returns($"Team {ThirdTeamName}");
                    Mock<IRole> mockReaderRole = new Mock<IRole>();
                    mockReaderRole.Setup(role => role.Id).Returns(DefaultReaderRoleId);
                    mockReaderRole.Setup(role => role.Name).Returns(DefaultReaderRoleName);

                    IRole[] roles = new IRole[]
                    {
                        mockRole.Object, mockRole2.Object, mockRole3.Object, mockReaderRole.Object
                    };
                    mockGuild.Setup(guild => guild.Roles).Returns(roles);

                    mockGuild
                        .Setup(guild => guild.GetUserAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                        .Returns<ulong, CacheMode, RequestOptions>((id, cacheMode, requestOptions) =>
                        {
                            if (existingIds?.Contains(id) == true)
                            {
                                return Task.FromResult(CommandMocks.CreateGuildUser(id, updateUser));
                            }

                            return Task.FromResult<IGuildUser>(null);
                        });
                    mockGuild
                        .Setup(guild => guild.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                        .Returns<CacheMode, RequestOptions>((cacheMode, requestOptions) =>
                        {
                            IReadOnlyCollection<IGuildUser> users = existingIds
                                .Select(id => CommandMocks.CreateGuildUser(id, updateUser))
                                .ToList();
                            return Task.FromResult(users);
                        });
                },
                out _);
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.BotConfigurationfactory);
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            GameStateManager manager = new GameStateManager();
            manager.TryCreate(DefaultChannelId, out GameState game);

            // Use the By Role team manager by default
            // game.TeamManager = new ByRoleTeamManager(commandContext.Guild, "Team ");
            // TODO: Need to update the Mock User to specify RoleIds
            // And this needs to come from GetUserId
            ITeamManager teamManager = null;
            teamManager = teamManagerType switch
            {
                TeamManagerType.ByCommand => new ByCommandTeamManager(),
                TeamManagerType.ByRole => new ByRoleTeamManager(commandContext.Guild, "Team "),
                _ => SoloOnlyTeamManager.Instance,
            };
            game.TeamManager = teamManager;

            this.Game = game;
            this.Handler = new GeneralCommandHandler(commandContext, manager, options, dbActionFactory);
        }

        private Task SetDefaultTeamRolePrefix()
        {
            using (BotConfigurationContext context = this.BotConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                return action.SetTeamRolePrefixAsync(DefaultGuildId, "Team ");
            }
        }

        private enum TeamManagerType
        {
            Solo,
            ByRole,
            ByCommand
        }
    }
}
