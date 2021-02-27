using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Sheets.v4.Data;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public abstract class BaseGoogleSheetsGenerator : IGoogleSheetsGenerator
    {
        public BaseGoogleSheetsGenerator(IGoogleSheetsApi sheetsApi)
        {
            this.SheetsApi = sheetsApi;
        }

        // Used in tests
        internal abstract int FirstPhaseRow { get; }

        internal abstract int LastBonusRow { get; }

        internal abstract int PhasesLimit { get; }

        // Used in tests
        internal abstract int PlayersPerTeamLimit { get; }

        // Used in tests
        internal abstract ReadOnlyMemory<SpreadsheetColumn> StartingColumns { get; }

        protected abstract List<string> ClearRostersRanges { get; }

        protected abstract int PlayerNameRow { get; }

        protected abstract ReadOnlyMemory<SpreadsheetColumn> BonusColumns { get; }

        protected abstract int TeamsLimit { get; }

        protected abstract int TeamNameRow { get; }

        private IGoogleSheetsApi SheetsApi { get; }

        public async Task<IResult<string>> TryCreateScoresheet(GameState game, Uri sheetsUri, int roundNumber)
        {
            Verify.IsNotNull(game, nameof(game));
            Verify.IsNotNull(sheetsUri, nameof(sheetsUri));

            IReadOnlyDictionary<string, string> teamIdToNames = await game.TeamManager.GetTeamIdToNames();
            if (teamIdToNames.Count == 0 || teamIdToNames.Count > 2)
            {
                return CreateFailureResult("Export only works if there are 1 or 2 teams in the game.");
            }

            // Make it an array so we don't keep re-evaluating the enumerable

            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> players = await game.GetLastScoringSplits();
            IEnumerable<IGrouping<string, PlayerTeamPair>> playersByTeam = players.GroupBy(
                kvp => kvp.Key.TeamId, kvp => kvp.Key);
            if (playersByTeam.Any(grouping => grouping.Count() > this.PlayersPerTeamLimit))
            {
                return CreateFailureResult("Export only currently works if there are at most 6 players on a team.");
            }

            string[] teamIds = playersByTeam.Select(grouping => grouping.Key).ToArray();

            IEnumerable<PhaseScore> phaseScores = await game.GetPhaseScores();
            int phaseScoresCount = phaseScores.Count();
            bool trimScoresheet = false;
            if (phaseScoresCount > this.PhasesLimit + 1 ||
                (phaseScoresCount == this.PhasesLimit + 1 && phaseScores.Last().ScoringSplitsOnActions.Any()))
            {
                trimScoresheet = true;
                phaseScores = phaseScores.Take(this.PhasesLimit);
            }

            IReadOnlyDictionary<ulong, SpreadsheetColumn> playerIdToColumn = this.CreatePlayerIdToColumnMapping(playersByTeam);
            string sheetName = this.GetSheetName(roundNumber);
            List<ValueRange> ranges = new List<ValueRange>();

            // Write the names of the teams
            int startingColumnIndex = 0;
            foreach (string teamId in teamIds)
            {
                if (!teamIdToNames.TryGetValue(teamId, out string teamName))
                {
                    // We know the player exists since the teamIds came from the list of players
                    teamName = players.First(kvp => kvp.Key.TeamId == teamId).Key.PlayerDisplayName;
                }

                ranges.Add(CreateUpdateSingleCellRequest(
                    sheetName, this.StartingColumns.Span[startingColumnIndex], this.TeamNameRow, teamName));

                startingColumnIndex++;
            }

            foreach (PlayerTeamPair pair in players.Select(kvp => kvp.Key))
            {
                // TODO: Make this more efficient by putting all the player names in one update request
                SpreadsheetColumn column = playerIdToColumn[pair.PlayerId];
                ranges.Add(CreateUpdateSingleCellRequest(sheetName, column, this.PlayerNameRow, pair.PlayerDisplayName));
            }

            // Tossup scoring should be similar, but some bonuses are 3 parts, while others are 1 value
            // We can either let the sheets handle that, or do a switch here for 1 vs 3 part bonuses
            // but we have the weird DT thing with TJSheets, so let's have abstract classes deal with it
            int row = this.FirstPhaseRow;
            int phasesCount = phaseScores.Count();
            foreach (PhaseScore phaseScore in phaseScores)
            {
                foreach (ScoringSplitOnScoreAction action in phaseScore.ScoringSplitsOnActions)
                {
                    if (!playerIdToColumn.TryGetValue(action.Action.Buzz.UserId, out SpreadsheetColumn column))
                    {
                        return new FailureResult<string>(
                            $"Unknown player {action.Action.Buzz.PlayerDisplayName} (ID {action.Action.Buzz.UserId}). Cannot accurately create a scoresheet. This happens in phase {row - this.FirstPhaseRow + 1}");
                    }

                    ranges.Add(CreateUpdateSingleCellRequest(sheetName, column, row, action.Action.Score));
                }

                IResult<IEnumerable<ValueRange>> additionalRanges = this.GetAdditionalUpdateRangesForTossup(
                    phaseScore, teamIds, sheetName, row, phasesCount);
                if (!additionalRanges.Success)
                {
                    return new FailureResult<string>(additionalRanges.ErrorMessage);
                }

                ranges.AddRange(additionalRanges.Value);

                // We need to move this to the UpdateRanges method, since we may need to write other stuff
                // Include that in the comment
                if (row <= this.LastBonusRow)
                {
                    IResult<IEnumerable<ValueRange>> bonusRanges = this.GetUpdateRangesForBonus(
                        phaseScore, teamIds, sheetName, row, phasesCount);
                    if (!bonusRanges.Success)
                    {
                        return new FailureResult<string>(bonusRanges.ErrorMessage);
                    }

                    ranges.AddRange(bonusRanges.Value);
                }

                row++;
            }

            IResult<string> updateResult = await this.SheetsApi.UpdateGoogleSheet(
                ranges, this.GetClearRanges(sheetName), sheetsUri);
            if (!updateResult.Success)
            {
                return updateResult;
            }

            string message = $"Game written to the scoresheet {sheetName}.";
            if (trimScoresheet)
            {
                message += $" This game had more tossups than space in the scoresheet, so only the first {this.PhasesLimit} tossup/bonus cycles were recorded.";
            }

            return new SuccessResult<string>(message);
        }

        public async Task<IResult<string>> TryUpdateRosters(IByRoleTeamManager teamManager, Uri sheetsUri)
        {
            Verify.IsNotNull(teamManager, nameof(teamManager));
            Verify.IsNotNull(sheetsUri, nameof(sheetsUri));

            IEnumerable<IGrouping<string, PlayerTeamPair>> groupings = await teamManager.GetPlayerTeamPairsForServer();

            if (groupings.Any(grouping => grouping.Count() > this.PlayersPerTeamLimit))
            {
                return CreateFailureResult(
                    $"Rosters can only support up to {this.PlayersPerTeamLimit} players per team.");
            }

            int groupingsCount = groupings.Count();

            if (groupingsCount == 0)
            {
                return CreateFailureResult($"No teams were found, so the rosters remain unchanged.");
            }
            else if (groupingsCount > this.TeamsLimit)
            {
                return CreateFailureResult(
                    $"Rosters can only support up to {this.TeamsLimit} teams.");
            }

            IReadOnlyDictionary<string, string> teamIdToNames = await teamManager.GetTeamIdToNamesForServer();
            IResult<List<ValueRange>> rangesResult = this.GetUpdateRangesForRoster(teamIdToNames, groupings);
            if (!rangesResult.Success)
            {
                return CreateFailureResult(rangesResult.ErrorMessage);
            }

            return await this.SheetsApi.UpdateGoogleSheet(rangesResult.Value, this.ClearRostersRanges, sheetsUri);
        }

        protected abstract List<string> GetClearRanges(string sheetName);

        protected abstract string GetSheetName(int roundNumber);

        protected abstract IResult<IEnumerable<ValueRange>> GetUpdateRangesForBonus(
            PhaseScore phaseScore, string[] teamIds, string sheetName, int row, int phasesCount);

        protected abstract IResult<IEnumerable<ValueRange>> GetAdditionalUpdateRangesForTossup(
            PhaseScore phaseScore, string[] teamIds, string sheetName, int row, int phasesCount);

        protected abstract IResult<List<ValueRange>> GetUpdateRangesForRoster(
            IReadOnlyDictionary<string, string> teamIdToNames, IEnumerable<IGrouping<string, PlayerTeamPair>> groupings);

        private IReadOnlyDictionary<ulong, SpreadsheetColumn> CreatePlayerIdToColumnMapping(
            IEnumerable<IGrouping<string, PlayerTeamPair>> playersByTeam)
        {
            Dictionary<ulong, SpreadsheetColumn> playerIdToColumn = new Dictionary<ulong, SpreadsheetColumn>();
            int startingColumnIndex = 0;
            foreach (IGrouping<string, PlayerTeamPair> grouping in playersByTeam)
            {
                SpreadsheetColumn startingColumn = this.StartingColumns.Span[startingColumnIndex];
                foreach (PlayerTeamPair pair in grouping)
                {
                    // TODO: If we ever support more than 6 players, we should bump up the next starting column
                    playerIdToColumn[pair.PlayerId] = startingColumn;
                    startingColumn = startingColumn + 1;
                }

                startingColumnIndex++;
            }

            return playerIdToColumn;
        }

        protected static FailureResult<string> CreateFailureResult(string errorMessage)
        {
            return new FailureResult<string>($"Couldn't write to the sheet. {errorMessage}");
        }

        protected static ValueRange CreateUpdateSingleCellRequest<T>(
            string sheetName, SpreadsheetColumn column, int rowIndex, T value)
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

        protected static ValueRange CreateUpdateCellsAlongColumnRequest<T>(
            string sheetName, SpreadsheetColumn column, int rowNumber, IEnumerable<T> values)
        {
            Verify.IsNotNull(values, nameof(values));

            // Subtract one, since the start column already covers the first value
            int endRowIndex = rowNumber + (values.Count() - 1);
            List<object> innerList = new List<object>();
            foreach (T value in values)
            {
                innerList.Add(value);
            }

            ValueRange range = new ValueRange()
            {
                Range = $"'{sheetName}'!{column}{rowNumber}:{column}{endRowIndex}",
                Values = new List<IList<object>>()
                {
                    innerList
                },
                MajorDimension = "COLUMNS"
            };

            return range;
        }

        protected static ValueRange CreateUpdateCellsAlongRowRequest<T>(
            string sheetName, SpreadsheetColumn column, int rowNumber, IEnumerable<T> values)
        {
            Verify.IsNotNull(values, nameof(values));

            // Subtract one, since the start column already covers the first value
            SpreadsheetColumn endColumn = column + (values.Count() - 1);
            List<object> innerList = new List<object>();
            foreach (T value in values)
            {
                innerList.Add(value);
            }

            ValueRange range = new ValueRange()
            {
                Range = $"'{sheetName}'!{column}{rowNumber}:{endColumn}{rowNumber}",
                Values = new List<IList<object>>()
                {
                    innerList
                }
            };

            return range;
        }
    }
}
