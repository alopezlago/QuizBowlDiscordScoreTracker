using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
            int nicknameFields = messageStore.ChannelEmbeds.Sum(embed => embed.Split("User\\_").Length - 1);
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
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;
            for (int i = 0; i < players.Length; i++)
            {
                ulong player = (ulong)players[i];
                string playerName = $"Player {player}";
                for (int j = 0; j <= i; j++)
                {
                    game.AddPlayer(player, playerName);
                    game.ScorePlayer(10);
                }

                await handler.GetScoreAsync();
                Assert.AreEqual(
                    1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds sent after {i} players");
                string embed = messageStore.ChannelEmbeds.First();
                messageStore.Clear();

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
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);

            game.ReaderId = 0;

            // All tied for 1st
            for (int i = 0; i < 3; i++)
            {
                game.AddPlayer(players[i], $"Player {i}");
                game.ScorePlayer(10);
            }

            game.AddPlayer(players[3], $"Player 3");
            game.ScorePlayer(0);

            await handler.GetScoreAsync();
            Assert.AreEqual(
                1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds after the first round of scoring");
            string embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
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
            game.AddPlayer(players[0], "Player 0");
            game.ScorePlayer(10);
            await handler.GetScoreAsync();
            Assert.AreEqual(
                1,
                messageStore.ChannelEmbeds.Count,
                $"Unexpected number of embeds sent after the second round of scoring");
            embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();

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
            game.AddPlayer(players[1], "Player 1");
            game.ScorePlayer(10);
            await handler.GetScoreAsync();
            Assert.AreEqual(
                1,
                messageStore.ChannelEmbeds.Count,
                $"Unexpected number of embeds sent after the third round of scoring");
            embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();

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
            game.AddPlayer(players[3], "Player 3");
            game.ScorePlayer(10);
            await handler.GetScoreAsync();
            Assert.AreEqual(
                1,
                messageStore.ChannelEmbeds.Count,
                $"Unexpected number of embeds sent after the fourth round of scoring");
            embed = messageStore.ChannelEmbeds.First();

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
        public async Task GetGameReportWithNoRounds()
        {
            this.CreateHandler(out GeneralCommandHandler handler, out _, out MessageStore messageStore);
            await handler.GetGameReportAsync();

            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("No questions read", StringComparison.OrdinalIgnoreCase),
                "No questions message not found");
        }

        [TestMethod]
        public async Task GetGameReportSkipLastRoundWithNoBuzzes()
        {
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);
            game.ReaderId = 0;
            game.AddPlayer(1, "Player");
            game.ScorePlayer(10);

            await handler.GetGameReportAsync();

            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("Question 1", StringComparison.OrdinalIgnoreCase), "Question 1 report wasn't found");
            Assert.IsFalse(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase), "Question 2 was found, but shouldn't be");
        }

        [TestMethod]
        public async Task GetGameReportQuestionWentDead()
        {
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);
            game.ReaderId = 0;
            game.AddPlayer(1, "Player");
            game.ScorePlayer(0);
            game.NextQuestion();

            await handler.GetGameReportAsync();

            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = messageStore.ChannelEmbeds.First();
            Assert.IsTrue(
                embed.Contains("Question went dead", StringComparison.OrdinalIgnoreCase),
                "Question 1 wasn't reported as dead");
        }

        // TODO: See if breaking this up into 5 different tests is preferable. We lose some coverage around the leaders
        // changing, but the unit is much smaller
        [TestMethod]
        public async Task GetGameReportAllBuzzesReported()
        {
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);
            game.ReaderId = 0;
            game.AddPlayer(1, "Player 1");
            game.ScorePlayer(-5);
            game.AddPlayer(2, "Player 2");
            game.ScorePlayer(0);
            game.AddPlayer(3, "Player 3");
            game.ScorePlayer(10);

            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
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

            game.AddPlayer(4, "Player 4");
            game.ScorePlayer(15);

            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
            Assert.IsTrue(
                embed.Contains("Question 1", StringComparison.OrdinalIgnoreCase),
                "Question 1 report wasn't found in the second call");
            Assert.IsTrue(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase), "Question 2 report wasn't found");
            Regex playerFourReport = new Regex("Powered by .*Player 4.*(1/0/0)");
            Assert.IsTrue(playerFourReport.IsMatch(embed), $"Couldn't find player 4's power in\n{embed}");
            Regex playerFourLead = new Regex(".*Player 4.* is in the lead.");
            Assert.IsTrue(playerFourLead.IsMatch(embed), $"Couldn't find correct player 4 as the leader in\n{embed}");

            game.AddPlayer(5, "Player 5");
            game.ScorePlayer(20);

            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = messageStore.ChannelEmbeds.First();
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
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);
            game.ReaderId = 0;
            game.AddPlayer(1, "Player 1");
            game.ScorePlayer(10);
            game.AddPlayer(2, "Player 2");
            game.ScorePlayer(10);

            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
            Assert.IsTrue(
                embed.Contains("Question 2", StringComparison.OrdinalIgnoreCase), "Question 3 report wasn't found");
            Regex leadReport = new Regex(".*Player 1.*,.*Player 2.* are in the lead.");
            Assert.IsTrue(leadReport.IsMatch(embed), $"Couldn't find the expected lead report in\n{embed}");
        }

        [TestMethod]
        public async Task GetGameReportMultipleLeadersWithOthers()
        {
            this.CreateHandler(
                out GeneralCommandHandler handler, out GameState game, out MessageStore messageStore);
            for (int i = 0; i < GeneralCommandHandler.MaxLeadersShown; i++)
            {
                ulong number = (ulong)i;
                game.AddPlayer(number, $"Player {number}");
                game.ScorePlayer(10);
            }

            // No "other" at the limit
            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            string embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
            Assert.IsFalse(
                embed.Contains("other are in the lead", StringComparison.OrdinalIgnoreCase),
                $"\"other\" was found unexpectedly in\n{embed}");

            // Once we are past the limit, show other
            ulong otherLimit = GeneralCommandHandler.MaxLeadersShown;
            game.AddPlayer(otherLimit, $"Player {otherLimit}");
            game.ScorePlayer(10);

            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
            Assert.IsTrue(
                embed.Contains("other are in the lead", StringComparison.OrdinalIgnoreCase),
                $"1 \"other\" wasn't found in\n {embed}");

            // Once we're two players past the limit, show "others", since there's a count
            ulong lastNumber = otherLimit + 1;
            game.AddPlayer(lastNumber, $"Player {lastNumber}");
            game.ScorePlayer(10);

            await handler.GetGameReportAsync();
            Assert.AreEqual(1, messageStore.ChannelEmbeds.Count, $"Unexpected number of embeds");
            embed = messageStore.ChannelEmbeds.First();
            messageStore.Clear();
            Assert.IsTrue(
                embed.Contains("others are in the lead", StringComparison.OrdinalIgnoreCase),
                $"2 \"others\" wasn't found in {embed}");
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
