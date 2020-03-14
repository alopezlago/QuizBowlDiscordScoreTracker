using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QBDiscordScoreTracker;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class TeamNameParserTests
    {
        [TestMethod]
        public void EmptyString()
        {
            Assert.IsTrue(
                TeamNameParser.TryGetTeamNamesFromParts("", out IList<string> names, out string errorMessage),
                "No teams should be parsable.");
            Assert.AreEqual(0, names.Count, "Names should be empty.");
        }

        [TestMethod]
        public void OneTeam()
        {
            const string teamName = "Team";
            Assert.IsTrue(
                TeamNameParser.TryGetTeamNamesFromParts(teamName, out IList<string> names, out string errorMessage),
                "Should be parsable.");
            Assert.AreEqual(1, names.Count, "There should be one name");
            Assert.IsTrue(names.Contains(teamName), $"Team name was not found. Names: {string.Join(",", names)}");
        }

        [TestMethod]
        public void TwoTeams()
        {
            const string teamNames = "Team1,Team2";
            Assert.IsTrue(
                TeamNameParser.TryGetTeamNamesFromParts(teamNames, out IList<string> names, out string errorMessage),
                "Should be parsable.");
            Assert.AreEqual(2, names.Count, "There should be one name");
            Assert.IsTrue(names.Contains("Team1"), $"Team 'Team1' name was not found. Names: {string.Join(",", names)}");
            Assert.IsTrue(names.Contains("Team2"), $"Team 'Team2' name was not found. Names: {string.Join(",", names)}");
        }

        [TestMethod]
        public void EscapedComma()
        {
            const string escapedTeamName = "One,,Team";
            const string teamName = "One,Team";
            Assert.IsTrue(
                TeamNameParser.TryGetTeamNamesFromParts(
                    escapedTeamName, out IList<string> names, out string errorMessage),
                "Should be parsable.");
            Assert.AreEqual(1, names.Count, "There should be one name");
            Assert.IsTrue(names.Contains(teamName), $"Team name was not found. Names: {string.Join(",", names)}");
        }

        [TestMethod]
        public void MultipleEscapedComma()
        {
            const string escapedTeamName = "One,,,,Team";
            const string teamName = "One,,Team";
            Assert.IsTrue(
                TeamNameParser.TryGetTeamNamesFromParts(
                    escapedTeamName, out IList<string> names, out string errorMessage),
                "Should be parsable.");
            Assert.AreEqual(1, names.Count, "There should be one name");
            Assert.IsTrue(names.Contains(teamName), $"Team name was not found. Names: {string.Join(",", names)}");
        }

        [TestMethod]
        public void EscapedCommaThenSeparatorComma()
        {
            const string escapedTeamNames = "One,,,Team";
            const string firstTeamName = "One,";
            const string secondTeam = "Team";
            Assert.IsTrue(
                TeamNameParser.TryGetTeamNamesFromParts(
                    escapedTeamNames, out IList<string> names, out string errorMessage),
                "Should be parsable.");
            Assert.AreEqual(2, names.Count, "There should be one name");
            Assert.IsTrue(
                names.Contains(firstTeamName),
                $"Team with commas in name was not found. Names: {string.Join(",", names)}");
            Assert.IsTrue(
                names.Contains(secondTeam), $"Second team was not found. Names: {string.Join(",", names)}");
        }

        [TestMethod]
        public void TrailingCommaProducesError()
        {
            const string teamNames = "Team1,";
            Assert.IsFalse(
                TeamNameParser.TryGetTeamNamesFromParts(teamNames, out IList<string> names, out string errorMessage),
                "No teams should be parsable.");
            Assert.IsNotNull(errorMessage, "Error message should have a value.");
        }
    }
}
