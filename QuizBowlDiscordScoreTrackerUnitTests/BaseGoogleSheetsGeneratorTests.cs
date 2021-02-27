using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Sheets.v4.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    public abstract class BaseGoogleSheetsGeneratorTests
    {
        protected static readonly Uri SheetsUri = new Uri("http://localhost/sheets/sheetsId/");

        protected const string FirstTeam = "Alpha";
        protected const string SecondTeam = "Beta";

        protected List<string> ClearedRanges { get; private set; }

        protected BaseGoogleSheetsGenerator Generator { get; set; }

        protected List<UpdateRange> UpdatedRanges { get; private set; }

        [TestInitialize]
        public void InitializeTest()
        {
            // Clear out the old fields
            this.UpdatedRanges = new List<UpdateRange>();
            this.ClearedRanges = new List<string>();

            IGoogleSheetsApi googleSheetsApi = this.CreateGoogleSheetsApi();
            this.Generator = this.CreateGenerator(googleSheetsApi);
        }

        [TestMethod]
        public async Task SetRostersFailsWithTooManyPlayers()
        {
            List<PlayerTeamPair> players = new List<PlayerTeamPair>();
            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                players.Add(new PlayerTeamPair((ulong)i, $"{i}", FirstTeam));
            }

            IByRoleTeamManager teamManager = CreateTeamManager(players.ToArray());

            IResult<string> result = await this.Generator.TryUpdateRosters(teamManager, SheetsUri);
            Assert.IsTrue(result.Success, $"Update should've succeeded at the limit.");

            players.Add(new PlayerTeamPair(1111, "OverLimit", FirstTeam));
            teamManager = CreateTeamManager(players.ToArray());

            result = await this.Generator.TryUpdateRosters(teamManager, SheetsUri);
            Assert.IsFalse(result.Success, $"Update should've failed after the limit.");
            Assert.AreEqual(
                $"Couldn't write to the sheet. Rosters can only support up to {this.Generator.PlayersPerTeamLimit} players per team.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetPastPlayerLimitFails()
        {
            List<PlayerTeamPair> players = new List<PlayerTeamPair>();
            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                players.Add(new PlayerTeamPair((ulong)i + 2, $"Player{i}", FirstTeam));
                players.Add(new PlayerTeamPair((ulong)i + 200, $"Player{i}", SecondTeam));
            }

            IByRoleTeamManager teamManager = CreateTeamManager(players.ToArray());

            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            players.Add(new PlayerTeamPair(1111, "OverLimit", FirstTeam));
            teamManager = CreateTeamManager(players.ToArray());
            game.TeamManager = teamManager;

            // We need to force an update to the game, so we don't have cached stats
            await game.AddPlayer(2, "Player2");
            game.ScorePlayer(0);

            result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsFalse(result.Success, $"Creation should've failed after the limit.");
            Assert.AreEqual(
                $"Couldn't write to the sheet. Export only currently works if there are at most {this.Generator.PlayersPerTeamLimit} players on a team.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithoutTeamsFails()
        {
            IByRoleTeamManager teamManager = CreateTeamManager();

            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(10);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsFalse(result.Success, $"Creation succeeded when it should've failed.");
            Assert.AreEqual(
                "Couldn't write to the sheet. Export only works if there are 1 or 2 teams in the game.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithMoreThanTwoTeamsFails()
        {
            PlayerTeamPair[] players = new PlayerTeamPair[3];
            for (int i = 0; i < players.Length; i++)
            {
                players[i] = new PlayerTeamPair((ulong)i + 100, $"Player{i}", $"Team{i}");
            }

            IByRoleTeamManager teamManager = CreateTeamManager(players);

            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(10);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsFalse(result.Success, $"Creation succeeded when it should've failed.");
            Assert.AreEqual(
                "Couldn't write to the sheet. Export only works if there are 1 or 2 teams in the game.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        protected abstract BaseGoogleSheetsGenerator CreateGenerator(IGoogleSheetsApi sheetsApi);

        protected static IByRoleTeamManager CreateDefaultTeamManager()
        {
            return CreateTeamManager(
                new PlayerTeamPair(2, "Alice", FirstTeam),
                new PlayerTeamPair(3, "Alan", FirstTeam),
                new PlayerTeamPair(4, "Bob", SecondTeam));
        }

        protected static IByRoleTeamManager CreateTeamManager(params PlayerTeamPair[] playerTeamPairs)
        {
            IEnumerable<IGrouping<string, PlayerTeamPair>> grouping = playerTeamPairs.GroupBy(pair => pair.TeamId);
            IReadOnlyDictionary<string, string> teamIdToNames = playerTeamPairs
                .Select(pair => pair.TeamId)
                .Distinct()
                .ToDictionary(key => key);

            Mock<IByRoleTeamManager> mockTeamManager = new Mock<IByRoleTeamManager>();
            mockTeamManager
                .Setup(manager => manager.GetTeamIdToNamesForServer())
                .Returns(Task.FromResult(teamIdToNames));
            mockTeamManager
                .Setup(manager => manager.GetPlayerTeamPairsForServer())
                .Returns(Task.FromResult(grouping));

            mockTeamManager
                .Setup(manager => manager.GetTeamIdToNames())
                .Returns(Task.FromResult(teamIdToNames));
            mockTeamManager
                .Setup(manager => manager.GetKnownPlayers())
                .Returns(Task.FromResult<IEnumerable<PlayerTeamPair>>(playerTeamPairs));

            mockTeamManager
                .Setup(manager => manager.GetTeamIdOrNull(It.IsAny<ulong>()))
                .Returns<ulong>((userId) =>
                {
                    PlayerTeamPair player = playerTeamPairs.FirstOrDefault(player => player.PlayerId == userId);
                    return Task.FromResult(
                        player != null && teamIdToNames.TryGetValue(player.TeamId, out string teamId) ?
                            teamId :
                            (string)null);
                });

            return mockTeamManager.Object;
        }

        protected void AssertInUpdateRange(string range, string value, string message)
        {
            Assert.IsTrue(
                this.UpdatedRanges
                    .Any(valueRange => valueRange.Range == range && valueRange.Values.Any(v => v.ToString() == value)),
                message);
        }

        protected IGoogleSheetsApi CreateGoogleSheetsApi()
        {
            Mock<IGoogleSheetsApi> mockGoogleSheetsApi = new Mock<IGoogleSheetsApi>();
            mockGoogleSheetsApi
                .Setup(api => api.UpdateGoogleSheet(It.IsAny<List<ValueRange>>(), It.IsAny<List<string>>(), It.IsAny<Uri>()))
                .Returns<List<ValueRange>, List<string>, Uri>((updateRanges, clearRanges, sheetsUri) =>
                {
                    this.UpdatedRanges.AddRange(updateRanges.Select(range =>
                        new UpdateRange()
                        {
                            Range = range.Range,
                            Values = range.Values.Count > 0 ? range.Values[0] : new List<object>()
                        }));
                    this.ClearedRanges.AddRange(clearRanges);
                    return Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty));
                });
            return mockGoogleSheetsApi.Object;
        }

        protected class UpdateRange
        {
            public string Range { get; set; }


            [SuppressMessage(
                "Usage",
                "CA2227:Collection properties should be read only",
                Justification = "Only used to set the values in a property-initializer constructor")]
            public IList<object> Values { get; set; }
        }
    }
}
