using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class ExcelFileScoresheetGeneratorTests
    {
        private const string FirstTeam = "Alpha";
        private const string SecondTeam = "Beta";

        [TestMethod]
        public async Task TryCreateScoresheetSucceeds()
        {
            const string readerName = "The Reader";
            const string roomName = "Room A";

            // Do something simple, then read the spreadsheet and verify some fields
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            teamManager.TryAddTeam(FirstTeam, out _);
            teamManager.TryAddTeam(SecondTeam, out _);
            teamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam);
            teamManager.TryAddPlayerToTeam(3, "Alan", FirstTeam);
            teamManager.TryAddPlayerToTeam(4, "Bob", SecondTeam);

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);
            game.TryScoreBonus("10/0/10");

            await game.AddPlayer(3, "Alan");
            game.ScorePlayer(-5);
            await game.AddPlayer(4, "Bob");
            game.ScorePlayer(10);
            game.TryScoreBonus("0/10/0");

            IResult<Stream> result = await generator.TryCreateScoresheet(game, readerName, roomName);
            Assert.IsTrue(result.Success, $"Failed: {(result.Success ? "" : result.ErrorMessage)}");

            using (IXLWorkbook workbook = new XLWorkbook(result.Value))
            {
                Assert.AreEqual(1, workbook.Worksheets.Count, "Unexpected number of worksheets");
                IXLWorksheet worksheet = workbook.Worksheet(1);

                Assert.AreEqual(roomName, worksheet.Cell("B2").Value.ToString(), "Unexpected reader");
                Assert.AreEqual(readerName, worksheet.Cell("B3").Value.ToString(), "Unexpected reader");
                Assert.AreEqual(readerName, worksheet.Cell("L3").Value.ToString(), "Unexpected scorekeeper");
                Assert.AreEqual(FirstTeam, worksheet.Cell("B6").Value.ToString(), "Unexpected first team name");
                Assert.AreEqual(SecondTeam, worksheet.Cell("N6").Value.ToString(), "Unexpected second team name");

                Assert.AreEqual(
                    "15", worksheet.Cell("B8").Value.ToString(), "Alice's power was not recorded");
                Assert.AreEqual("10", worksheet.Cell("H8").Value.ToString(), "1st bonus part in 1st bonus is wrong");
                Assert.AreEqual("0", worksheet.Cell("I8").Value.ToString(), "2nd bonus part in 1st bonus is wrong");
                Assert.AreEqual("10", worksheet.Cell("J8").Value.ToString(), "3rd bonus part in 1st bonus is wrong");
                Assert.AreEqual(
                    "35", worksheet.Cell("K8").Value.ToString(), $"{FirstTeam}'s total for the 1st phase is wrong");

                Assert.AreEqual(
                    "-5", worksheet.Cell("C9").Value.ToString(), "Alan's neg was not recorded");
                Assert.AreEqual(
                    "10", worksheet.Cell("N9").Value.ToString(), "Bob's get was not recorded");
                Assert.AreEqual("0", worksheet.Cell("T9").Value.ToString(), "1st bonus part in 2nd bonus is wrong");
                Assert.AreEqual("10", worksheet.Cell("U9").Value.ToString(), "2nd bonus part in 2nd bonus is wrong");
                Assert.AreEqual("0", worksheet.Cell("V9").Value.ToString(), "3rd bonus part in 2nd bonus is wrong");
                Assert.AreEqual(
                    "20", worksheet.Cell("W9").Value.ToString(), $"{SecondTeam}'s total for the 1st phase is wrong");
                Assert.AreEqual("-5", worksheet.Cell("K9").Value.ToString(), $"{FirstTeam}'s's total for the phase is wrong");
            }
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtPlayerLimitSucceeds()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");

            for (int i = 0; i < ExcelFileScoresheetGenerator.PlayersPerTeamLimit; i++)
            {
                Assert.IsTrue(
                    teamManager.TryAddPlayerToTeam((ulong)i + 2, $"FirstPlayer{i}", FirstTeam),
                    $"Couldn't add player #{i} to the first team");
                Assert.IsTrue(
                    teamManager.TryAddPlayerToTeam((ulong)i + 200, $"SecondPlayer{i}", SecondTeam),
                    $"Couldn't add player #{i} to the second team");
            }

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            using (IXLWorkbook workbook = new XLWorkbook(result.Value))
            {
                Assert.AreEqual(1, workbook.Worksheets.Count, "Unexpected number of worksheets");
                IXLWorksheet worksheet = workbook.Worksheet(1);
                for (int i = 2; i <= 7; i++)
                {
                    string playerName = $"FirstPlayer{i - 2}";
                    Assert.AreEqual(
                        playerName,
                        worksheet.Cell(7, i).Value.ToString(),
                        $"Unexpected first team player name at column {i}");
                }

                for (int i = 14; i <= 19; i++)
                {
                    string playerName = $"SecondPlayer{i - 14}";
                    Assert.AreEqual(
                        playerName,
                        worksheet.Cell(7, i).Value.ToString(),
                        $"Unexpected first team player name at column {i}");
                }
            }
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtLastTossupSucceeds()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam), "Couldn't add first player to team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(3, "Bob", SecondTeam), "Couldn't add second player to team");

            // The first phase row is the first one, so add 1 to include it in the count
            int tossupsCount = ExcelFileScoresheetGenerator.LastBonusRow - ExcelFileScoresheetGenerator.FirstPhaseRow + 1;
            for (int i = 0; i < tossupsCount - 1; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
                Assert.IsTrue(game.TryScoreBonus("0"), $"Scoring a bonus should've succeeded in phase {i}");
            }

            await game.AddPlayer(3, "Bob");
            game.ScorePlayer(-5);
            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);
            Assert.IsTrue(game.TryScoreBonus("10/0/10"), "Scoring the last bonus should've succeeded");

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            using (IXLWorkbook workbook = new XLWorkbook(result.Value))
            {
                Assert.AreEqual(1, workbook.Worksheets.Count, "Unexpected number of worksheets");
                IXLWorksheet worksheet = workbook.Worksheet(1);
                Assert.AreEqual(
                    "15", worksheet.Cell("B31").Value.ToString(), "Alice's power at the end was not recorded");
                Assert.AreEqual(
                    "-5", worksheet.Cell("N31").Value.ToString(), "Bob's neg at the end was not recorded");
                Assert.AreEqual("10", worksheet.Cell("H31").Value.ToString(), "First bonus part is wrong");
                Assert.AreEqual("0", worksheet.Cell("I31").Value.ToString(), "Second bonus part is wrong");
                Assert.AreEqual("10", worksheet.Cell("J31").Value.ToString(), "Third bonus part is wrong");
                Assert.AreEqual("35", worksheet.Cell("K31").Value.ToString(), "Alice's total for the phase is wrong");
                Assert.AreEqual("-5", worksheet.Cell("W31").Value.ToString(), "Bob's total for the phase is wrong");
            }
        }

        [TestMethod]
        public async Task TryCreateScoresheetAtPhaseLimitSucceeds()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam), "Couldn't add first player to team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(3, "Bob", SecondTeam), "Couldn't add second player to team");

            for (int i = 0; i < ExcelFileScoresheetGenerator.PhasesLimit - 1; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
                Assert.IsTrue(game.TryScoreBonus("0"), $"Scoring a bonus should've succeeded in phase {i}");
            }

            await game.AddPlayer(3, "Bob");
            game.ScorePlayer(-5);
            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(15);
            Assert.IsTrue(game.TryScoreBonus("10/0/10"), "Scoring the last bonus should've succeeded");

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            using (IXLWorkbook workbook = new XLWorkbook(result.Value))
            {
                Assert.AreEqual(1, workbook.Worksheets.Count, "Unexpected number of worksheets");
                IXLWorksheet worksheet = workbook.Worksheet(1);
                Assert.AreEqual(
                    "15", worksheet.Cell("B36").Value.ToString(), "Alice's power at the end was not recorded");
                Assert.AreEqual(
                    "-5", worksheet.Cell("N36").Value.ToString(), "Bob's neg at the end was not recorded");

                for (int i = ExcelFileScoresheetGenerator.LastBonusRow + 1; i < ExcelFileScoresheetGenerator.LastBonusRow + 5; i++)
                {
                    Assert.AreEqual(string.Empty, worksheet.Cell($"H{i}").Value.ToString(), $"First bonus part is wrong for row {i}");
                    Assert.AreEqual(string.Empty, worksheet.Cell($"I{i}").Value.ToString(), $"Second bonus part is wrong for row {i}");
                    Assert.AreEqual(string.Empty, worksheet.Cell($"J{i}").Value.ToString(), $"Third bonus part is wrong for row {i}");
                }
            }
        }

        [TestMethod]
        public async Task TryCreateScoresheetPastPlayerLimitFails()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the first team");
            Assert.IsTrue(teamManager.TryAddTeam(SecondTeam, out _), "Couldn't add the second team");

            for (int i = 0; i < ExcelFileScoresheetGenerator.PlayersPerTeamLimit; i++)
            {
                Assert.IsTrue(
                    teamManager.TryAddPlayerToTeam((ulong)i + 2, $"Player{i}", FirstTeam),
                    $"Couldn't add player #{i} to the first team");
                Assert.IsTrue(
                    teamManager.TryAddPlayerToTeam((ulong)i + 200, $"Player{i}", SecondTeam),
                    $"Couldn't add player #{i} to the second team");
            }

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            Assert.IsTrue(
                teamManager.TryAddPlayerToTeam(1111, "OverLimit", FirstTeam),
                "Adding the player over the limit should've succeeded");
            result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsFalse(result.Success, $"Creation should've failed after the limit.");
            Assert.AreEqual(
                $"Export only currently works if there are at most {ExcelFileScoresheetGenerator.PlayersPerTeamLimit} players on a team.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetPastPhaseLimitFails()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            Assert.IsTrue(teamManager.TryAddTeam(FirstTeam, out _), "Couldn't add the team");
            Assert.IsTrue(teamManager.TryAddPlayerToTeam(2, "Alice", FirstTeam), "Couldn't add player to team");

            for (int i = 0; i < ExcelFileScoresheetGenerator.PhasesLimit; i++)
            {
                await game.AddPlayer(2, "Alice");
                game.ScorePlayer(10);
            }

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsTrue(result.Success, $"Creation should've succeeded at the limit.");

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(10);
            result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsFalse(result.Success, $"Creation should've failed after the limit.");
            Assert.AreEqual(
                $"Export only currently works if there are at most {ExcelFileScoresheetGenerator.PhasesLimit} tosusps answered in a game.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithoutTeamsFails()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(10);

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsFalse(result.Success, $"Creation succeeded when it should've failed.");
            Assert.AreEqual(
                "Export only works if there are 1 or 2 teams in the game.",
                result.ErrorMessage,
                "Unexpected error message");
        }

        [TestMethod]
        public async Task TryCreateScoresheetWithMoreThanTwoTeamsFails()
        {
            ExcelFileScoresheetGenerator generator = new ExcelFileScoresheetGenerator();
            ByCommandTeamManager teamManager = new ByCommandTeamManager();
            GameState game = new GameState()
            {
                Format = Format.CreateTossupBonusesShootout(false),
                ReaderId = 1,
                TeamManager = teamManager
            };

            for (int i = 0; i < 3; i++)
            {
                string teamName = $"Team{i}";
                Assert.IsTrue(teamManager.TryAddTeam(teamName, out _), $"Couldn't add team {teamName}");
            }

            await game.AddPlayer(2, "Alice");
            game.ScorePlayer(10);

            IResult<Stream> result = await generator.TryCreateScoresheet(game, "Reader X", "Room A");
            Assert.IsFalse(result.Success, $"Creation succeeded when it should've failed.");
            Assert.AreEqual(
                "Export only works if there are 1 or 2 teams in the game.",
                result.ErrorMessage,
                "Unexpected error message");
        }
    }
}
