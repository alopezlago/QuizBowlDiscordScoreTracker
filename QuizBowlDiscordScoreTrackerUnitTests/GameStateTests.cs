using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;
using System.Collections.Generic;

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
                gameState.TryGetNextPlayer(out ulong nextPlayerId), 
                "There should be no next player if no one was added to the queue.");
        }

        [TestMethod]
        public void CannotAddReaderToQueue()
        {
            GameState gameState = new GameState();
            gameState.ReaderId = 123;
            Assert.IsFalse(
                gameState.AddPlayer(gameState.ReaderId.Value),
                "Adding the reader to the queue should not be possible.");
        }

        [TestMethod]
        public void ReaderIdPersists()
        {
            const ulong readerId = 123;
            GameState gameState = new GameState();
            gameState.ReaderId = readerId;
            Assert.AreEqual(readerId, gameState.ReaderId, "Reader Id is not persisted.");
        }

        [TestMethod]
        public void CannotAddSamePlayerTwiceToQueue()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Adding the player the first time should succeed.");
            Assert.IsFalse(gameState.AddPlayer(id), "Adding the player the second time should fail.");
        }

        [TestMethod]
        public void FirstAddedPlayerIsTopOfQueue()
        {
            const ulong firstId = 1;
            const ulong secondId = 2;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(firstId), "Adding the player the first time should succeed.");
            Assert.IsTrue(gameState.AddPlayer(secondId), "Adding the player the second time should succeed.");
            Assert.IsTrue(gameState.TryGetNextPlayer(out ulong nextPlayerId), "There should be a player in the queue.");
            Assert.AreEqual(firstId, nextPlayerId, "The player first in the queue should be the first one added.");
        }

        [TestMethod]
        public void PlayerOrderInQueue()
        {
            ulong[] ids = new ulong[] { 1, 2, 3, 4 };
            GameState gameState = new GameState();
            foreach (ulong id in ids)
            {
                Assert.IsTrue(gameState.AddPlayer(id), $"Should be able to add {id} to the queue.");
            }

            ulong nextPlayerId;
            foreach (ulong id in ids)
            {
                Assert.IsTrue(
                    gameState.TryGetNextPlayer(out nextPlayerId),
                    $"Should be able to get a player from the queue (which should match ID {id}.");
                Assert.AreEqual(id, nextPlayerId, "Unexpected ID from the queue.");
                gameState.ScorePlayer(0);
            }

            Assert.IsFalse(
                gameState.TryGetNextPlayer(out nextPlayerId), "No players should be left in the queue.");
        }

        [TestMethod]
        public void CanWithdrawPlayerOnTopOfQueue()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();

            ulong nextPlayerId;
            Assert.IsTrue(gameState.AddPlayer(id), "Adding the player should succeed.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out nextPlayerId),
                "There should be a player in the queue.");
            Assert.AreEqual(id, nextPlayerId, "Id of the next player should be ours.");
            Assert.IsTrue(gameState.WithdrawPlayer(id), "Withdrawing the same player should succeed.");
            Assert.IsFalse(
                gameState.TryGetNextPlayer(out nextPlayerId),
                "There should be no player in the queue when they withdrew.");
        }

        [TestMethod]
        public void CanWithdrawPlayerInMiddleOfQueue()
        {
            const ulong firstId = 1;
            const ulong secondId = 22;
            const ulong thirdId = 333;
            GameState gameState = new GameState();

            ulong nextPlayerId;
            Assert.IsTrue(gameState.AddPlayer(firstId), "Adding the first player should succeed.");
            Assert.IsTrue(gameState.AddPlayer(secondId), "Adding the second player should succeed.");
            Assert.IsTrue(gameState.AddPlayer(thirdId), "Adding the third player should succeed.");
            Assert.IsTrue(gameState.WithdrawPlayer(secondId), "Withdrawing the second player should succeed.");
            Assert.IsTrue(gameState.WithdrawPlayer(firstId), "Withdrawing the first player should succeed.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out nextPlayerId),
                "There should be a player in the queue.");
            Assert.AreEqual(thirdId, nextPlayerId, "Id of the next player should be the third player's.");
        }

        [TestMethod]
        public void CannotWithdrawPlayerNotInQueue()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            gameState.AddPlayer(id);
            Assert.IsFalse(gameState.WithdrawPlayer(id + 1), "Should not be able to withdraw player who is not in the queue.");
        }

        [TestMethod]
        public void CannotWithdrawSamePlayerInQueueTwiceInARow()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Adding player should succeed.");
            Assert.IsTrue(gameState.WithdrawPlayer(id), "First withdrawal should succeed.");
            Assert.IsFalse(gameState.WithdrawPlayer(id), "Second withdrawal should fail.");
        }

        [TestMethod]
        public void CanWithdrawSamePlayerInQueueTwice()
        {
            const ulong id = 1234;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "First add should succeed.");
            Assert.IsTrue(gameState.WithdrawPlayer(id), "First withdrawal should succeed.");
            Assert.IsTrue(gameState.AddPlayer(id), "Second add should succeed.");
            Assert.IsTrue(gameState.WithdrawPlayer(id), "Second withdrawal should succeed.");
        }

        [TestMethod]
        public void ClearCurrentRoundClearsQueueAndKeepsReader()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState();
            gameState.ReaderId = readerId;

            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ClearCurrentRound();
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong nextPlayerId), "Queue should have been cleared.");
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed after clear.");
            Assert.AreEqual(readerId, gameState.ReaderId, "Reader should remain the same.");
        }

        [TestMethod]
        public void ClearAllClearsQueueAndReader()
        {
            const ulong id = 1234;
            const ulong readerId = 12345;
            GameState gameState = new GameState();
            gameState.ReaderId = readerId;

            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ClearAll();
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong nextPlayerId), "Queue should have been cleared.");
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed after clear.");
            Assert.IsNull(gameState.ReaderId, "Reader should be cleared.");
        }

        [TestMethod]
        public void CannotAddPlayerAfterNeg()
        {
            const ulong id = 1;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ScorePlayer(-5);
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong nextPlayerId), "Queue should have been cleared.");
            Assert.IsFalse(gameState.AddPlayer(id), "Add should fail after a neg.");
        }

        [TestMethod]
        public void CannotAddPlayerAfterZeroPointBuzz()
        {
            const ulong id = 1;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ScorePlayer(0);
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong nextPlayerId), "Queue should have been cleared.");
            Assert.IsFalse(gameState.AddPlayer(id), "Add should fail after a no penalty buzz.");
        }

        [TestMethod]
        public void CanAddPlayerAfterCorrectBuzz()
        {
            const ulong id = 1;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ScorePlayer(10);
            Assert.IsFalse(gameState.TryGetNextPlayer(out ulong nextPlayerId), "Queue should have been cleared.");
            Assert.IsTrue(gameState.AddPlayer(id), "Add should suceed after correct buzz.");
        }

        [TestMethod]
        public void NegScoredCorrectly()
        {
            const ulong id = 123;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ScorePlayer(-5);
            IEnumerable<KeyValuePair<ulong, int>> scores = gameState.GetScores();
            Assert.AreEqual(1, scores.Count(), "Only one player should have a score.");
            KeyValuePair<ulong, int> scorePair = scores.First();

            Assert.AreEqual(id, scorePair.Key, "Unexpected ID.");
            Assert.AreEqual(-5, scorePair.Value, "Unexpected score.");
        }

        [TestMethod]
        public void CorrectBuzzScoredCorrectly()
        {
            const ulong id = 123;
            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(id), "Add should succeed.");
            gameState.ScorePlayer(10);
            IEnumerable<KeyValuePair<ulong, int>> scores = gameState.GetScores();
            Assert.AreEqual(1, scores.Count(), "Only one player should have a score.");
            KeyValuePair<ulong, int> scorePair = scores.First();

            Assert.AreEqual(id, scorePair.Key, "Unexpected ID.");
            Assert.AreEqual(10, scorePair.Value, "Unexpected score.");
        }

        [TestMethod]
        public void MultipleBuzzesWithCorrectScore()
        {
            const ulong id = 123;
            int[] points = new int[] { 10, -5, 15 };
            GameState gameState = new GameState();
            foreach (int point in points)
            {
                Assert.IsTrue(gameState.AddPlayer(id), $"Add should succeed for point total {point}.");
                gameState.ScorePlayer(point);
                if (point <= 0)
                {
                    gameState.ClearCurrentRound();
                }                
            }

            IEnumerable<KeyValuePair<ulong, int>> scores = gameState.GetScores();
            Assert.AreEqual(1, scores.Count(), "Only one player should have a score.");
            KeyValuePair<ulong, int> scorePair = scores.First();

            Assert.AreEqual(id, scorePair.Key, "Unexpected ID.");
            Assert.AreEqual(points.Sum(), scorePair.Value, "Unexpected score.");
        }

        [TestMethod]
        public void DifferentPlayersInQueueScoredCorrectly()
        {
            const ulong firstId = 1;
            const ulong secondId = 22;

            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(firstId), "Add for first player should succeed.");
            Assert.IsTrue(gameState.AddPlayer(secondId), "Add for second player should succeed.");
            gameState.ScorePlayer(-5);
            gameState.ScorePlayer(10);

            IEnumerable<KeyValuePair<ulong, int>> scores = gameState.GetScores();
            Assert.AreEqual(2, scores.Count(), "Only one player should have a score.");

            KeyValuePair<ulong, int> scorePair = scores.FirstOrDefault(pair => pair.Key == firstId);
            Assert.IsNotNull(scorePair, "We should have a pair which relates to the first player.");
            Assert.AreEqual(-5, scorePair.Value, "The first player should have negged.");

            scorePair = scores.FirstOrDefault(pair => pair.Key == secondId);
            Assert.IsNotNull(scorePair, "We should have a pair which relates to the second player.");
            Assert.AreEqual(10, scorePair.Value, "The second player should have negged.");
        }

        [TestMethod]
        public void UndoOnNoScoreDoesNothing()
        {
            const ulong firstId = 1;

            GameState gameState = new GameState();
            Assert.IsTrue(gameState.AddPlayer(firstId), "Add should succeed.");
            Assert.IsFalse(gameState.Undo(out ulong id), "Undo should return false.");
            Assert.IsTrue(
                gameState.TryGetNextPlayer(out ulong nextPlayerId),
                "We should still have a player in the buzz queue.");
            Assert.AreEqual(firstId, nextPlayerId, "Next player should be the first one.");
        }

        // TODO: Add tests for undo
        // - Undo on one score (negative/zero) puts that user back to the front of the buzz queue, and undoes their score. Queues are restored.
        // - Undo on one score (positive) puts that user back to the front of the buzz queue, and undoes their score. Queues are restored.
        // - Undo has a limit, if we score over that, we can't get back the one past the limit.
        // - Undo can carry between questions

        // TODO: Add tests for Bot. We'd want to create another class that implements the event handlers, but has different arguments
        // which don't require Discord-specific classes.
    }
}
