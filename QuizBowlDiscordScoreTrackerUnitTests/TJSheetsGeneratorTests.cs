using System;
using System.Collections.Generic;
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
    [TestClass]
    public class TJheetsGeneratorTests
    {
        private const string FirstTeam = "Alpha";
        private const string SecondTeam = "Beta";

        private static readonly Uri SheetsUri = new Uri("http://localhost/sheets/sheetsId/");

        private List<string> ClearedRanges { get; set; }

        private TJSheetsGenerator Generator { get; set; }

        private ByCommandTeamManager TeamManager { get; set; }

        private List<UpdateRange> UpdatedRanges { get; set; }

        [TestInitialize]
        public void InitializeTest()
        {
            // Clear out the old fields
            this.UpdatedRanges = new List<UpdateRange>();
            this.ClearedRanges = new List<string>();
            this.TeamManager = new ByCommandTeamManager();

            IGoogleSheetsApi googleSheetsApi = this.CreateGoogleSheetsApi();
            this.Generator = new TJSheetsGenerator(googleSheetsApi);
        }

        [TestMethod]
        public async Task SetRostersSucceeds()
        {
            this.TeamManager.TryAddTeam(FirstTeam, out _);
            this.TeamManager.TryAddTeam(SecondTeam, out _);
            this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(3, "Alan", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(4, "Bob", SecondTeam);

            IResult<string> result = await this.Generator.TryUpdateRosters(this.TeamManager, SheetsUri);
            Assert.IsTrue(result.Success, "Update should've succeeded");

            Assert.AreEqual(1, this.ClearedRanges.Count, "Unexpected number of clears");
            Assert.AreEqual($"'{TJSheetsGenerator.RostersSheetName}'!A1:ZZ21", this.ClearedRanges[0], "Unexpected range");

            Assert.AreEqual(3, this.UpdatedRanges.Count, "Unexpected number of update ranges");

            UpdateRange updateRange = this.UpdatedRanges[0];
            Assert.AreEqual(
                $"'{TJSheetsGenerator.RostersSheetName}'!A1:B1",
                updateRange.Range,
                "Unexpected range for the team names");
            CollectionAssert.AreEquivalent(
                new string[] { FirstTeam, SecondTeam },
                updateRange.Values.ToArray(),
                "Unexpected row for the team names");

            updateRange = this.UpdatedRanges[1];
            Assert.AreEqual(
                $"'{TJSheetsGenerator.RostersSheetName}'!A2:A3",
                updateRange.Range,
                "Unexpected range for the first team's players");
            CollectionAssert.AreEquivalent(
                new string[] { "Alice", "Alan" },
                updateRange.Values.ToArray(),
                "Unexpected row for the first team's players");

            updateRange = this.UpdatedRanges[2];
            Assert.AreEqual(
                $"'{TJSheetsGenerator.RostersSheetName}'!B2:B2",
                updateRange.Range,
                "Unexpected range for the second team's players");
            CollectionAssert.AreEquivalent(
                new string[] { "Bob" },
                updateRange.Values.ToArray(),
                "Unexpected row for the second team's players");
        }

        [TestMethod]
        public async Task SetRostersFailsWithMoreThanSixPlayers()
        {
            this.TeamManager.TryAddTeam(FirstTeam, out _);
            for (int i = 0; i < 6; i++)
            {
                this.TeamManager.TryAddPlayerToTeam((ulong)i, $"{i}", FirstTeam);
            }

            IResult<string> result = await this.Generator.TryUpdateRosters(this.TeamManager, SheetsUri);
            Assert.IsTrue(result.Success, $"Update should've succeeded at the limit.");

            Assert.IsTrue(
                this.TeamManager.TryAddPlayerToTeam(1111, "OverLimit", FirstTeam),
                "Adding the player over the limit should've succeeded");
            result = await this.Generator.TryUpdateRosters(this.TeamManager, SheetsUri);
            Assert.IsFalse(result.Success, $"Update should've failed after the limit.");
            Assert.AreEqual(
                $"Couldn't write to the sheet. Rosters can only support up to {this.Generator.PlayersPerTeamLimit} players per team.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetSucceeds()
        {
            // Do something simple, then read the spreadsheet and verify some fields
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            this.TeamManager.TryAddTeam(FirstTeam, out _);
            this.TeamManager.TryAddTeam(SecondTeam, out _);
            this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(3, "Alan", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(4, "Bob", SecondTeam);

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
            Assert.IsTrue(this.ClearedRanges.Contains("'ROUND 1'!C4:I27"), "First team scores are not in the list of cleared ranges.");
            Assert.IsTrue(this.ClearedRanges.Contains("'ROUND 1'!M4:S27"), "Second team scores are not in the list of cleared ranges.");

            // These checks are O(n^2), since we do Any for all of them. However, n is small here (~15), so it's not
            // too bad.

            // Team names
            this.AssertInUpdateRange("'ROUND 1'!C2", FirstTeam, "Couldn't find first team");
            this.AssertInUpdateRange("'ROUND 1'!M2", SecondTeam, "Couldn't find second team");

            // Player names
            this.AssertInUpdateRange("'ROUND 1'!C3", "Alice", "Couldn't find Alice");
            this.AssertInUpdateRange("'ROUND 1'!D3", "Alan", "Couldn't find Alan");
            this.AssertInUpdateRange("'ROUND 1'!M3", "Bob", "Couldn't find Bob");

            // Tossups
            this.AssertInUpdateRange("'ROUND 1'!C4", "15", "Couldn't find Alice's buzz");
            this.AssertInUpdateRange("'ROUND 1'!D5", "-5", "Couldn't find Alan's buzz");
            this.AssertInUpdateRange("'ROUND 1'!M5", "10", "Couldn't find Bob's buzz");

            // Bonuses
            UpdateRange updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == "'ROUND 1'!I4");
            Assert.IsNotNull(updateRange, "Couldn't find range for the first bonus");
            CollectionAssert.AreEquivalent(
                new int[] { 20 },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the first bonus");

            updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == "'ROUND 1'!S5");
            Assert.IsNotNull(updateRange, "Couldn't find range for the second bonus");
            CollectionAssert.AreEquivalent(
                new int[] { 10 },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the second bonus");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithDeadTossups()
        {
            // Do something simple, then read the spreadsheet and verify some fields
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            this.TeamManager.TryAddTeam(FirstTeam, out _);
            this.TeamManager.TryAddTeam(SecondTeam, out _);
            this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(3, "Alan", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(4, "Bob", SecondTeam);

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(-5);
            game.NextQuestion();
            game.NextQuestion();

            await game.AddPlayer(4, "Bob");
            game.ScorePlayer(10);
            game.TryScoreBonus("0/10/0");

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Failed: {(result.Success ? "" : result.ErrorMessage)}");

            // These checks are O(n^2), since we do Any for all of them. However, n is small here (~15), so it's not
            // too bad.

            // Tossups
            this.AssertInUpdateRange("'ROUND 1'!C4", "-5", "Couldn't find Alice's buzz");
            this.AssertInUpdateRange("'ROUND 1'!I4", "DT", "Couldn't find the first dead tossup marker");
            this.AssertInUpdateRange("'ROUND 1'!I5", "DT", "Couldn't find the second dead tossup marker");

            // Bonuses
            UpdateRange updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == "'ROUND 1'!S6");
            Assert.IsNotNull(updateRange, "Couldn't find range for the bonus");
            CollectionAssert.AreEquivalent(
                new int[] { 10 },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the bonus");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithDeadTossupsInTiebreakers()
        {
            // Do something simple, then read the spreadsheet and verify some fields
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            this.TeamManager.TryAddTeam(FirstTeam, out _);
            this.TeamManager.TryAddTeam(SecondTeam, out _);
            this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(3, "Alan", FirstTeam);
            this.TeamManager.TryAddPlayerToTeam(4, "Bob", SecondTeam);

            for (int i = 0; i < this.Generator.PhasesLimit - 1; i++)
            {
                game.NextQuestion();
            }

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Failed: {(result.Success ? "" : result.ErrorMessage)}");

            // These checks are O(n^2), since we do Any for all of them. However, n is small here (~15), so it's not
            // too bad.

            // Tossups
            // FirstRowPhase is included in the limit, so subtract 1 from what the row should be
            int buzzRow = this.Generator.PhasesLimit + this.Generator.FirstPhaseRow - 1;
            this.AssertInUpdateRange($"'ROUND 1'!C{buzzRow}", "15", "Couldn't find Alice's buzz");
            this.AssertInUpdateRange("'ROUND 1'!I4", "DT", "Couldn't find the first dead tossup marker");
            this.AssertInUpdateRange($"'ROUND 1'!I{this.Generator.LastBonusRow + 1}", "DT", "Couldn't find the tiebreaker dead tossup marker");

            // No bonus in the tie breaker
            UpdateRange updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == $"'ROUND 1'!I{buzzRow}");
            Assert.IsNull(updateRange, "There shouldn't be an update for the bonus yet");
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtPlayerLimitSucceeds()
        {
            GameState game = new GameState()
            {
                Format = Format.TossupShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            Assert.IsTrue(this.TeamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(this.TeamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");

            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                Assert.IsTrue(
                    this.TeamManager.TryAddPlayerToTeam((ulong)i + 2, $"FirstPlayer{i}", FirstTeam),
                    $"Couldn't add player #{i} to the first team");
                Assert.IsTrue(
                    this.TeamManager.TryAddPlayerToTeam((ulong)i + 200, $"SecondPlayer{i}", SecondTeam),
                    $"Couldn't add player #{i} to the second team");
            }

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            SpreadsheetColumn firstTeamColumn = this.Generator.StartingColumns.Span[0];
            SpreadsheetColumn secondTeamColumn = this.Generator.StartingColumns.Span[1];
            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                this.AssertInUpdateRange(
                    $"'ROUND 1'!{firstTeamColumn}3",
                    $"FirstPlayer{i}",
                    $"Couldn't find the player { i + 1 } on the first team");
                this.AssertInUpdateRange(
                    $"'ROUND 1'!{secondTeamColumn}3",
                    $"SecondPlayer{i}",
                    $"Couldn't find player {i + 1} on the second team");
                firstTeamColumn = firstTeamColumn + 1;
                secondTeamColumn = secondTeamColumn + 1;
            }
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtPhasesLimitSucceeds()
        {
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            Assert.IsTrue(this.TeamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(this.TeamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");
            Assert.IsTrue(this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam), "Couldn't add first player to team");
            Assert.IsTrue(this.TeamManager.TryAddPlayerToTeam(3, "Bob", SecondTeam), "Couldn't add second player to team");

            for (int i = 0; i < this.Generator.PhasesLimit - 1; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
                Assert.IsTrue(game.TryScoreBonus("0"), $"Scoring a bonus should've succeeded in phase {i}");
            }

            await game.AddPlayer(3, "Bob");
            game.ScorePlayer(-5);
            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            int lastRow = this.Generator.FirstPhaseRow + this.Generator.PhasesLimit - 1;
            this.AssertInUpdateRange($"'ROUND 1'!C{lastRow}", "15", "Couldn't find Alice's buzz");
            this.AssertInUpdateRange($"'ROUND 1'!M{lastRow}", "-5", "Couldn't find Bob's buzz");
        }

        [TestMethod]
        public async Task NoBonusesWrittenAfterBonusLimit()
        {
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            Assert.IsTrue(this.TeamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(this.TeamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");
            Assert.IsTrue(this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam), "Couldn't add first player to team");
            Assert.IsTrue(this.TeamManager.TryAddPlayerToTeam(3, "Bob", SecondTeam), "Couldn't add second player to team");

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
                .FirstOrDefault(valueRange => valueRange.Range == $"'ROUND 1'!I{this.Generator.LastBonusRow}");
            Assert.IsNotNull(updateRange, "Couldn't find range for the last bonus");
            CollectionAssert.AreEquivalent(
                new int[] { 30 },
                updateRange.Values.ToArray(),
                "Unexpected scoring for the last bonus");

            updateRange = this.UpdatedRanges
                .FirstOrDefault(valueRange => valueRange.Range == $"'ROUND 1'!I{this.Generator.LastBonusRow + 1}");
            Assert.IsNull(updateRange, "Bonus past the last bonus phase should'nt be exported");
        }

        [TestMethod]
        public async Task TryCreateScoresheetPastPlayerLimitFails()
        {
            GameState game = new GameState()
            {
                Format = Format.TossupShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            Assert.IsTrue(this.TeamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(this.TeamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");

            for (int i = 0; i < this.Generator.PlayersPerTeamLimit; i++)
            {
                Assert.IsTrue(
                    this.TeamManager.TryAddPlayerToTeam((ulong)i + 2, $"Player{i}", FirstTeam),
                    $"Couldn't add player #{i} to the first team");
                Assert.IsTrue(
                    this.TeamManager.TryAddPlayerToTeam((ulong)i + 200, $"Player{i}", SecondTeam),
                    $"Couldn't add player #{i} to the second team");
            }

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            Assert.IsTrue(
                this.TeamManager.TryAddPlayerToTeam(1111, "OverLimit", FirstTeam),
                "Adding the player over the limit should've succeeded");

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
        public async Task TryCreateScoresheetOneTeamAndIndividual()
        {
            const string firstTeamPlayer = "Alice";
            const string individualName = "Individual";

            GameState game = new GameState()
            {
                Format = Format.TossupShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            Assert.IsTrue(this.TeamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the team");
            Assert.IsTrue(this.TeamManager.TryAddPlayerToTeam(2, firstTeamPlayer, FirstTeam), "Couldn't add player to team");

            await game.AddPlayer(2, firstTeamPlayer);
            game.ScorePlayer(10);
            await game.AddPlayer(3, individualName);
            game.ScorePlayer(15);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsTrue(result.Success, $"Creation should've succeeded");

            // n^2 runtime, but not that many checks or update ranges, so it should be okay

            this.AssertInUpdateRange($"'ROUND 1'!C2", FirstTeam, "Couldn't find the first team's name");
            this.AssertInUpdateRange($"'ROUND 1'!M2", individualName, "Couldn't find the second team's name");
            this.AssertInUpdateRange($"'ROUND 1'!C3", firstTeamPlayer, "Couldn't find the first team's player name");
            this.AssertInUpdateRange($"'ROUND 1'!M3", individualName, "Couldn't find the indivual team's player name");
            this.AssertInUpdateRange($"'ROUND 1'!C4", "10", "Couldn't find the buzz from the first team");
            this.AssertInUpdateRange($"'ROUND 1'!M5", "15", "Couldn't find the buzz from the individual team");
        }

        [TestMethod]
        public async Task TryCreateScoresheetPastPhaseLimitFails()
        {
            GameState game = new GameState()
            {
                Format = Format.TossupShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            Assert.IsTrue(this.TeamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the team");
            Assert.IsTrue(this.TeamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam), "Couldn't add player to team");

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
            this.AssertInUpdateRange($"'ROUND 1'!C27", "10", "Couldn't find the last buzz");
            Assert.IsFalse(
                this.UpdatedRanges
                    .Any(valueRange => valueRange.Values.Any(v => v.ToString() == "15")),
                "Last buzz should've been cut off, but we found a power");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithoutTeamsFails()
        {
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
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
            GameState game = new GameState()
            {
                Format = Format.TossupBonusesShootout,
                ReaderId = 1,
                TeamManager = this.TeamManager
            };

            for (int i = 0; i < 3; i++)
            {
                string teamName = $"Team{i}";
                Assert.IsTrue(this.TeamManager.TryAddTeam(teamName, out _), $"Couldn't add team {teamName}");
            }

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(10);

            IResult<string> result = await this.Generator.TryCreateScoresheet(game, SheetsUri, 1);
            Assert.IsFalse(result.Success, $"Creation succeeded when it should've failed.");
            Assert.AreEqual(
                "Couldn't write to the sheet. Export only works if there are 1 or 2 teams in the game.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        private void AssertInUpdateRange(string range, string value, string message)
        {
            Assert.IsTrue(
                this.UpdatedRanges
                    .Any(valueRange => valueRange.Range == range && valueRange.Values.Any(v => v.ToString() == value)),
                message);
        }

        private IGoogleSheetsApi CreateGoogleSheetsApi()
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

        private class UpdateRange
        {
            public string Range { get; set; }

            public IList<object> Values { get; set; }
        }
    }
}
