using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class GameStateTests
    {
        [TestMethod]
        public void ReaderIdNotSetInConstructor()
        {
            // This isn't that useful of a test, but use it as a proof of concept.
            GameState gameState = new GameState();
            Assert.IsNull(gameState.ReaderId, "No reader should be assigned on initialization.");
        }

        [TestMethod]
        public void TryGetNextPlayerFalseWhenQueueIsEmpty()
        {
            GameState gameState = new GameState();
            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _),
                "There should be no next player if no one was added to the queue.");
        }

        [TestMethod]
        public async Task CannotAddReaderToQueue()
        {
            GameState gameState = new GameState
            {
                ReaderId = 123
            };
            Assert.IsFalse(
                await gameState.AddPlayer(gameState.ReaderId.Value, "Reader"),
                "Adding the reader to the queue should not be possible.");
        }

        [TestMethod]
        public void ReaderIdPersists()
        {
            const ulong readerId = 123;
            GameState gameState = new GameState
            {
                ReaderId = readerId
            };
            Assert.AreEqual(readerId, gameState.ReaderId, "Reader Id is not persisted.");
        }

        [TestMethod]
        public async Task CannotAddSamePlayerTwiceToQueue()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Adding the player the first time should succeed.");
            Assert.IsFalse(await gameState.AddPlayer(id, "Player"), "Adding the player the second time should fail.");
        }

        [TestMethod]
        public async Task FirstAddedPlayerIsTopOfQueue()
        {
            const ulong firstId = 1;
            const ulong secondId = 2;
            GameState gameState = new GameState();
            Assert.IsTrue(
                await gameState.AddPlayer(firstId, "Player1"), "Adding the player the first time should succeed.");
            Assert.IsTrue(
                await gameState.AddPlayer(secondId, "Player2"), "Adding the player the second time should succeed.");
            Assert.IsTrue(gameState.TryGetNextPlayer(out ulong nextPlayerId), "There should be a player in the queue.");
            Assert.AreEqual(firstId, nextPlayerId, "The player first in the queue should be the first one added.");
        }

        [TestMethod]
        public async Task PlayerOrderInQueue()
        {
            ulong[] ids = new ulong[] { 1, 2, 3, 4 };
            GameState gameState = new GameState();
            foreach (ulong id in ids)
            {
                Assert.IsTrue(
                    await gameState.AddPlayer(id, $"Player {id}"), $"Should be able to add {id} to the queue.");
            }

            foreach (ulong id in ids)
            {
                Assert.IsTrue(
                    gameState.TryGetNextPlayer(out ulong nextPlayerId),
                    $"Should be able to get a player from the queue (which should match ID {id}.");
                Assert.AreEqual(id, nextPlayerId, "Unexpected ID from the queue.");
                gameState.ScorePlayer(0);
            }

            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _), "No players should be left in the queue.");
        }

        [TestMethod]
        public async Task PlayerOnSameTeamSkippedInQueue()
        {
            ulong[] ids = new ulong[] { 1, 3, 2, 5, 4 };
            GameState gameState = CreateGameStateWithByCommandTeamManager(out ByCommandTeamManager teamManager);
            Assert.IsTrue(teamManager.TryAddTeam("100", out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam("101", out _), "Couldn't add the second team");

            foreach (ulong id in ids)
            {
                ulong teamId = 100 + (id % 2);
                teamManager.TryAddPlayerToTeam(id, $"User_{id}", teamId.ToString(CultureInfo.InvariantCulture));

                Assert.IsTrue(
                    await gameState.AddPlayer(id, $"Player {id}"), $"Should be able to add {id} to the queue.");
            }

            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                $"Should be able to get a player from the queue (which should be for the first team)");
            Assert.AreEqual(1u, nextPlayerId, "Unexpected ID from the queue.");
            gameState.ScorePlayer(0);
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out nextPlayerId),
                $"Should be able to get a player from the queue (which should be for the second team)");
            Assert.AreEqual(2u, nextPlayerId, "Unexpected ID from the queue after getting the 2nd player.");
            gameState.ScorePlayer(0);

            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _),
                "No players should be left in the queue, since they are on the same team.");
        }

        [TestMethod]
        public async Task CanWithdrawPlayerOnTopOfQueue()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();

            Assert.IsTrue(await gameState.AddPlayer(id, $"Player {id}"), "Adding the player should succeed.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "There should be a player in the queue.");
            Assert.AreEqual(id, nextPlayerId, "Id of the next player should be ours.");
            Assert.IsTrue(await gameState.WithdrawPlayer(id), "Withdrawing the same player should succeed.");
            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _),
                "There should be no player in the queue when they withdrew.");
        }

        [TestMethod]
        public async Task CanWithdrawPlayerInMiddleOfQueue()
        {
            const ulong firstId = 1;
            const ulong secondId = 22;
            const ulong thirdId = 333;
            GameState gameState = new GameState();

            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "Adding the first player should succeed.");
            Assert.IsTrue(await gameState.AddPlayer(secondId, "Player2"), "Adding the second player should succeed.");
            Assert.IsTrue(await gameState.AddPlayer(thirdId, "Player3"), "Adding the third player should succeed.");
            Assert.IsTrue(await gameState.WithdrawPlayer(secondId), "Withdrawing the second player should succeed.");
            Assert.IsTrue(await gameState.WithdrawPlayer(firstId), "Withdrawing the first player should succeed.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "There should be a player in the queue.");
            Assert.AreEqual(thirdId, nextPlayerId, "Id of the next player should be the third player's.");
        }

        [TestMethod]
        public async Task CannotWithdrawPlayerNotInQueue()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            await gameState.AddPlayer(id, "Player");
            Assert.IsFalse(await gameState.WithdrawPlayer(id + 1), "Should not be able to withdraw player who is not in the queue.");
        }

        [TestMethod]
        public async Task CannotWithdrawSamePlayerInQueueTwiceInARow()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Adding player should succeed.");
            Assert.IsTrue(await gameState.WithdrawPlayer(id), "First withdrawal should succeed.");
            Assert.IsFalse(await gameState.WithdrawPlayer(id), "Second withdrawal should fail.");
        }

        [TestMethod]
        public async Task CanWithdrawSamePlayerInQueueTwice()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "First add should succeed.");
            Assert.IsTrue(await gameState.WithdrawPlayer(id), "First withdrawal should succeed.");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Second add should succeed.");
            Assert.IsTrue(await gameState.WithdrawPlayer(id), "Second withdrawal should succeed.");
        }

        [TestMethod]
        public async Task ClearCurrentRoundClearsQueueAndKeepsReader()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId
            };

            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ClearCurrentRound();
            Assert.IsFalse(gameState.TryGetNextPlayer(out _), "Queue should have been cleared.");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed after clear.");
            Assert.AreEqual(readerId, gameState.ReaderId, "Reader should remain the same.");
        }

        [TestMethod]
        public async Task ClearAllClearsQueueAndReader()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId
            };

            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ClearAll();
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong _), "Queue should have been cleared.");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed after clear.");
            Assert.IsNull(gameState.ReaderId, "Reader should be cleared.");
        }

        [TestMethod]
        public async Task MixedTossupsAndBonusPhases()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId,
                Format = Format.CreateTossupBonusesShootout(false)
            };

            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected initial stage");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(10);
            Assert.AreEqual(PhaseStage.Bonus, gameState.CurrentStage, "Unexpected stage after scoring the player");
            Assert.IsTrue(gameState.TryScoreBonus("0"), "Should've been able to score the bonus");

            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected stage after scoring the bonus");
            Assert.AreEqual(2, gameState.PhaseNumber, "Unexpected phase number after scoring the bonus");
            gameState.Format = Format.CreateTossupShootout(false);
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Second add should succeed.");
            gameState.ScorePlayer(10);

            Assert.AreEqual(
                PhaseStage.Tossup, gameState.CurrentStage, "Unexpected stage after scoring the second tossup");
            Assert.AreEqual(3, gameState.PhaseNumber, "Unexpected phase number after scoring the second tossup");
        }

        [TestMethod]
        public async Task NextQuestionClearsQueueAndKeepsReader()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId
            };

            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(-5);
            gameState.NextQuestion();
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong _), "Queue should have been cleared.");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed after clear.");
            Assert.AreEqual(readerId, gameState.ReaderId, "Reader should remain the same.");
            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await gameState.GetLastScoringSplits();
            Assert.AreEqual(1, lastSplits.Count, "Unexpected number of scores.");

            KeyValuePair<PlayerTeamPair, LastScoringSplit> splitPair = lastSplits.First();
            Assert.AreEqual(id, splitPair.Key.PlayerId, "Unexpected ID for the score.");
            Assert.AreEqual(-5, splitPair.Value.Split.Points, "Unexpected point total for the score.");
        }

        [TestMethod]
        public async Task NextQuestionOnTossupPhaseSkipsBonus()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId,
                Format = Format.CreateTossupBonusesShootout(false)
            };

            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected initial stage");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(-5);
            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected stage after scoring the player");

            gameState.NextQuestion();
            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected stage after calling nextQuestion");
            IReadOnlyDictionary<string, BonusStats> bonusStats = await gameState.GetBonusStats();
            Assert.AreEqual(0, bonusStats.Count, "There should be no bonus stats");
        }

        [TestMethod]
        public async Task NextQuestionOnBonusZeroesBonus()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId,
                Format = Format.CreateTossupBonusesShootout(false)
            };

            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected initial stage");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(10);
            Assert.AreEqual(PhaseStage.Bonus, gameState.CurrentStage, "Unexpected stage after scoring the player");

            gameState.NextQuestion();
            Assert.AreEqual(PhaseStage.Tossup, gameState.CurrentStage, "Unexpected stage after calling nextQuestion");
            IReadOnlyDictionary<string, BonusStats> bonusStats = await gameState.GetBonusStats();
            Assert.AreEqual(1, bonusStats.Count, "There should be no bonus stats");

            BonusStats stats = bonusStats.Values.First();
            Assert.AreEqual(1, stats.Heard, "Unexpected number of heard bonuses");
            Assert.AreEqual(0, stats.Total, "Unexpected number of points for the bonus");
        }

        [TestMethod]
        public void NextAddsNoPhaseAtMaximumPhaseCount()
        {
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId
            };

            for (int i = 0; i < GameState.MaximumPhasesCount; i++)
            {
                gameState.NextQuestion();
                Assert.AreEqual(i + 2, gameState.PhaseNumber, "Phase number didn't increase");
            }

            gameState.NextQuestion();
            Assert.AreEqual(
                GameState.MaximumPhasesCount + 1, gameState.PhaseNumber, "Phase number should've remained the same");
        }

        [TestMethod]
        public async Task ScoringAddsNoPhaseAtMaximumPhaseCount()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState
            {
                ReaderId = readerId
            };

            for (int i = 0; i < GameState.MaximumPhasesCount; i++)
            {
                gameState.NextQuestion();
                Assert.AreEqual(i + 2, gameState.PhaseNumber, "Phase number didn't increase");
            }

            Assert.AreEqual(
                GameState.MaximumPhasesCount + 1, gameState.PhaseNumber, "Phase number should be at the limit");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(10);
            Assert.AreEqual(
                GameState.MaximumPhasesCount + 1, gameState.PhaseNumber, "Phase number should've remained the same");
        }

        [TestMethod]
        public async Task CannotAddPlayerAfterNeg()
        {
            const ulong id = 1;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(-5);
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong _), "Queue should have been cleared.");
            Assert.IsFalse(await gameState.AddPlayer(id, "Player"), "Add should fail after a neg.");
        }

        [TestMethod]
        public async Task CannotAddPlayerAfterZeroPointBuzz()
        {
            const ulong id = 1;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(0);
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong _), "Queue should have been cleared.");
            Assert.IsFalse(await gameState.AddPlayer(id, "Player"), "Add should fail after a no penalty buzz.");
        }

        [TestMethod]
        public async Task CanAddPlayerAfterCorrectBuzz()
        {
            const ulong id = 1;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(10);
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong _), "Queue should have been cleared.");
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should suceed after correct buzz.");
        }

        [TestMethod]
        public async Task NegScoredCorrectly()
        {
            const ulong id = 123;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(-5);
            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await gameState.GetLastScoringSplits();
            Assert.AreEqual(1, lastSplits.Count, "Only one player should have a score.");
            KeyValuePair<PlayerTeamPair, LastScoringSplit> splitPair = lastSplits.First();

            Assert.AreEqual(id, splitPair.Key.PlayerId, "Unexpected ID.");
            Assert.AreEqual(-5, splitPair.Value.Split.Points, "Unexpected score.");
        }

        [TestMethod]
        public async Task CorrectBuzzScoredCorrectly()
        {
            const ulong id = 123;
            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(id, "Player"), "Add should succeed.");
            gameState.ScorePlayer(10);
            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await gameState.GetLastScoringSplits();
            Assert.AreEqual(1, lastSplits.Count, "Only one player should have a score.");
            KeyValuePair<PlayerTeamPair, LastScoringSplit> splitPair = lastSplits.First();

            Assert.AreEqual(id, splitPair.Key.PlayerId, "Unexpected ID.");
            Assert.AreEqual(10, splitPair.Value.Split.Points, "Unexpected score.");
        }

        [TestMethod]
        public async Task MultipleBuzzesWithCorrectScore()
        {
            const ulong id = 123;
            int[] points = new int[] { 10, -5, 15 };
            GameState gameState = new GameState();
            foreach (int point in points)
            {
                Assert.IsTrue(await gameState.AddPlayer(id, "Player"), $"Add should succeed for point total {point}.");
                gameState.ScorePlayer(point);
                if (point <= 0)
                {
                    gameState.NextQuestion();
                }
            }

            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await gameState.GetLastScoringSplits();
            Assert.AreEqual(1, lastSplits.Count, "Only one player should have a score.");
            KeyValuePair<PlayerTeamPair, LastScoringSplit> splitPair = lastSplits.First();

            Assert.AreEqual(id, splitPair.Key.PlayerId, "Unexpected ID.");
            Assert.AreEqual(points.Sum(), splitPair.Value.Split.Points, "Unexpected score.");
        }

        [TestMethod]
        public async Task DifferentPlayersInQueueScoredCorrectly()
        {
            const ulong firstId = 1;
            const ulong secondId = 22;

            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "Add for first player should succeed.");
            Assert.IsTrue(await gameState.AddPlayer(secondId, "Player2"), "Add for second player should succeed.");
            gameState.ScorePlayer(-5);
            gameState.ScorePlayer(10);

            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await gameState.GetLastScoringSplits();
            Assert.AreEqual(2, lastSplits.Count, "Two players should have scored.");

            KeyValuePair<PlayerTeamPair, LastScoringSplit> scoreGrouping = lastSplits
                .FirstOrDefault(pair => pair.Key.PlayerId == firstId);
            Assert.IsNotNull(scoreGrouping, "We should have a pair which relates to the first player.");
            Assert.AreEqual(-5, scoreGrouping.Value.Split.Points, "The first player should have negged.");

            scoreGrouping = lastSplits.FirstOrDefault(pair => pair.Key.PlayerId == secondId);
            Assert.IsNotNull(scoreGrouping, "We should have a pair which relates to the second player.");
            Assert.AreEqual(10, scoreGrouping.Value.Split.Points, "The second player should have negged.");
        }

        [TestMethod]
        public async Task PlayerOnSameTeamSkippedOnWrongBuzz()
        {
            const ulong firstPlayerId = 1;
            const ulong secondPlayerId = 2;
            const ulong otherTeamPlayerId = 3;
            const string firstPlayerName = "Player1";
            const string secondPlayerName = "Player2";
            const string otherPlayerName = "Player3";
            const string firstTeamId = "Alpha";
            const string secondTeamId = "Beta";

            GameState gameState = CreateGameStateWithByCommandTeamManager(out ByCommandTeamManager teamManager);
            Assert.IsTrue(teamManager.TryAddTeam(firstTeamId, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(secondTeamId, out _), "Couldn't add the second team");
            teamManager.TryAddPlayerToTeam(firstPlayerId, firstPlayerName, firstTeamId);
            teamManager.TryAddPlayerToTeam(secondPlayerId, secondPlayerName, firstTeamId);
            teamManager.TryAddPlayerToTeam(otherTeamPlayerId, otherPlayerName, secondTeamId);

            Assert.IsTrue(
                await gameState.AddPlayer(firstPlayerId, firstPlayerName), "Add should succeed the first time");
            Assert.IsTrue(
                await gameState.AddPlayer(secondPlayerId, secondPlayerName), "Add should succeed the second time");
            Assert.IsTrue(
                await gameState.AddPlayer(otherTeamPlayerId, otherPlayerName), "Add should succeed the third time");

            gameState.ScorePlayer(0);
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId), "There should be another player in the queue");
            Assert.AreEqual(otherTeamPlayerId, nextPlayerId, "Player on the other team should be prompted next");
            gameState.ScorePlayer(0);

            Assert.IsFalse(gameState.TryGetNextPlayer(out _), "No other players should be taken from the queue");
        }

        [TestMethod]
        public async Task TeamIncludedInScore()
        {
            const ulong firstPlayerId = 1;
            const ulong secondPlayerId = 3;
            const string firstPlayerName = "Player1";
            const string secondPlayerName = "Player2";
            const string firstTeamId = "Team1";
            const string secondTeamId = "Team2";

            GameState gameState = CreateGameStateWithByCommandTeamManager(out ByCommandTeamManager teamManager);
            Assert.IsTrue(teamManager.TryAddTeam(firstTeamId, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(secondTeamId, out _), "Couldn't add the second team");
            teamManager.TryAddPlayerToTeam(firstPlayerId, firstPlayerName, firstTeamId);
            teamManager.TryAddPlayerToTeam(secondPlayerId, secondPlayerName, secondTeamId);

            Assert.IsTrue(
                await gameState.AddPlayer(firstPlayerId, firstPlayerName), "Add should succeed the first time");
            gameState.ScorePlayer(10);

            Assert.IsTrue(
                await gameState.AddPlayer(secondPlayerId, secondPlayerName), "Add should succeed the third time");
            gameState.ScorePlayer(15);

            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await gameState.GetLastScoringSplits();
            PlayerTeamPair firstPair = new PlayerTeamPair(firstPlayerId, firstPlayerName, firstTeamId);
            Assert.IsTrue(
                lastSplits.TryGetValue(firstPair, out LastScoringSplit split),
                "Couldn't find split for the first player");
            Assert.AreEqual(10, split.Split.Points, "Unexpected score for the first player");
            Assert.AreEqual(firstTeamId, split.TeamId, "Unexpected team ID for the first player's buzz");

            PlayerTeamPair secondPair = new PlayerTeamPair(secondPlayerId, secondPlayerName, secondTeamId);
            Assert.IsTrue(
                lastSplits.TryGetValue(secondPair, out split), "Couldn't find split for the second player");
            Assert.AreEqual(15, split.Split.Points, "Unexpected score for the second player");
            Assert.AreEqual(secondTeamId, split.TeamId, "Unexpected team ID for the second player's buzz");
        }

        [TestMethod]
        public async Task ScoringBonusSucceeds()
        {
            const ulong firstId = 1;

            GameState gameState = new GameState
            {
                Format = Format.CreateTossupBonusesShootout(false)
            };
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add should succeed.");

            gameState.ScorePlayer(10);
            Assert.AreEqual(PhaseStage.Bonus, gameState.CurrentStage, "Unexpected stage after ansewering correctly");
            IReadOnlyDictionary<string, BonusStats> statsMap = await gameState.GetBonusStats();
            Assert.AreEqual(1, statsMap.Count, "Unexpected number of bonus stats");
            Assert.IsTrue(
                statsMap.TryGetValue(firstId.ToString(CultureInfo.InvariantCulture), out BonusStats bonusStats),
                "Couldn't get the bonus stats from the player");
            Assert.AreEqual(0, bonusStats.Total, "Unexpected bonus total before the bonus is scored");

            Assert.IsTrue(gameState.TryScoreBonus("30"), "Couldn't score the bonus");
            statsMap = await gameState.GetBonusStats();
            Assert.AreEqual(1, statsMap.Count, "Unexpected number of bonus stats");
            Assert.IsTrue(
                statsMap.TryGetValue(firstId.ToString(CultureInfo.InvariantCulture), out bonusStats),
                "Couldn't get the bonus stats from the player after scoring the bonus");
            Assert.AreEqual(30, bonusStats.Total, "Unexpected bonus total after the bonus is scored");
        }

        [TestMethod]
        public async Task UndoOnNoScoreDoesNothing()
        {
            const ulong firstId = 1;

            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "Add should succeed.");
            Assert.IsFalse(gameState.Undo(out _), "Undo should return false.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "We should still have a player in the buzz queue.");
            Assert.AreEqual(firstId, nextPlayerId, "Next player should be the first one.");
        }

        [TestMethod]
        public async Task UndoNeggedQuestion()
        {
            await TestUndoRestoresState(-5);
        }

        [TestMethod]
        public async Task UndoNoPenaltyQuestion()
        {
            await TestUndoRestoresState(0);
        }

        [TestMethod]
        public async Task UndoCorrectQuestion()
        {
            await TestUndoRestoresState(10);
        }

        [TestMethod]
        public async Task UndoNeggedQuestionWithTeams()
        {
            await TestUndoRestoresStateWithTeams(-5);
        }

        [TestMethod]
        public async Task UndoNoPenaltyQuestionWithTeams()
        {
            await TestUndoRestoresStateWithTeams(0);
        }

        [TestMethod]
        public async Task UndoCorrectQuestionWithTeams()
        {
            await TestUndoRestoresStateWithTeams(10);
        }

        [TestMethod]
        public async Task UndoPersistsBetweenQuestions()
        {
            const ulong firstId = 1;
            const ulong secondId = 2;

            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add should succeed.");
            Assert.IsTrue(await gameState.AddPlayer(secondId, "Player2"), "Second add should succeed.");

            gameState.ScorePlayer(10);
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add in second question should succeed.");
            gameState.ScorePlayer(15);

            Assert.IsTrue(gameState.Undo(out ulong? firstUndoId), "First undo should succeed.");
            Assert.AreEqual(firstId, firstUndoId, "First ID returned by undo is incorrect.");
            Assert.IsTrue(gameState.Undo(out ulong? secondUndoId), "Second undo should succeed.");
            Assert.AreEqual(firstId, secondUndoId, "Second ID returned by undo is incorrect.");

            gameState.ScorePlayer(-5);
            Assert.IsTrue(gameState.TryGetNextPlayer(out ulong nextPlayerId), "There should be a player in the queue.");
            Assert.AreEqual(secondId, nextPlayerId, "Wrong player in queue.");
        }

        [TestMethod]
        public async Task UndoAndWithdrawPromptsNextPlayerOnTeam()
        {
            const ulong firstUserId = 1;
            const ulong secondUserId = 2;
            const string teamId = "Alpha";
            string firstPlayerName = $"Player {firstUserId}";
            string secondPlayerName = $"Player {secondUserId}";

            GameState gameState = CreateGameStateWithByCommandTeamManager(out ByCommandTeamManager teamManager);
            Assert.IsTrue(teamManager.TryAddTeam(teamId, out _), "Couldn't add the team");
            teamManager.TryAddPlayerToTeam(firstUserId, firstPlayerName, teamId);
            teamManager.TryAddPlayerToTeam(secondUserId, secondPlayerName, teamId);

            Assert.IsTrue(
                await gameState.AddPlayer(firstUserId, firstPlayerName),
                "Adding the first player should succeed.");
            Assert.IsTrue(
                await gameState.AddPlayer(secondUserId, secondPlayerName),
                "Adding the second player should succeed.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "There should be a player in the queue.");
            Assert.AreEqual(firstUserId, nextPlayerId, "Id of the next player should be ours.");
            gameState.ScorePlayer(-5);

            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _),
                "We shouldn't get any other players in the queue since they're on the same team");
            Assert.IsTrue(gameState.Undo(out ulong? nextUndonePlayerId), "Undo should've succeeded");
            Assert.AreEqual(firstUserId, nextUndonePlayerId, "Player returned by Undo should be the first one");

            Assert.IsTrue(await gameState.WithdrawPlayer(firstUserId), "Withdrawing the first player should succeed.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out nextPlayerId), "There should be another player in the queue.");
            Assert.AreEqual(secondUserId, nextPlayerId, "Second player should be prompted");
        }

        [TestMethod]
        public async Task UndoNextQuestionStopsAfterOnePhase()
        {
            const ulong firstId = 1;

            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "Add should succeed.");
            gameState.ScorePlayer(10);
            gameState.NextQuestion();
            Assert.AreEqual(3, gameState.PhaseNumber, "Unexpected phase after NextQuestion");

            Assert.IsTrue(gameState.Undo(out ulong? undoPlayerId), "Undo should return true");
            Assert.IsNull(undoPlayerId, "No player should've been returned");
            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _),
                "We should have no player's in the buzz queue.");
            Assert.AreEqual(2, gameState.PhaseNumber, "Unexpected phase after undo");
        }

        [TestMethod]
        public async Task UndoNextQuestionTossupsOnly()
        {
            const ulong firstId = 1;
            const ulong secondId = 2;

            GameState gameState = new GameState();
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add should succeed.");
            gameState.ScorePlayer(10);
            Assert.IsTrue(await gameState.AddPlayer(secondId, "Player2"), "Second add should succeed.");
            gameState.ScorePlayer(-5);
            gameState.NextQuestion();
            Assert.AreEqual(3, gameState.PhaseNumber, "Unexpected phase after NextQuestion");

            Assert.IsTrue(gameState.Undo(out ulong? undoPlayerId), "Undo should return true");
            Assert.IsNull(undoPlayerId, "No player should've been returned");
            Assert.IsFalse(
                gameState.TryGetNextPlayer(out _),
                "We should have no player's in the buzz queue.");
            Assert.AreEqual(2, gameState.PhaseNumber, "Unexpected phase after undo");

            bool addPlayer = await gameState.AddPlayer(secondId, "Player2");
            Assert.IsFalse(addPlayer, "Second player was added to the queue, but they already buzzed in");
        }

        [TestMethod]
        public async Task UndoNextQuestionAfterBonus()
        {
            const ulong firstId = 1;

            GameState gameState = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false)
            };

            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add should succeed.");
            gameState.ScorePlayer(10);
            gameState.NextQuestion();
            Assert.AreEqual(2, gameState.PhaseNumber, "Unexpected phase after NextQuestion");

            Assert.IsTrue(gameState.Undo(out ulong? undoPlayerId), "Undo should return true");
            Assert.IsNull(undoPlayerId, "No player should've been returned");
            Assert.AreEqual(1, gameState.PhaseNumber, "Unexpected phase after undo");

            Assert.AreEqual(PhaseStage.Bonus, gameState.CurrentStage, "Unexpected stage");
        }

        public static async Task TestUndoRestoresState(int pointsFromBuzz)
        {
            const ulong firstId = 1;
            const ulong secondId = 2;
            const int firstPointsFromBuzz = 10;

            GameState gameState = new GameState();
            // To make sure we're not just clearing the field, give the first player points
            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add should succeed.");
            gameState.ScorePlayer(firstPointsFromBuzz);

            Assert.IsTrue(await gameState.AddPlayer(firstId, "Player1"), "First add in second question should succeed.");
            Assert.IsTrue(await gameState.AddPlayer(secondId, "Player2"), "Second add in second question should succeed.");

            gameState.ScorePlayer(pointsFromBuzz);
            IDictionary<ulong, int> scores = (await gameState.GetLastScoringSplits())
                .ToDictionary(lastSplitPair => lastSplitPair.Key.PlayerId,
                lastSplitPair => lastSplitPair.Value.Split.Points);
            Assert.IsTrue(scores.TryGetValue(firstId, out int score), "Unable to get score for the first player.");
            Assert.AreEqual(pointsFromBuzz + firstPointsFromBuzz, score, "Incorrect score.");

            Assert.IsTrue(gameState.Undo(out ulong? id), "Undo should return true.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "We should still have a player in the buzz queue.");
            Assert.AreEqual(firstId, nextPlayerId, "Next player should be the first one.");

            scores = (await gameState.GetLastScoringSplits())
                .ToDictionary(lastSplitPair => lastSplitPair.Key.PlayerId,
                lastSplitPair => lastSplitPair.Value.Split.Points);
            Assert.IsTrue(
                scores.TryGetValue(firstId, out int scoreAfterUndo),
                "Unable to get score for the first player after undo.");
            Assert.AreEqual(firstPointsFromBuzz, scoreAfterUndo, "Incorrect score after undo.");

            Assert.IsFalse(
                await gameState.AddPlayer(firstId, "Player1"),
                "First player already buzzed, so we shouldn't be able to add them again.");
            Assert.IsFalse(
                await gameState.AddPlayer(secondId, "Player2"),
                "Second player already buzzed, so we shouldn't be able to add them again.");

            gameState.ScorePlayer(0);
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong finalPlayerId),
                "Buzz queue should have two players after an undo.");
            Assert.AreEqual(secondId, finalPlayerId, "Next player should be the second one.");
        }

        public static async Task TestUndoRestoresStateWithTeams(int pointsFromBuzz)
        {
            const ulong firstId = 1;
            const ulong secondId = 2;
            const string firstPlayerName = "Player1";
            const string secondPlayerName = "Player2";
            const string firstTeamId = "1001";
            const string secondTeamId = "1002";
            const int firstPointsFromBuzz = 10;

            GameState gameState = CreateGameStateWithByCommandTeamManager(out ByCommandTeamManager teamManager);
            Assert.IsTrue(teamManager.TryAddTeam(firstTeamId, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(secondTeamId, out _), "Couldn't add the second team");
            teamManager.TryAddPlayerToTeam(firstId, firstPlayerName, firstTeamId);
            teamManager.TryAddPlayerToTeam(secondId, secondPlayerName, secondTeamId);

            // To make sure we're not just clearing the field, give the first player points
            Assert.IsTrue(await gameState.AddPlayer(firstId, firstPlayerName), "First add should succeed.");
            gameState.ScorePlayer(firstPointsFromBuzz);

            Assert.IsTrue(
                await gameState.AddPlayer(firstId, firstPlayerName), "First add in second question should succeed.");
            Assert.IsTrue(
                await gameState.AddPlayer(secondId, secondPlayerName), "Second add in second question should succeed.");

            gameState.ScorePlayer(pointsFromBuzz);
            IDictionary<ulong, int> scores = (await gameState.GetLastScoringSplits())
                .ToDictionary(lastSplitPair => lastSplitPair.Key.PlayerId,
                    lastSplitPair => lastSplitPair.Value.Split.Points);
            Assert.IsTrue(scores.TryGetValue(firstId, out int score), "Unable to get score for the first player.");
            Assert.AreEqual(pointsFromBuzz + firstPointsFromBuzz, score, "Incorrect score.");

            Assert.IsTrue(gameState.Undo(out ulong? id), "Undo should return true.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "We should still have a player in the buzz queue.");
            Assert.AreEqual(firstId, nextPlayerId, "Next player should be the first one.");

            scores = (await gameState.GetLastScoringSplits())
                .ToDictionary(lastSplitPair => lastSplitPair.Key.PlayerId,
                    lastSplitPair => lastSplitPair.Value.Split.Points);
            Assert.IsTrue(
                scores.TryGetValue(firstId, out int scoreAfterUndo),
                "Unable to get score for the first player after undo.");
            Assert.AreEqual(firstPointsFromBuzz, scoreAfterUndo, "Incorrect score after undo.");

            Assert.IsFalse(
                await gameState.AddPlayer(firstId, "Player1"),
                "First player already buzzed, so we shouldn't be able to add them again.");
            Assert.IsFalse(
                await gameState.AddPlayer(secondId, "Player2"),
                "Second player already buzzed, so we shouldn't be able to add them again.");

            gameState.ScorePlayer(0);
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong finalPlayerId),
                "Buzz queue should have two players after an undo.");
            Assert.AreEqual(secondId, finalPlayerId, "Next player should be the second one.");
        }

        private static GameState CreateGameStateWithByCommandTeamManager(out ByCommandTeamManager teamManager)
        {
            teamManager = new ByCommandTeamManager();
            return new GameState()
            {
                ReaderId = 0,
                TeamManager = teamManager
            };
        }
    }
}
