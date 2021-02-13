using System;
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
        private const string DefaultTeamRolePrefix = "Team";
        private const ulong EveryoneRoleId = 3303;
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
        public async Task GetTeamIdToNamesNoOverwritePermissions()
        {
            ByRoleTeamManager teamManager = CreateTeamManager();
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyAllTeamsInTeamIdToName(teamIdToName);
        }

        [TestMethod]
        public async Task GetTeamIdToNamesNoRolesEveryoneDeniedView()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                (OverwritePermissions?)null :
                new OverwritePermissions(viewChannel: PermValue.Deny));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesNoRolesEveryoneDeniedSend()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                (OverwritePermissions?)null :
                new OverwritePermissions(sendMessages: PermValue.Deny));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesNoRolesEveryoneViewOnly()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                (OverwritePermissions?)null :
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Deny));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesNoRolesEveryoneSendOnly()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                (OverwritePermissions?)null :
                new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Allow));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesNoRolesEveryoneAllowed()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                (OverwritePermissions?)null :
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyAllTeamsInTeamIdToName(teamIdToName);
        }

        [TestMethod]
        public async Task GetTeamIdToNamesRolesInheritEveryoneDeniedSend()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
            OverwritePermissions.InheritAll :
            new OverwritePermissions(sendMessages: PermValue.Deny));
        IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesRolesInheritEveryoneViewOnly()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                OverwritePermissions.InheritAll :
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Deny));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesRolesInheritEveryoneSendOnly()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                OverwritePermissions.InheritAll :
                new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Allow));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesRolesInheritEveryoneAllowed()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) => roleId != EveryoneRoleId ?
                OverwritePermissions.InheritAll :
                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyAllTeamsInTeamIdToName(teamIdToName);
        }

        [TestMethod]
        public async Task GetTeamIdToNamesFirstAllowsSecondInheritsEveryoneAllowed()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) =>
            {
                if (roleId == RoleIds[0] || roleId == EveryoneRoleId)
                {
                    return new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow);
                }

                return OverwritePermissions.InheritAll;
            });

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyAllTeamsInTeamIdToName(teamIdToName);
        }

        [TestMethod]
        public async Task GetTeamIdToNamesFirstAllowsSecondInheritsEveryoneDenied()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) =>
            {
                if (roleId == RoleIds[0])
                {
                    return new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow);
                }
                else if (roleId == EveryoneRoleId)
                {
                    return new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Deny);
                }

                return OverwritePermissions.InheritAll;
            });

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyOnlyOneTeamInTeamIdToName(teamIdToName);
        }

        [TestMethod]
        public async Task GetTeamIdToNamesFirstAllowsSecondDeniedEveryoneAllowed()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) =>
            {
                if (roleId == RoleIds[0] || roleId == EveryoneRoleId)
                {
                    return new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow);
                }

                return new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Deny);
            });

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyOnlyOneTeamInTeamIdToName(teamIdToName);
        }

        [TestMethod]
        public async Task GetTeamIdToNamesFirstPartialSecondInheritsEveryoneDenied()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) =>
            {
                if (roleId == RoleIds[0])
                {
                    return new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Inherit);
                }
                else if (roleId == EveryoneRoleId)
                {
                    return new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Deny);
                }

                return OverwritePermissions.InheritAll;
            });

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            Assert.AreEqual(0, teamIdToName.Count, "Unexpected number of teams");
        }

        [TestMethod]
        public async Task GetTeamIdToNamesFirstInheritsSecondAllowsEveryoneDenied()
        {
            ByRoleTeamManager teamManager = CreateTeamManager((roleId) =>
            {
                if (roleId == RoleIds[1])
                {
                    return new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow);
                }
                else if (roleId == EveryoneRoleId)
                {
                    return new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Deny);
                }

                return OverwritePermissions.InheritAll;
            });

            IReadOnlyDictionary<string, string> teamIdToName = await teamManager.GetTeamIdToNames();
            VerifyOnlyOneTeamInTeamIdToName(teamIdToName, 1);
        }

        [TestMethod]
        public async Task ReloadTeamRoles()
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

            mockGuild
                .Setup(guild => guild.Roles)
                .Returns(roles);

            bool newRolesAssigned = false;
            List<IGuildUser> users = new List<IGuildUser>();
            for (int i = 0; i < PlayerIds.Length; i++)
            {
                Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
                mockUser.Setup(user => user.Id).Returns(PlayerIds[i]);
                mockUser.Setup(user => user.Nickname).Returns($"User_{PlayerIds[i]}");

                // Make a copy so that we don't use the value of i after it's done in the loop in the return function's
                // closure
                int index = i;
                mockUser.Setup(user => user.RoleIds).Returns(() =>
                {
                    if (newRolesAssigned || index % 2 == 0)
                    {
                        return new ulong[] { RoleIds[index] };
                    }

                    return Array.Empty<ulong>();
                });
                users.Add(mockUser.Object);
            }

            IReadOnlyCollection<IGuildUser> readonlyUsers = users;
            mockGuild
                .Setup(guild => guild.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<CacheMode, RequestOptions>((mode, options) => Task.FromResult(readonlyUsers));
            mockGuild
                .Setup(guild => guild.GetUserAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<ulong, CacheMode, RequestOptions>((id, mode, options) => Task.FromResult(users.FirstOrDefault(user => user.Id == id)));

            Mock<IGuildChannel> mockGuildChannel = new Mock<IGuildChannel>();
            mockGuildChannel
                .Setup(channel => channel.GetPermissionOverwrite(It.IsAny<IRole>()))
                .Returns<IRole>(null);
            mockGuildChannel
                .SetupGet(channel => channel.Guild)
                .Returns(mockGuild.Object);

            ByRoleTeamManager teamManager = new ByRoleTeamManager(mockGuildChannel.Object, DefaultTeamRolePrefix);

            IEnumerable<PlayerTeamPair> players = await teamManager.GetKnownPlayers();
            Assert.AreEqual(PlayerIds.Length / 2, players.Count(), "Unexpected number of players the first time");
            Assert.AreEqual(PlayerIds[0], players.First().PlayerId, "Unexpected player in the first set of players");

            newRolesAssigned = true;
            string message = teamManager.ReloadTeamRoles();
            Assert.IsNotNull(message, "Message to report shouldn't be null");
            Assert.AreEqual(PlayerIds.Length, players.Count(), "Unexpected number of players the second time");
            Assert.IsTrue(players.Any(player => player.PlayerId == PlayerIds[0]), "First player isn't in the list of teams");
            Assert.IsTrue(players.Any(player => player.PlayerId == PlayerIds[1]), "Second player isn't in the list of teams");
        }

        private static ByRoleTeamManager CreateTeamManager(
            Func<ulong, OverwritePermissions?> channelOverwritePermissions = null)
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

            IReadOnlyCollection<IGuildUser> readonlyUsers = users;
            mockGuild
                .Setup(guild => guild.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<CacheMode, RequestOptions>((mode, options) => Task.FromResult(readonlyUsers));
            mockGuild
                .Setup(guild => guild.GetUserAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<ulong, CacheMode, RequestOptions>((id, mode, options) => Task.FromResult(users.FirstOrDefault(user => user.Id == id)));

            Mock<IRole> mockEveryoneRole = new Mock<IRole>();
            mockEveryoneRole.SetupGet(role => role.Id).Returns(EveryoneRoleId);
            mockGuild
                .SetupGet(guild => guild.EveryoneRole)
                .Returns(mockEveryoneRole.Object);

            Mock<IGuildChannel> mockGuildChannel = new Mock<IGuildChannel>();
            mockGuildChannel
                .Setup(channel => channel.GetPermissionOverwrite(It.IsAny<IRole>()))
                .Returns<IRole>(role => channelOverwritePermissions?.Invoke(role.Id));
            mockGuildChannel
                .SetupGet(channel => channel.Guild)
                .Returns(mockGuild.Object);

            return new ByRoleTeamManager(mockGuildChannel.Object, DefaultTeamRolePrefix);
        }

        private static void VerifyAllTeamsInTeamIdToName(IReadOnlyDictionary<string, string> teamIdToName)
        {
            Assert.AreEqual(RoleIds.Length, teamIdToName.Count, "Unexpected number of teams");

            for (int i = 0; i < RoleIds.Length; i++)
            {
                Assert.IsTrue(
                    teamIdToName.TryGetValue(RoleIds[i].ToString(CultureInfo.InvariantCulture), out string teamName),
                    $"Couldn't get the team name for role ID {RoleIds[i]}");
                Assert.AreEqual(TeamNames[i], teamName, $"Unexpected team name for team #{i + 1}");
            }
        }

        private static void VerifyOnlyOneTeamInTeamIdToName(
            IReadOnlyDictionary<string, string> teamIdToName, int teamIndex = 0)
        {
            Assert.AreEqual(1, teamIdToName.Count, "Unexpected number of teams");

            int teamNumber = teamIndex + 1;
            Assert.IsTrue(
                teamIdToName.TryGetValue(RoleIds[teamIndex].ToString(CultureInfo.InvariantCulture), out string teamName),
                $"Couldn't get the team name for role #{teamNumber}");
            Assert.AreEqual(TeamNames[teamIndex], teamName, $"Unexpected team name for team #{teamNumber}");
        }
    }
}
