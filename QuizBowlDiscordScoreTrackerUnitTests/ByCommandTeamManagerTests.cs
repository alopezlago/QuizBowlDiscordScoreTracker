using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class ByCommandTeamManagerTests
    {
        private const string FirstTeam = "Alpha";
        private const string SecondTeam = "Beta";
        private const ulong FirstPlayerId = 1;
        private const ulong SecondPlayerId = 2;

        [TestMethod]
        public async Task AddingPlayerToTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Couldn't add player to team");
            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players");
            PlayerTeamPair pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should be known");
            Assert.AreEqual(FirstTeam, pair.TeamId, "Player should be on the first team");
        }

        [TestMethod]
        public async Task AddingSamePlayerToSameTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Couldn't add player to team");
            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players");
            PlayerTeamPair pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should be known");
            Assert.AreEqual(FirstTeam, pair.TeamId, "Player should be on the first team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Should be able to add the same player to the same team (as a no-op)");
            pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players after the second add");
            pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should still be known");
            Assert.AreEqual(FirstTeam, pair.TeamId, "Player should still be on the first team");
        }

        [TestMethod]
        public async Task AddingSamePlayerToDifferentTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Couldn't add player to team");

            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players");
            PlayerTeamPair pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should be known");
            Assert.AreEqual(FirstTeam, pair.TeamId, "Player should be on the first team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, SecondTeam),
                "Should be able to add the same player to the same team (as a no-op)");
            pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players after the second add");
            pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should still be known");
            Assert.AreEqual(SecondTeam, pair.TeamId, "Player should be on the second team");
        }

        [TestMethod]
        public async Task RemovePlayerFromTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Couldn't add player to team");
            Assert.IsTrue(teamManager.TryRemovePlayerFromTeam(FirstPlayerId), "Couldn't remove player from team");

            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.IsFalse(pairs.Any(), "There should be no players, but some were found");
        }

        [TestMethod]
        public async Task CannotRemoveNonexistentPlayerFromTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Couldn't add player to team");
            Assert.IsFalse(teamManager.TryRemovePlayerFromTeam(SecondPlayerId), "Second player shouldn't be removable");

            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players");
            PlayerTeamPair pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should be known");
            Assert.AreEqual(FirstTeam, pair.TeamId, "Player should be on the first team");
        }

        [TestMethod]
        public async Task TryAddTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(teamIdToName.TryGetValue(FirstTeam, out string teamName), "Couldn't find team after adding it");
            Assert.AreEqual(FirstTeam, teamName, "Unexpected team name");
        }

        [TestMethod]
        public async Task TryReaddTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(teamIdToName.TryGetValue(FirstTeam, out string teamName), "Couldn't find team after adding it");
            Assert.AreEqual(FirstTeam, teamName, "Unexpected team name");

            Assert.IsFalse(
                teamManager.TryAddTeam(FirstTeam, out string errorMessage),
                "Re-adding a team should fail");
            Assert.AreEqual(
                $@"Team ""{FirstTeam}"" already exists.", errorMessage);
        }

        [TestMethod]
        public async Task AddingPlayerToRemovedTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(teamIdToName.ContainsKey(FirstTeam), "Couldn't find team after adding it");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam),
                "Couldn't add player to team");
            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(1, pairs.Count(), "Unexpected number of players");
            PlayerTeamPair pair = pairs.First();
            Assert.AreEqual(FirstPlayerId, pair.PlayerId, "Player should be known");
            Assert.AreEqual(FirstTeam, pair.TeamId, "Player should be on the first team");
        }

        [TestMethod]
        public async Task AddRemoveAndReaddTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add team");

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(teamIdToName.TryGetValue(FirstTeam, out string teamName), "Couldn't find team after adding it");
            Assert.AreEqual(FirstTeam, teamName, "Unexpected team name");

            Assert.IsTrue(teamManager.TryRemoveTeam(FirstTeam, out _), "Couldn't remove team");
            teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Remove didn't remove the team");

            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Re-adding a team should succeed");
            teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.IsTrue(
                teamIdToName.TryGetValue(FirstTeam, out teamName), "Couldn't find team after re-adding it");
            Assert.AreEqual(FirstTeam, teamName, "Unexpected team name after re-addition");
        }

        [TestMethod]
        public void CannotRemoveNonexistentTeam()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsFalse(teamManager.TryRemoveTeam(FirstTeam, out string errorMessage), "Remove should've failed");
            Assert.AreEqual(
                $@"Cannot remove team ""{FirstTeam}"" because it's not in the current game.",
                errorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task GetTeamIdOfPlayer()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam), "Couldn't add the player");
            Assert.AreEqual(
                FirstTeam, await teamManager.GetTeamIdOrNull(FirstPlayerId), "Unexpected team ID");
        }

        [TestMethod]
        public async Task GetTeamIdOfNonexistentPlayer()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam), "Couldn't add the player");
            Assert.IsNull(await teamManager.GetTeamIdOrNull(SecondPlayerId), "Unexpected team ID");
        }

        [TestMethod]
        public async Task GetTeamIdOfPlayerAfterRemoval()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the team");
            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam), "Couldn't add the player");
            Assert.IsTrue(
                teamManager.TryRemovePlayerFromTeam(FirstPlayerId), "Couldn't remove the player");
            Assert.IsNull(await teamManager.GetTeamIdOrNull(FirstPlayerId), "Unexpected team ID");
        }

        [TestMethod]
        public async Task AddMultipleTeams()
        {
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(
                teamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(FirstPlayerId, FirstTeam), "Couldn't add the first player");
            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(SecondPlayerId, SecondTeam),
                "Couldn't add the second player");

            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(2, pairs.Count(), "Unexpected number of known players");

            PlayerTeamPair firstPair = pairs.FirstOrDefault(pair => pair.PlayerId == FirstPlayerId);
            Assert.IsNotNull(firstPair, "Couldn't find the first player");
            Assert.AreEqual(FirstTeam, firstPair.TeamId, "First player has the wrong team");

            PlayerTeamPair secondPair = pairs.FirstOrDefault(pair => pair.PlayerId == SecondPlayerId);
            Assert.IsNotNull(secondPair, "Couldn't find the second player");
            Assert.AreEqual(SecondTeam, secondPair.TeamId, "First player has the wrong team");
        }
    }
}
