using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Google.Apis.Sheets.v4.Data;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public class TJSheetsGenerator : BaseGoogleSheetsGenerator
    {
        internal const int RostersFirstPlayerRow = 2;

        internal const string RostersSheetName = "ROSTERS";
        internal static readonly ReadOnlyMemory<SpreadsheetColumn> StartingColumnsArray = new SpreadsheetColumn[]
        {
            new SpreadsheetColumn('C'),
            new SpreadsheetColumn('M')
        };
        internal static readonly ReadOnlyMemory<SpreadsheetColumn> BonusColumnsArray = new SpreadsheetColumn[]
        {
            new SpreadsheetColumn('I'),
            new SpreadsheetColumn('S')
        };

        private static readonly List<string> ClearRosters = new List<string>() { $"'{RostersSheetName}'!A1:ZZ21" };
        private static readonly SpreadsheetColumn RostersFirstTeamColumn = new SpreadsheetColumn(1);

        public TJSheetsGenerator(IGoogleSheetsApi sheetsApi) : base(sheetsApi)
        {
        }

        internal override int FirstPhaseRow => 4;

        internal override int LastBonusRow => 23;

        internal override int PhasesLimit => 24;

        // TJ Sheets doesn't really have a roster player limit, but we use 6 because the scoresheet limits it to 6,
        // and it greatly simplifies the logic
        internal override int PlayersPerTeamLimit => 6;

        internal override ReadOnlyMemory<SpreadsheetColumn> StartingColumns => StartingColumnsArray;

        protected override List<string> ClearRostersRanges => ClearRosters;

        protected override int PlayerNameRow => 3;

        protected override ReadOnlyMemory<SpreadsheetColumn> BonusColumns => BonusColumnsArray;

        protected override int TeamsLimit => 2000;

        protected override int TeamNameRow => 2;

        protected override IResult<IEnumerable<ValueRange>> GetAdditionalUpdateRangesForTossup(
            PhaseScore phaseScore, string[] teamIds, string sheetName, int row, int phasesCount)
        {
            Verify.IsNotNull(phaseScore, nameof(phaseScore));

            if (row == this.FirstPhaseRow + phasesCount - 1 && !phaseScore.ScoringSplitsOnActions.Any())
            {
                // We're in the last phase, and nothing has happened, so it's a placeholder phase for any future
                // scoring actions. This shouldn't be put into the scoresheet.
                return new SuccessResult<IEnumerable<ValueRange>>(Enumerable.Empty<ValueRange>());
            }

            // If the question went dead, put in "DT" in the first bonus column to indicate that the question was read
            if (phaseScore.ScoringSplitsOnActions.All(split => split.Action.Score <= 0))
            {
                List<ValueRange> ranges = new List<ValueRange>();
                ranges.Add(CreateUpdateSingleCellRequest(sheetName, this.BonusColumns.Span[0], row, "DT"));
                return new SuccessResult<IEnumerable<ValueRange>>(ranges);
            }

            return new SuccessResult<IEnumerable<ValueRange>>(Enumerable.Empty<ValueRange>());
        }

        protected override string GetSheetName(int roundNumber)
        {
            return $"ROUND {roundNumber}";
        }

        protected override List<string> GetClearRanges(string sheetName)
        {
            // We want to include all the player columns, and the bonus column, so don't include - 1
            int columnsAfterInitial = this.PlayersPerTeamLimit;
            return new List<string>()
            {
                // Add 5 to the column because the sheet supports 6 players. The first column counts as the first
                // player already.
                $"'{sheetName}'!{StartingColumnsArray.Span[0]}4:{StartingColumnsArray.Span[0] + columnsAfterInitial}27",
                $"'{sheetName}'!{StartingColumnsArray.Span[1]}4:{StartingColumnsArray.Span[1] + columnsAfterInitial}27",

                // Clear the second team name; the first should always be overwritten
                $"'{sheetName}'!{this.StartingColumns.Span[1]}{TeamNameRow}:{this.StartingColumns.Span[1]}{TeamNameRow}",

                // Clear player names too, but subtract one, since we don't include the bonus row
                $"'{sheetName}'!{this.StartingColumns.Span[0]}{PlayerNameRow}:{this.StartingColumns.Span[0] + columnsAfterInitial - 1}{PlayerNameRow}",
                $"'{sheetName}'!{this.StartingColumns.Span[1]}{PlayerNameRow}:{this.StartingColumns.Span[1] + columnsAfterInitial - 1}{PlayerNameRow}",
            };
        }

        protected override IResult<IEnumerable<ValueRange>> GetUpdateRangesForBonus(
            PhaseScore phaseScore, string[] teamIds, string sheetName, int row, int phasesCount)
        {
            Verify.IsNotNull(phaseScore, nameof(phaseScore));

            List<ValueRange> ranges = new List<ValueRange>();
            if (phaseScore.BonusScores?.Any() == true)
            {
                int bonusScoresIndex = Array.IndexOf(teamIds, phaseScore.BonusTeamId);
                if (bonusScoresIndex < 0)
                {
                    return new FailureResult<IEnumerable<ValueRange>>(
                        $"Unknown bonus team in phase {row - this.FirstPhaseRow + 1}. Cannot accurately create a scoresheet.");
                }

                int bonusTotal = phaseScore.BonusScores.Sum();
                if (bonusTotal != 0 && bonusTotal != 10 && bonusTotal != 20 && bonusTotal != 30)
                {
                    return new FailureResult<IEnumerable<ValueRange>>(
                        $"Invalid bonus value in phase {row - this.FirstPhaseRow + 1}. Value must be 0/10/20/30, but it was {bonusTotal}");
                }

                SpreadsheetColumn bonusColumn = this.BonusColumns.Span[bonusScoresIndex];
                ranges.Add(CreateUpdateSingleCellRequest(sheetName, bonusColumn, row, bonusTotal));
            }
            else if (row != this.FirstPhaseRow + phasesCount - 1 || phaseScore.ScoringSplitsOnActions.Any())
            {
                // We need to find if anyone got it correct, and if so, what team they are on. Fill that bonus
                // column with 0.
                ScoringSplitOnScoreAction split = phaseScore.ScoringSplitsOnActions
                    .FirstOrDefault(split => split.Action.Score > 0);
                if (split != null)
                {
                    // TODO: See if there's a better way to get the individual's team (add it when we add the buzz?)
                    // Risk is if there's a team name that is the same as someone's ID
                    // If it's an individual who is a team, then the teamId will be null, but their user ID may be a
                    // team ID.
                    int bonusIndex = Array.IndexOf(
                        teamIds, 
                        split.Action.Buzz.TeamId ?? split.Action.Buzz.UserId.ToString(CultureInfo.InvariantCulture));
                    if (bonusIndex < 0 || bonusIndex >= 2)
                    {
                        return new FailureResult<IEnumerable<ValueRange>>(
                            $"Unknown bonus team in phase {row - this.FirstPhaseRow + 1}. Cannot accurately create a scoresheet.");
                    }

                    ranges.Add(CreateUpdateSingleCellRequest(sheetName, this.BonusColumns.Span[bonusIndex], row, 0));
                }
            }

            return new SuccessResult<IEnumerable<ValueRange>>(ranges);
        }

        protected override IResult<List<ValueRange>> GetUpdateRangesForRoster(
            IReadOnlyDictionary<string, string> teamIdToNames, IEnumerable<IGrouping<string, PlayerTeamPair>> groupings)
        {
            Verify.IsNotNull(groupings, nameof(groupings));

            // Need to put teams in first row
            List<ValueRange> ranges = new List<ValueRange>();
            IEnumerable<string> teamNames = groupings
                .Select(grouping => teamIdToNames.TryGetValue(grouping.Key, out string name) ?
                    name :
                    grouping.First().PlayerDisplayName);
            ranges.Add(CreateUpdateCellsAlongRowRequest(RostersSheetName, RostersFirstTeamColumn, 1, teamNames));

            SpreadsheetColumn teamColumn = RostersFirstTeamColumn;
            foreach (IGrouping<string, PlayerTeamPair> grouping in groupings)
            {
                IEnumerable<string> playerNames = grouping.Select(pair => pair.PlayerDisplayName);
                ranges.Add(CreateUpdateCellsAlongColumnRequest(
                    RostersSheetName, teamColumn, RostersFirstPlayerRow, playerNames));

                teamColumn = teamColumn + 1;
            }

            return new SuccessResult<List<ValueRange>>(ranges);
        }
    }
}
