using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class ByRoleTeamManagerTests
    {
        // GetTeamIdToNames
        private const string DefaultTeamRolePrefix = "Team";
        private const ulong NonexistentPlayerId = 12;
        private const ulong NonexistentUserId = 1212;
        private const ulong NonTeamRoleId = 34;
        private const string NonTeamRoleName = "SomeRole";

        private static readonly ulong[] PlayerIds = new ulong[] { 1, 2 };
        private static readonly ulong[] RoleIds = new ulong[] { 3, 4 };
        private static readonly string[] TeamNames = new string[]
        {
            "Alpha",
            "Beta"
        };
        private static readonly string[] RoleNames = TeamNames
            .Select(teamName => $"{DefaultTeamRolePrefix} {teamName}")
            .ToArray();

        [TestMethod]
        public async Task GetTeamIdOfPlayer()
        {
            ByRoleTeamManager teamManager = CreateTeamManager();
            for (int i = 0; i < PlayerIds.Length; i++)
            {
                Assert.AreEqual(
                    RoleIds[i].ToString(CultureInfo.InvariantCulture),
                    await teamManager.GetTeamIdOrNull(PlayerIds[i]),
                    $"Unexpected team ID for player {i}");
            }
        }

        [TestMethod]
        public async Task GetTeamIdOfNonexistentPlayer()
        {
            ByRoleTeamManager teamManager = CreateTeamManager();
            Assert.IsNull(await teamManager.GetTeamIdOrNull(NonexistentPlayerId), "Unexpected team ID");
        }

        [TestMethod]
        public async Task GetTeamIdOfNonexistentUser()
        {
            ByRoleTeamManager teamManager = CreateTeamManager();
            Assert.IsNull(await teamManager.GetTeamIdOrNull(NonexistentUserId), "Unexpected team ID");
        }

        [TestMethod]
        public async Task GetKnownPlayers()
        {
            ByRoleTeamManager teamManager = CreateTeamManager();
            IEnumerable<PlayerTeamPair> pairs = await teamManager.GetKnownPlayers();
            Assert.AreEqual(PlayerIds.Length, pairs.Count(), "Unexpected number of players");

            for (int i = 0; i < PlayerIds.Length; i++)
            {
                PlayerTeamPair pair = pairs.FirstOrDefault(pair => pair.PlayerId == PlayerIds[i]);
                Assert.IsNotNull(pair, $"Couldn't find the player #{i + 1}");
                Assert.AreEqual(
                    RoleIds[i].ToString(CultureInfo.InvariantCulture),
                    pair.TeamId,
                    $"Unexpected team ID for player #{i + 1}");
            }
        }

        [TestMethod]
        public async Task GetTeamIdToNames()
        {
            ByRoleTeamManager teamManager = CreateTeamManager();
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(RoleIds.Length, teamIdToName.Count, "Unexpected number of teams");

            for (int i = 0; i < RoleIds.Length; i++)
            {
                Assert.IsTrue(
                    teamIdToName.TryGetValue(RoleIds[i].ToString(CultureInfo.InvariantCulture), out string teamName),
                    $"Couldn't get the team name for role ID {RoleIds[i]}");
                Assert.AreEqual(TeamNames[i], teamName, $"Unexpected team name for team #{i + 1}");
            }
        }

        private static ByRoleTeamManager CreateTeamManager()
        {
            Mock<IGuild> mockGuild = new Mock<IGuild>();
            List<IRole> roles = new List<IRole>();
            for (int i = 0; i < RoleIds.Length; i++)
            {
                Mock<IRole> mockRole = new Mock<IRole>();
                mockRole.Setup(role => role.Id).Returns(RoleIds[i]);
                mockRole.Setup(role => role.Name).Returns(RoleNames[i]);
                roles.Add(mockRole.Object);
            }

            Mock<IRole> mockNonTeamRole = new Mock<IRole>();
            mockNonTeamRole.Setup(role => role.Id).Returns(NonTeamRoleId);
            mockNonTeamRole.Setup(role => role.Name).Returns(NonTeamRoleName);
            roles.Add(mockNonTeamRole.Object);

            mockGuild
                .Setup(guild => guild.Roles)
                .Returns(roles);

            List<IGuildUser> users = new List<IGuildUser>();
            for (int i = 0; i < PlayerIds.Length; i++)
            {
                Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
                mockUser.Setup(user => user.Id).Returns(PlayerIds[i]);
                mockUser.Setup(user => user.Nickname).Returns($"User_{PlayerIds[i]}");
                mockUser.Setup(user => user.RoleIds).Returns(new ulong[] { RoleIds[i] });
                users.Add(mockUser.Object);
            }

            Mock<IGuildUser> mockNonexistentUser = new Mock<IGuildUser>();
            mockNonexistentUser.Setup(user => user.Id).Returns(NonexistentPlayerId);
            mockNonexistentUser.Setup(user => user.Nickname).Returns($"User_{NonexistentPlayerId}");
            mockNonexistentUser.Setup(user => user.RoleIds).Returns(new ulong[] { NonTeamRoleId });
            users.Add(mockNonexistentUser.Object);

            IReadOnlyCollection<IGuildUser> readonlyUsers = (IReadOnlyCollection<IGuildUser>)users;
            mockGuild
                .Setup(guild => guild.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<CacheMode, RequestOptions>((mode, options) => Task.FromResult(readonlyUsers));
            mockGuild
                .Setup(guild => guild.GetUserAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<ulong, CacheMode, RequestOptions>((id, mode, options) => Task.FromResult(users.FirstOrDefault(user => user.Id == id)));

            return new ByRoleTeamManager(mockGuild.Object, DefaultTeamRolePrefix);
        }
    }
}
