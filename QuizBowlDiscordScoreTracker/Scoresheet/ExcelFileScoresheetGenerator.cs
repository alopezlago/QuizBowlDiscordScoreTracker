using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public class ExcelFileScoresheetGenerator : IFileScoresheetGenerator
    {
        internal const int PlayersPerTeamLimit = 6;
        internal const int PhasesLimit = 28;
        internal const int FirstPhaseRow = 8;
        internal const int LastBonusRow = 31;

        private const int RoomRow = 2;
        private const int ModeratorRow = 3;
        private const int TeamNameRow = 6;
        private const int PlayerNameRow = 7;
        private const string TemplateFilename = "naqt-scoresheet-electronic-template.xlsx";
        private static readonly string TemplateFile = Path.Combine("Scoresheet", TemplateFilename);

        private static readonly int[] StartingColumns = new int[] { 2, 14 };
        private static readonly int[] BonusColumns = new int[] { 8, 20 };

        public async Task<IResult<Stream>> TryCreateScoresheet(GameState game, string readerName, string roomName)
        {
            Verify.IsNotNull(game, nameof(game));

            IReadOnlyDictionary<string, string> teamIdToNames = await game.TeamManager.GetTeamIdToNames();
            if (teamIdToNames.Count == 0 || teamIdToNames.Count > 2)
            {
                return new FailureResult<Stream>("Export only works if there are 1 or 2 teams in the game.");
            }

            // Make it an array so we don't keep re-evaluating the enumerable
            PlayerTeamPair[] players = (await game.TeamManager.GetKnownPlayers()).ToArray();
            IEnumerable<IGrouping<string, PlayerTeamPair>> playersByTeam = players.GroupBy(player => player.TeamId);
            if (playersByTeam.Any(grouping => grouping.Count() > PlayersPerTeamLimit))
            {
                return new FailureResult<Stream>("Export only currently works if there are at most 6 players on a team.");
            }

            string[] teamIds = playersByTeam.Select(grouping => grouping.Key).ToArray();

            // We could have an extra phase, but if there are no scoring actions, then it wasn't played
            IEnumerable<PhaseScore> phaseScores = await game.GetPhaseScores();
            int phaseScoresCount = phaseScores.Count();
            if (phaseScoresCount > PhasesLimit + 1 ||
                (phaseScoresCount == PhasesLimit + 1 && phaseScores.Last().ScoringSplitsOnActions.Any()))
            {
                return new FailureResult<Stream>(
                    $"Export only currently works if there are at most {ExcelFileScoresheetGenerator.PhasesLimit} tosusps answered in a game.");
            }

            // Copy the file to a stream, so we don't have the handle open the whole time. We may want to cache this
            // stream, or make several copies of the stream that we can use at once, so we can keep this in memory and
            // avoid hitting the disk.
            MemoryStream stream = new MemoryStream();
            using (FileStream fs = File.OpenRead(TemplateFile))
            {
                await fs.CopyToAsync(stream);
            }

            // Create the playerId -> column mapping
            IReadOnlyDictionary<ulong, int> playerIdToColumn = CreatePlayerIdToColumnMapping(playersByTeam);

            using (XLWorkbook workbook = new XLWorkbook(stream))
            {
                IXLWorksheet worksheet = workbook.Worksheets.First();

                worksheet.Cell(RoomRow, 2).Value = roomName;
                worksheet.Cell(ModeratorRow, 2).Value = readerName;
                worksheet.Cell(ModeratorRow, 12).Value = readerName;

                worksheet.Cell(TeamNameRow, 2).Value = teamIdToNames.Values.First();
                if (teamIdToNames.Count > 1)
                {
                    worksheet.Cell(TeamNameRow, StartingColumns[1]).Value = teamIdToNames.ElementAt(1).Value;
                }

                foreach (PlayerTeamPair pair in players)
                {
                    int column = playerIdToColumn[pair.PlayerId];
                    worksheet.Cell(PlayerNameRow, column).Value = pair.PlayerDisplayName;
                }

                // Go through and score each phase
                int row = FirstPhaseRow;
                foreach (PhaseScore phaseScore in phaseScores)
                {
                    foreach (ScoringSplitOnScoreAction action in phaseScore.ScoringSplitsOnActions)
                    {
                        if (!playerIdToColumn.TryGetValue(action.Action.Buzz.UserId, out int column))
                        {
                            return new FailureResult<Stream>(
                                $"Unknown player {action.Action.Buzz.PlayerDisplayName} (ID {action.Action.Buzz.UserId}). Cannot accurately create a scoresheet. This happens in phase {row - FirstPhaseRow + 1}");
                        }

                        worksheet.Cell(row, column).Value = action.Action.Score;
                    }

                    if (row <= LastBonusRow && phaseScore.BonusScores?.Any() == true)
                    {
                        int bonusPartCount = phaseScore.BonusScores.Count();
                        if (bonusPartCount != 3)
                        {
                            return new FailureResult<Stream>(
                                $"Non-three part bonus in phase {row - FirstPhaseRow + 1}. Number of parts: {bonusPartCount}. These aren't supported for the scoresheet.");
                        }

                        int bonusScoresIndex = Array.IndexOf(teamIds, phaseScore.BonusTeamId);
                        if (bonusScoresIndex < 0)
                        {
                            return new FailureResult<Stream>(
                                $"Unknown bonus team in phase {row - FirstPhaseRow + 1}. Cannot accurately create a scoresheet.");
                        }

                        int bonusColumn = BonusColumns[bonusScoresIndex];
                        foreach (int score in phaseScore.BonusScores)
                        {
                            worksheet.Cell(row, bonusColumn).Value = score;
                            bonusColumn++;
                        }
                    }

                    // After the last bonus row, we skip one row to account for the divider
                    if (row == LastBonusRow)
                    {
                        row++;
                    }

                    row++;
                }

                workbook.Save();
            }
            stream.Position = 0;
            return new SuccessResult<Stream>(stream);
        }

        private static IReadOnlyDictionary<ulong, int> CreatePlayerIdToColumnMapping(
            IEnumerable<IGrouping<string, PlayerTeamPair>> playersByTeam)
        {
            Dictionary<ulong, int> playerIdToColumn = new Dictionary<ulong, int>();
            int startingColumnIndex = 0;
            foreach (IGrouping<string, PlayerTeamPair> grouping in playersByTeam)
            {
                int startingColumn = StartingColumns[startingColumnIndex];
                foreach (PlayerTeamPair pair in grouping)
                {
                    // TODO: If we ever support more than 6 players, we should bump up the next starting column
                    playerIdToColumn[pair.PlayerId] = startingColumn++;
                }

                startingColumnIndex++;
            }

            return playerIdToColumn;
        }
    }
}
