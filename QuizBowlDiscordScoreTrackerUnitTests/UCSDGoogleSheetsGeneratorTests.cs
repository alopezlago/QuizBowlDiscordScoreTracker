using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class UCSDGoogleSheetsGeneratorTests : BaseGoogleSheetsGeneratorTests
    {
        protected override BaseGoogleSheetsGenerator CreateGenerator(IGoogleSheetsApi sheetsApi)
        {
            return new UCSDGoogleSheetsGenerator(sheetsApi);
        }

        [TestMethod]
        public async Task SetRostersSucceeds()
        {
            IByRoleTeamManager teamManager = CreateDefaultTeamManager();
            await ValidateDefaultRostersSet(teamManager);
        }

        [TestMethod]
        public async Task TryCreateScoresheetSucceeds()
        {
            // Do something simple, then read the spreadsheet and verify some fields
            IByRoleTeamManager teamManager = CreateDefaultTeamManager();

            // Do something simple, then read the spreadsheet and verify some fields
            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);
            game.TryScoreBonus("10/0/10");

            await game.AddPlayer(3, "Alan");
            game.ScorePlayer(-5);
            await game.AddPlayer(4, "Bob");
            game.ScorePlayer(10);
            game.TryScoreBonus("0/10/0");

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Failed: {(result.Success ? "" : result.ErrorMessage)}");

            // Assert we cleared these two fields
            Assert.IsTrue(this.ClearedRanges.Contains("'Round 1'!C4:H31"), "First team scores are not in the list of cleared ranges");
            Assert.IsTrue(this.ClearedRanges.Contains("'Round 1'!O4:T31"), "Second team scores are not in the list of cleared ranges");

            // Assert we cleared the second team and the players
            Assert.IsTrue(this.ClearedRanges.Contains("'Round 1'!O1:O1"), "Second team name wasn't cleared");
            Assert.IsTrue(this.ClearedRanges.Contains("'Round 1'!C3:H3"), "First team's player names weren't cleared");
            Assert.IsTrue(this.ClearedRanges.Contains("'Round 1'!O3:T3"), "Second team's player names weren't cleared");

            // These checks are O(n^2), since we do Any for all of them. However, n is small here (~15), so it's not
            // too bad.

            // Team names
            this.AssertInUpdateRange("'Round 1'!C1", FirstTeam, "Couldn't find first team");
            this.AssertInUpdateRange("'Round 1'!O1", SecondTeam, "Couldn't find second team");

            // Player names
            this.AssertInUpdateRange("'Round 1'!C3", "Alice", "Couldn't find Alice");
            this.AssertInUpdateRange("'Round 1'!D3", "Alan", "Couldn't find Alan");
            this.AssertInUpdateRange("'Round 1'!O3", "Bob", "Couldn't find Bob");

            // Tossups
            this.AssertInUpdateRange("'Round 1'!C4", "15", "Couldn't find Alice's buzz");
            this.AssertInUpdateRange("'Round 1'!D5", "-5", "Couldn't find Alan's buzz");
            this.AssertInUpdateRange("'Round 1'!O5", "10", "Couldn't find Bob's buzz");

            // Bonuses
            UpdateRange updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == "'Round 1'!I4:K4");
            Assert.IsNotNull(updateRange, "Couldn't find range for the first bonus");
            CollectionAssert.AreEquivalent(
                new bool[] { true, false, true },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the first bonus");

            updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == "'Round 1'!U5:W5");
            Assert.IsNotNull(updateRange, "Couldn't find range for the second bonus");
            CollectionAssert.AreEquivalent(
                new bool[] { false, true, false },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the second bonus");
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtPlayerLimitSucceeds()
        {
            List<PlayerTeamPair> players = new List<PlayerTeamPair>();
            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                players.Add(new PlayerTeamPair((ulong)i + 2, $"FirstPlayer{i}", FirstTeam));
                players.Add(new PlayerTeamPair((ulong)i + 200, $"SecondPlayer{i}", SecondTeam));
            }

            ITeamManager teamManager = CreateTeamManager(players.ToArray());

            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            SpreadsheetColumn firstTeamColumn = this.Generator.StartingColumns.Span[0];
            SpreadsheetColumn secondTeamColumn = this.Generator.StartingColumns.Span[1];
            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                this.AssertInUpdateRange(
                    $"'Round 1'!{firstTeamColumn}3",
                    $"FirstPlayer{i}",
                    $"Couldn't find the player { i + 1 } on the first team");
                this.AssertInUpdateRange(
                    $"'Round 1'!{secondTeamColumn}3",
                    $"SecondPlayer{i}",
                    $"Couldn't find player {i + 1} on the second team");
                firstTeamColumn = firstTeamColumn + 1;
                secondTeamColumn = secondTeamColumn + 1;
            }
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtPhasesLimitSucceeds()
        {
            IByRoleTeamManager teamManager = CreateDefaultTeamManager();

            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            for (int i = 0; i < this.Generator.PhasesLimit - 1; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
                Assert.IsTrue(game.TryScoreBonus("0"), $"Scoring a bonus should've succeeded in phase {i}");
            }

            await game.AddPlayer(4, "Bob");
            game.ScorePlayer(-5);
            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            int lastRow = this.Generator.FirstPhaseRow + this.Generator.PhasesLimit - 1;
            this.AssertInUpdateRange($"'Round 1'!C{lastRow}", "15", "Couldn't find Alice's buzz");
            this.AssertInUpdateRange($"'Round 1'!O{lastRow}", "-5", "Couldn't find Bob's buzz");
        }

        [TestMethod]
        public async Task NoBonusesWrittenAfterBonusLimit()
        {
            IByRoleTeamManager teamManager = CreateDefaultTeamManager();

            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            int lastBonusPhase = this.Generator.LastBonusRow - this.Generator.FirstPhaseRow;
            for (int i = 0; i < lastBonusPhase; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
                Assert.IsTrue(game.TryScoreBonus("0"), $"Scoring a bonus should've succeeded in phase {i}");
            }

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);
            Assert.IsTrue(game.TryScoreBonus("30"), $"Scoring a bonus should've succeeded in the last phase");

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            UpdateRange updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == $"'Round 1'!I{this.Generator.LastBonusRow}:K{this.Generator.LastBonusRow}");
            Assert.IsNotNull(updateRange, "Couldn't find range for the last bonus");
            CollectionAssert.AreEquivalent(
                new bool[] { true, true, true },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the last bonus");

            updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == $"'Round 1'!I{this.Generator.LastBonusRow + 1}:K{this.Generator.LastBonusRow + 1}");
            Assert.IsNull(updateRange, "Bonus past the last bonus phase should'nt be exported");
        }

        [TestMethod]
        public async Task TryCreateScoresheetOneTeamAndIndividual()
        {
            const string firstTeamPlayer = "Alice";
            const string individualName = "Individual";

            IByRoleTeamManager teamManager = CreateTeamManager(new PlayerTeamPair(2, firstTeamPlayer, FirstTeam));

            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            await game.AddPlayer(2, firstTeamPlayer);
            game.ScorePlayer(10);
            await game.AddPlayer(3, individualName);
            game.ScorePlayer(15);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded");

            // n^2 runtime, but not that many checks or update ranges, so it should be okay

            this.AssertInUpdateRange($"'Round 1'!C1", FirstTeam, "Couldn't find the first team's name");
            this.AssertInUpdateRange($"'Round 1'!O1", individualName, "Couldn't find the second team's name");
            this.AssertInUpdateRange($"'Round 1'!C3", firstTeamPlayer, "Couldn't find the first team's player name");
            this.AssertInUpdateRange($"'Round 1'!O3", individualName, "Couldn't find the indivual team's player name");
            this.AssertInUpdateRange($"'Round 1'!C4", "10", "Couldn't find the buzz from the first team");
            this.AssertInUpdateRange($"'Round 1'!O5", "15", "Couldn't find the buzz from the individual team");
        }

        [TestMethod]
        public async Task TryCreateScoresheetPastPhaseLimitFails()
        {
            IByRoleTeamManager teamManager = CreateDefaultTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            for (int i = 0; i < this.Generator.PhasesLimit; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
            }

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);

            result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded.");
            this.AssertInUpdateRange($"'Round 1'!C27", "10", "Couldn't find the last buzz");
            Assert.IsFalse(
                this.UpdatedRanges
                    .Any(valueRange => valueRange.Values.Any(v => v.ToString() == "15")),
                "Last buzz should've been cut off, but we found a power");
        }

        [TestMethod]
        public async Task SetRostersWithNoRolesInChannelSucceeds()
        {
            PlayerTeamPair[] playerTeamPairs = new PlayerTeamPair[]
            {
                new PlayerTeamPair(1, "Alice", FirstTeam),
                new PlayerTeamPair(2, "Alan", FirstTeam),
                new PlayerTeamPair(3, "Bob", SecondTeam)
            };
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
                .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
            mockTeamManager
                .Setup(manager => manager.GetKnownPlayers())
                .Returns(Task.FromResult(Enumerable.Empty<PlayerTeamPair>()));

            IByRoleTeamManager teamManager = mockTeamManager.Object;
            await ValidateDefaultRostersSet(teamManager);
        }

        private async Task ValidateDefaultRostersSet(IByRoleTeamManager teamManager)
        {
            IResult<string> result = await this.Generator.TryUpdateRosters(teamManager, SheetsUri);
            Assert.IsTrue(result.Success, "Update should've succeeded");

            Assert.AreEqual(1, this.ClearedRanges.Count, "Unexpected number of clears");
            Assert.AreEqual($"'{UCSDGoogleSheetsGenerator.RostersSheetName}'!A2:G999", this.ClearedRanges[0], "Unexpected range");

            Assert.AreEqual(2, this.UpdatedRanges.Count, "Unexpected number of update ranges");

            UpdateRange updateRange = this.UpdatedRanges[0];
            Assert.AreEqual(
                $"'{UCSDGoogleSheetsGenerator.RostersSheetName}'!A2:C2",
                updateRange.Range,
                "Unexpected range for the first team");
            CollectionAssert.AreEquivalent(
                new string[] { FirstTeam, "Alice", "Alan" },
                updateRange.Values.ToArray(),
                "Unexpected row for the first team");

            updateRange = this.UpdatedRanges[1];
            Assert.AreEqual(
                $"'{UCSDGoogleSheetsGenerator.RostersSheetName}'!A3:B3",
                updateRange.Range,
                "Unexpected range for the second team");
            CollectionAssert.AreEquivalent(
                new string[] { SecondTeam, "Bob" },
                updateRange.Values.ToArray(),
                "Unexpected row for the second team");
        }
    }
}
