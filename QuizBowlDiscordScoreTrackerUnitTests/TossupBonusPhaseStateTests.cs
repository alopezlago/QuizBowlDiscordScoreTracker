using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class TossupBonusPhaseStateTests
    {
        private static readonly Buzz DefaultBuzz = new Buzz()
        {
            TeamId = "10",
            UserId = 1,
            PlayerDisplayName = "Alice",
            Timestamp = DateTime.Now
        };

        [TestMethod]
        public void TestStagesForCorrectBuzz()
        {
            TossupBonusPhaseState phaseState = new TossupBonusPhaseState(false);
            Assert.IsFalse(phaseState.HasBonus, "We shouldn't have a bonus until a correct buzz");
            Assert.AreEqual(PhaseStage.Tossup, phaseState.CurrentStage, "We should be in the tossup phase");

            phaseState.AddBuzz(DefaultBuzz);
            Assert.IsTrue(phaseState.TryScoreBuzz(10), "Scoring the buzz should succeed");

            Assert.IsTrue(phaseState.HasBonus, "We should have a bonus on a correct buzz");
            Assert.AreEqual(PhaseStage.Bonus, phaseState.CurrentStage, "We should be in the bonus phase");

            Assert.IsTrue(phaseState.TryScoreBonus("0"), "Scoring the bonus should succeed");
            Assert.AreEqual(PhaseStage.Complete, phaseState.CurrentStage, "We should be done with this phase");
        }

        [TestMethod]
        public void TestStagesForIncorrectBuzz()
        {
            TossupBonusPhaseState phaseState = new TossupBonusPhaseState(false);
            Assert.IsFalse(phaseState.HasBonus, "We shouldn't have a bonus until a correct buzz");
            Assert.AreEqual(PhaseStage.Tossup, phaseState.CurrentStage, "We should be in the tossup phase");

            phaseState.AddBuzz(DefaultBuzz);
            Assert.IsTrue(phaseState.TryScoreBuzz(0), "Scoring the buzz should succeed");

            // Should be the same
            Assert.IsFalse(phaseState.HasBonus, "We shouldn't have a bonus on an incorrect buzz");
            Assert.AreEqual(PhaseStage.Tossup, phaseState.CurrentStage, "We should still be in the tossup phase");
        }

        [TestMethod]
        public void OnlyOneBuzzHandledWhenQueueDisabled()
        {
            Buzz lateBuzz = new Buzz()
            {
                PlayerDisplayName = "Late",
                TeamId = "743",
                Timestamp = DateTime.Now,
                UserId = DefaultBuzz.UserId + 1
            };

            TossupBonusPhaseState phaseState = new TossupBonusPhaseState(disableBuzzQueue: true);
            Assert.IsFalse(phaseState.HasBonus, "We shouldn't have a bonus until a correct buzz");
            Assert.AreEqual(PhaseStage.Tossup, phaseState.CurrentStage, "We should be in the tossup phase");

            phaseState.AddBuzz(DefaultBuzz);
            phaseState.AddBuzz(lateBuzz);

            Assert.IsTrue(phaseState.TryScoreBuzz(0), "Scoring the buzz should succeed");
            Assert.IsFalse(
                phaseState.TryGetNextPlayer(out _), "There shouldn't be another player after the incorrect buzz");
            Assert.IsFalse(phaseState.HasBonus, "We should still be on the tossup phase");

            phaseState.AddBuzz(lateBuzz);
            Assert.IsTrue(
                phaseState.TryGetNextPlayer(out _), "There should be a player after buzzing in again");
            Assert.IsTrue(phaseState.TryScoreBuzz(10), "Scoring the buzz the second time should succeed");
            Assert.AreEqual(PhaseStage.Bonus, phaseState.CurrentStage, "We should be in the bonus phase");
        }

        [TestMethod]
        public void ZeroStringScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsTrue(phaseState.TryScoreBonus("0"), "Scoring the bonus should succeed");
            Assert.AreEqual(
                TossupBonusPhaseState.DefaultBonusLength, phaseState.BonusScores.Count, "We should have three parts scored");
            Assert.IsTrue(
                phaseState.BonusScores.All(score => score == 0),
                $"Not all parts were scored a 0: {string.Join('/', phaseState.BonusScores)}");
        }

        [TestMethod]
        public void ThirtyStringScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsTrue(phaseState.TryScoreBonus("30"), "Scoring the bonus should succeed");
            Assert.AreEqual(
                TossupBonusPhaseState.DefaultBonusLength, phaseState.BonusScores.Count, "We should have three parts scored");
            Assert.IsTrue(
                phaseState.BonusScores.All(score => score == 10),
                $"Not all parts were scored a 0: {string.Join('/', phaseState.BonusScores)}");
        }

        [TestMethod]
        public void ThreeSplitsScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsTrue(phaseState.TryScoreBonus("10/0/10"), "Scoring the bonus should succeed");
            Assert.AreEqual(
                TossupBonusPhaseState.DefaultBonusLength, phaseState.BonusScores.Count, "We should have three parts scored");
            CollectionAssert.AreEqual(
                new int[] { 10, 0, 10 },
                phaseState.BonusScores.ToArray(),
                $"Not all parts were scored correctly. Expected 10/0/10, got {string.Join('/', phaseState.BonusScores)}");
        }

        [TestMethod]
        public void ThreeBinaryDigitsScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsTrue(phaseState.TryScoreBonus("011"), "Scoring the bonus should succeed");
            Assert.AreEqual(
                TossupBonusPhaseState.DefaultBonusLength, phaseState.BonusScores.Count, "We should have three parts scored");
            CollectionAssert.AreEqual(
                new int[] { 0, 10, 10 },
                phaseState.BonusScores.ToArray(),
                $"Not all parts were scored correctly. Expected 0/10/10, got {string.Join('/', phaseState.BonusScores)}");
        }

        [TestMethod]
        public void TenAndTwentyNotScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsFalse(phaseState.TryScoreBonus("10"), "Scoring the bonus should've failed for 2 splits");
            Assert.AreEqual(
                0, phaseState.BonusScores.Count, "No bonus should've been scored for 10, since we don't know the splits");

            Assert.IsFalse(phaseState.TryScoreBonus("20"), "Scoring the bonus should've failed for 4 splits");
            Assert.AreEqual(
                0, phaseState.BonusScores.Count, "No bonus should've been scored for 20, since we don't know the splits");
        }

        [TestMethod]
        public void NonThreeSplitsNotScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsFalse(phaseState.TryScoreBonus("10/0"), "Scoring the bonus should've failed for 2 splits");
            Assert.AreEqual(0, phaseState.BonusScores.Count, "No bonus should've been scored for 2 splits");

            Assert.IsFalse(phaseState.TryScoreBonus("10/0/10/10"), "Scoring the bonus should've failed for 4 splits");
            Assert.AreEqual(0, phaseState.BonusScores.Count, "No bonus should've been scored for 4 splits");
        }

        [TestMethod]
        public void NonThreeBinaryDigitsNotScoredForBonus()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsFalse(phaseState.TryScoreBonus("11"), "Scoring the bonus should've failed for 2 splits");
            Assert.AreEqual(0, phaseState.BonusScores.Count, "No bonus should've been scored for 2 digits");

            Assert.IsFalse(phaseState.TryScoreBonus("1011"), "Scoring the bonus should've failed for 4 splits");
            Assert.AreEqual(0, phaseState.BonusScores.Count, "No bonus should've been scored for 4 digits");
        }

        // TODO: Add tests for Undo
        [TestMethod]
        public void UndoScoredTossup()
        {
            TossupBonusPhaseState phaseState = new TossupBonusPhaseState(false);
            phaseState.AddBuzz(DefaultBuzz);
            Assert.IsTrue(phaseState.TryScoreBuzz(-5), "Scoring the first buzz should succeed");

            Assert.IsTrue(
                phaseState.AlreadyBuzzedPlayerIds.Contains(DefaultBuzz.UserId),
                "Default player not in the list of already buzzed players");
            Assert.AreEqual(1, phaseState.Actions.Count, "Unexpected number of buzzes recorded");

            ulong secondBuzzPlayerId = DefaultBuzz.UserId + 1;
            string secondTeamId = $"{DefaultBuzz.TeamId}1";
            Buzz incorrectBuzz = new Buzz()
            {
                TeamId = secondTeamId,
                PlayerDisplayName = "Bob",
                UserId = secondBuzzPlayerId,
                Timestamp = DateTime.Now + TimeSpan.FromSeconds(1)
            };

            phaseState.AddBuzz(incorrectBuzz);
            Assert.IsTrue(phaseState.TryScoreBuzz(0), "Scoring the second buzz should succeed");
            Assert.IsTrue(
                phaseState.AlreadyBuzzedPlayerIds.Contains(secondBuzzPlayerId),
                "Second player not in the list of already buzzed players");
            Assert.AreEqual(2, phaseState.Actions.Count, "Unexpected number of buzzes recorded after the second buzz");

            phaseState.Undo(out ulong? userId);
            Assert.AreEqual(secondBuzzPlayerId, userId, "Unexpected userId returned by Undo");
            Assert.IsTrue(
                phaseState.AlreadyScoredTeamIds.Contains(DefaultBuzz.TeamId),
                "Default player not in the list of already scored teams after the first undo");
            Assert.IsFalse(
                phaseState.AlreadyScoredTeamIds.Contains(secondTeamId),
                "Second player not in the list of already scored teams after the first undo");
            Assert.AreEqual(1, phaseState.Actions.Count, "Unexpected number of buzzes recorded after the first undo");

            phaseState.Undo(out userId);
            Assert.AreEqual(DefaultBuzz.UserId, userId, "Unexpected userId returned by Undo");
            Assert.IsFalse(
                phaseState.AlreadyScoredTeamIds.Contains(DefaultBuzz.TeamId),
                "Default player not in the list of already scored teams after the second undo");
            Assert.AreEqual(0, phaseState.Actions.Count, "Unexpected number of buzzes recorded after the second undo");
        }

        [TestMethod]
        public void UndoBonusBeforeScoringIt()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();

            phaseState.Undo(out ulong? userId);
            Assert.IsFalse(phaseState.HasBonus, "We shouldn't have a bonus now");
            Assert.AreEqual(PhaseStage.Tossup, phaseState.CurrentStage, "We should be in the tossup stage");
            Assert.AreEqual(DefaultBuzz.UserId, userId, "Unexpected userId");
            Assert.IsFalse(
                phaseState.AlreadyScoredTeamIds.Contains(DefaultBuzz.TeamId),
                "Default player not in the list of already scored teams after the second undo");
            Assert.AreEqual(0, phaseState.Actions.Count, "Unexpected number of buzzes recorded after the second undo");
        }

        [TestMethod]
        public void UndoBonusAfterScoringIt()
        {
            TossupBonusPhaseState phaseState = CreatePhaseInBonusStage();
            Assert.IsTrue(phaseState.TryScoreBonus("0"), "Scoring the bonus should've succeeded");
            Assert.AreEqual(
                TossupBonusPhaseState.DefaultBonusLength, phaseState.BonusScores.Count, "We should have three parts scored");

            phaseState.Undo(out ulong? userId);
            Assert.IsNull(userId, "userId should be null (scoring a bonus)");
            Assert.AreEqual(0, phaseState.BonusScores.Count, "Bonus scores should be cleared, but not gone");
            Assert.AreEqual(PhaseStage.Bonus, phaseState.CurrentStage, "Unexpected stage after first undo");
            Assert.IsTrue(
                phaseState.AlreadyScoredTeamIds.Contains(DefaultBuzz.TeamId),
                "Default player not in the list of already scored teams after the second undo");
            Assert.AreEqual(1, phaseState.Actions.Count, "Unexpected number of buzzes recorded after the second undo");

            phaseState.Undo(out userId);
            Assert.AreEqual(DefaultBuzz.UserId, userId, "userId should not be null after undoing the bonus score");
            Assert.IsNull(phaseState.BonusScores, "Bonus scores should be gone");
            Assert.AreEqual(PhaseStage.Tossup, phaseState.CurrentStage, "Unexpected stage after second undo");
        }

        private static TossupBonusPhaseState CreatePhaseInBonusStage()
        {
            TossupBonusPhaseState phaseState = new TossupBonusPhaseState(false);
            phaseState.AddBuzz(DefaultBuzz);
            phaseState.TryScoreBuzz(10);
            return phaseState;
        }
    }
}
