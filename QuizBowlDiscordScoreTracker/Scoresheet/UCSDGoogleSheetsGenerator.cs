using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Sheets.v4.Data;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    // TODO: Eventually allow for more columns than Z (can even just do up to ZZ: +500 unrealistic for QB)
    // TODO: Add reader name to mod/scorekeeper section

    public sealed class UCSDGoogleSheetsGenerator : IGoogleSheetsGenerator
    {
        internal const int FirstPhaseRow = 4;
        internal const int PlayersPerTeamLimit = 6;
        internal const int PhasesLimit = 28;
        internal const int LastBonusRow = 27;
        internal const string RostersSheetName = "Rosters";
        internal static readonly ReadOnlyMemory<char> StartingColumnsChars = new char[] { 'C', 'O' };

        private const int TeamNameRow = 1;
        private const int PlayerNameRow = 3;

        private static readonly char[] BonusColumns = new char[] { 'I', 'U' };
        private static readonly bool[] ClearedBonusArray = new bool[] { false, false, false };

        private static readonly List<string> ClearRosters = new List<string>() { $"'{RostersSheetName}'!A2:G999" };

        public UCSDGoogleSheetsGenerator(IGoogleSheetsApi sheetsApi)
        {
            this.SheetsApi = sheetsApi;
        }

        private IGoogleSheetsApi SheetsApi { get; }

        public async Task<IResult<string>> TryCreateScoresheet(GameState game, Uri sheetsUri, string sheetName)
        {
            Verify.IsNotNull(game, nameof(game));
            Verify.IsNotNull(sheetsUri, nameof(sheetsUri));

            // TODO: Initializing the team and player mappings is the same as ExcleFileScoresheetGenerator. See if we
            // can unify this, if the abstractions make sense.
            IReadOnlyDictionary<string, string> teamIdToNames = await game.TeamManager.GetTeamIdToNames();
            if (teamIdToNames.Count == 0 || teamIdToNames.Count > 2)
            {
                return CreateFailureResult("Export only works if there are 1 or 2 teams in the game.");
            }

            // Make it an array so we don't keep re-evaluating the enumerable
            PlayerTeamPair[] players = (await game.TeamManager.GetKnownPlayers()).ToArray();
            IEnumerable<IGrouping<string, PlayerTeamPair>> playersByTeam = players.GroupBy(player => player.TeamId);
            if (playersByTeam.Any(grouping => grouping.Count() > PlayersPerTeamLimit))
            {
                return CreateFailureResult("Export only currently works if there are at most 6 players on a team.");
            }

            string[] teamIds = playersByTeam.Select(grouping => grouping.Key).ToArray();

            // We could have an extra phase, but if there are no scoring actions, then it wasn't played
            IEnumerable<PhaseScore> phaseScores = await game.GetPhaseScores();
            int phaseScoresCount = phaseScores.Count();
            if (phaseScoresCount > PhasesLimit + 1 ||
                (phaseScoresCount == PhasesLimit + 1 && phaseScores.Last().ScoringSplitsOnActions.Any()))
            {
                return CreateFailureResult(
                    $"Export only currently works if there are at most {PhasesLimit} tosusps answered in a game. Bonuses will only be tracked up to question 24.");
            }

            // TODO: When Formats are supported (so bonuses stop after a certain number of questions), support the
            // tiebreakers.

            IReadOnlyDictionary<ulong, char> playerIdToColumn = CreatePlayerIdToColumnMapping(playersByTeam);

            // TODO: See if filling in player's scores by columns can be done efficiently, since it should require less
            // requests
            List<ValueRange> ranges = new List<ValueRange>
            {
                CreateUpdateSingleCellRequest(
                    sheetName, StartingColumnsChars.Span[0], TeamNameRow, teamIdToNames[playersByTeam.First().Key])
            };
            if (teamIdToNames.Count > 1)
            {
                ranges.Add(CreateUpdateSingleCellRequest(
                    sheetName, StartingColumnsChars.Span[1], TeamNameRow, teamIdToNames[playersByTeam.ElementAt(1).Key]));
            }

            foreach (PlayerTeamPair pair in players)
            {
                // TODO: Make this more efficient by putting all the player names in one update request
                char column = playerIdToColumn[pair.PlayerId];
                ranges.Add(CreateUpdateSingleCellRequest(sheetName, column, PlayerNameRow, pair.PlayerDisplayName));
            }

            int row = FirstPhaseRow;
            List<char> scoredColumns = new List<char>();
            foreach (PhaseScore phaseScore in phaseScores)
            {
                foreach (ScoringSplitOnScoreAction action in phaseScore.ScoringSplitsOnActions)
                {
                    if (!playerIdToColumn.TryGetValue(action.Action.Buzz.UserId, out char column))
                    {
                        return new FailureResult<string>(
                            $"Unknown player {action.Action.Buzz.PlayerDisplayName} (ID {action.Action.Buzz.UserId}). Cannot accurately create a scoresheet. This happens in phase {row - FirstPhaseRow + 1}");
                    }

                    ranges.Add(CreateUpdateSingleCellRequest(sheetName, column, row, action.Action.Score));
                    scoredColumns.Add(column);
                }

                scoredColumns.Clear();

                if (row <= LastBonusRow)
                {
                    if (phaseScore.BonusScores?.Any() == true)
                    {
                        int bonusPartCount = phaseScore.BonusScores.Count();
                        if (bonusPartCount != 3)
                        {
                            return new FailureResult<string>(
                                $"Non-three part bonus in phase {row - FirstPhaseRow + 1}. Number of parts: {bonusPartCount}. These aren't supported for the scoresheet.");
                        }

                        int bonusScoresIndex = Array.IndexOf(teamIds, phaseScore.BonusTeamId);
                        if (bonusScoresIndex < 0)
                        {
                            return new FailureResult<string>(
                                $"Unknown bonus team in phase {row - FirstPhaseRow + 1}. Cannot accurately create a scoresheet.");
                        }

                        char bonusColumn = BonusColumns[bonusScoresIndex];
                        ranges.Add(CreateUpdateCellsAlongRowRequest(
                            sheetName, bonusColumn, row, phaseScore.BonusScores.Select(score => score > 0).ToArray()));

                        char otherBonusColumn = BonusColumns[BonusColumns.Length - bonusScoresIndex - 1];
                        ranges.Add(CreateUpdateCellsAlongRowRequest(
                            sheetName, otherBonusColumn, row, ClearedBonusArray));
                    }
                    else
                    {
                        // Clear the bonus columns
                        foreach (char column in BonusColumns)
                        {
                            ranges.Add(CreateUpdateCellsAlongRowRequest(sheetName, column, row, ClearedBonusArray));
                        }
                    }
                }

                row++;
            }

            int columnsAfterInitial = PlayersPerTeamLimit - 1;
            List<string> rangesToClear = new List<string>()
            {
                // Add 5 to the column because the sheet supports 6 players. The first column counts as the first
                // player already.
                $"'{sheetName}'!{StartingColumnsChars.Span[0]}4:{(char)(StartingColumnsChars.Span[0] + columnsAfterInitial)}31",
                $"'{sheetName}'!{StartingColumnsChars.Span[1]}4:{(char)(StartingColumnsChars.Span[1] + columnsAfterInitial)}31",
            };

            return await this.SheetsApi.UpdateGoogleSheet(ranges, rangesToClear, sheetsUri);
        }

        public async Task<IResult<string>> TryUpdateRosters(ITeamManager teamManager, Uri sheetsUri)
        {
            Verify.IsNotNull(teamManager, nameof(teamManager));
            Verify.IsNotNull(sheetsUri, nameof(sheetsUri));

            IReadOnlyDictionary<string, string> teamIdToNames = await teamManager.GetTeamIdToNames();

            IEnumerable<PlayerTeamPair> playerTeamPairs = await teamManager.GetKnownPlayers();
            IEnumerable<IGrouping<string, PlayerTeamPair>> groupings = playerTeamPairs
                .GroupBy(pair => pair.TeamId)
                .Where(grouping => grouping.Any());

            if (groupings.Any(grouping => grouping.Count() > PlayersPerTeamLimit))
            {
                return CreateFailureResult("Rosters can only support up to 6 players per team.");
            }

            int row = 2;
            List<ValueRange> ranges = new List<ValueRange>();
            foreach (IGrouping<string, PlayerTeamPair> grouping in groupings)
            {
                PlayerTeamPair firstPair = grouping.First();
                if (!teamIdToNames.TryGetValue(firstPair.TeamId, out string teamName))
                {
                    // It's not a team, but an individual player. Use their name.
                    teamName = firstPair.PlayerDisplayName;
                }

                IEnumerable<string> playerNames = grouping.Select(pair => pair.PlayerDisplayName);
                IEnumerable<string> rowValues = new string[] { teamName }
                    .Concat(playerNames);
                ranges.Add(CreateUpdateCellsAlongRowRequest(RostersSheetName, 'A', row, rowValues));

                row++;
            }

            // Go through the teams and update rosters
            return await this.SheetsApi.UpdateGoogleSheet(ranges, ClearRosters, sheetsUri);
        }

        private static FailureResult<string> CreateFailureResult(string errorMessage)
        {
            return new FailureResult<string>($"Couldn't write to the sheet. {errorMessage}");
        }

        // This is similar to the one in ExcelFileScoresheetGenerator, but it works on
        private static IReadOnlyDictionary<ulong, char> CreatePlayerIdToColumnMapping(
            IEnumerable<IGrouping<string, PlayerTeamPair>> playersByTeam)
        {
            Dictionary<ulong, char> playerIdToColumn = new Dictionary<ulong, char>();
            int startingColumnIndex = 0;
            foreach (IGrouping<string, PlayerTeamPair> grouping in playersByTeam)
            {
                char startingColumn = StartingColumnsChars.Span[startingColumnIndex];
                foreach (PlayerTeamPair pair in grouping)
                {
                    // TODO: If we ever support more than 6 players, we should bump up the next starting column
                    playerIdToColumn[pair.PlayerId] = startingColumn++;
                }

                startingColumnIndex++;
            }

            return playerIdToColumn;
        }

        // TODO: If the sheet ever supports columns past Z, we need to use a string or wrapper class for AA and above
        private static ValueRange CreateUpdateSingleCellRequest<T>(string sheetName, char column, int rowIndex, T value)
        {
            ValueRange range = new ValueRange()
            {
                Range = $"'{sheetName}'!{column}{rowIndex}",
                Values = new List<IList<object>>()
                {
                    new List<object>()
                    {
                        value
                    }
                }
            };

            return range;
        }

        // TODO: If the sheet ever supports columns past Z, we need to use a string or wrapper class for AA and above
        private static ValueRange CreateUpdateCellsAlongRowRequest<T>(
            string sheetName, char column, int rowIndex, IEnumerable<T> values)
        {
            // Subtract one, since the start column already covers the first value
            char endColumn = (char)(column + values.Count() - 1);
            List<object> innerList = new List<object>();
            foreach (T value in values)
            {
                innerList.Add(value);
            }

            ValueRange range = new ValueRange()
            {
                Range = $"'{sheetName}'!{column}{rowIndex}:{endColumn}{rowIndex}",
                Values = new List<IList<object>>()
                {
                    innerList
                }
            };

            return range;
        }
    }
}
