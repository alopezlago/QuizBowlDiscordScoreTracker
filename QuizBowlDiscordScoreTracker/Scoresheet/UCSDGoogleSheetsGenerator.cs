using System;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.Sheets.v4.Data;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    // TODO: Eventually allow for more columns than Z (can even just do up to ZZ: +500 unrealistic for QB)
    // TODO: Add reader name to mod/scorekeeper section

    public sealed class UCSDGoogleSheetsGenerator : BaseGoogleSheetsGenerator
    {
        internal const string RostersSheetName = "Rosters";
        internal static readonly ReadOnlyMemory<SpreadsheetColumn> StartingColumnsArray = new SpreadsheetColumn[]
        {
            new SpreadsheetColumn('C'),
            new SpreadsheetColumn('O')
        };

        private static readonly SpreadsheetColumn[] BonusColumnsArray = new SpreadsheetColumn[]
        {
            new SpreadsheetColumn('I'),
            new SpreadsheetColumn('U')
        };
        private static readonly bool[] ClearedBonusArray = new bool[] { false, false, false };
        private static readonly SpreadsheetColumn FirstColumn = new SpreadsheetColumn(1);

        private static readonly List<string> ClearRosters = new List<string>() { $"'{RostersSheetName}'!A2:G999" };

        public UCSDGoogleSheetsGenerator(IGoogleSheetsApi sheetsApi) : base(sheetsApi)
        {
        }

        protected override List<string> ClearRostersRanges => ClearRosters;

        internal override int FirstPhaseRow => 4;

        internal override int LastBonusRow => 27;

        internal override int PhasesLimit => 28;

        protected override int PlayerNameRow => 3;

        internal override int PlayersPerTeamLimit => 6;

        internal override ReadOnlyMemory<SpreadsheetColumn> StartingColumns => StartingColumnsArray;

        protected override ReadOnlyMemory<SpreadsheetColumn> BonusColumns => BonusColumnsArray;

        protected override int TeamsLimit => 100;

        protected override int TeamNameRow => 1;

        protected override IResult<IEnumerable<ValueRange>> GetAdditionalUpdateRangesForTossup(
            PhaseScore phaseScore, string[] teamIds, string sheetName, int row, int phasesCount)
        {
            return new SuccessResult<IEnumerable<ValueRange>>(Enumerable.Empty<ValueRange>());
        }

        protected override IResult<List<ValueRange>> GetUpdateRangesForRoster(
            IReadOnlyDictionary<string, string> teamIdToNames,
            IEnumerable<IGrouping<string, PlayerTeamPair>> groupings)
        {
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
                ranges.Add(CreateUpdateCellsAlongRowRequest(RostersSheetName, FirstColumn, row, rowValues));

                row++;
            }

            return new SuccessResult<List<ValueRange>>(ranges);
        }

        protected override string GetSheetName(int roundNumber)
        {
            return $"Round {roundNumber}";
        }

        protected override IResult<IEnumerable<ValueRange>> GetUpdateRangesForBonus(
            PhaseScore phaseScore, string[] teamIds, string sheetName, int row, int phasesCount)
        {
            Verify.IsNotNull(phaseScore, nameof(phaseScore));

            List<ValueRange> ranges = new List<ValueRange>();
            if (phaseScore.BonusScores?.Any() == true)
            {
                int bonusPartCount = phaseScore.BonusScores.Count();
                if (bonusPartCount != 3)
                {
                    return new FailureResult<IEnumerable<ValueRange>>(
                        $"Non-three part bonus in phase {row - this.FirstPhaseRow + 1}. Number of parts: {bonusPartCount}. These aren't supported for the scoresheet.");
                }

                int bonusScoresIndex = Array.IndexOf(teamIds, phaseScore.BonusTeamId);
                if (bonusScoresIndex < 0)
                {
                    return new FailureResult<IEnumerable<ValueRange>>(
                        $"Unknown bonus team in phase {row - this.FirstPhaseRow + 1}. Cannot accurately create a scoresheet.");
                }

                SpreadsheetColumn bonusColumn = this.BonusColumns.Span[bonusScoresIndex];
                ranges.Add(CreateUpdateCellsAlongRowRequest(
                    sheetName, bonusColumn, row, phaseScore.BonusScores.Select(score => score > 0).ToArray()));

                SpreadsheetColumn otherBonusColumn = this.BonusColumns.Span[this.BonusColumns.Length - bonusScoresIndex - 1];
                ranges.Add(CreateUpdateCellsAlongRowRequest(
                    sheetName, otherBonusColumn, row, ClearedBonusArray));
            }
            else
            {
                // Clear the bonus columns
                for (int i = 0; i < this.BonusColumns.Span.Length; i++)
                {
                    ranges.Add(CreateUpdateCellsAlongRowRequest(
                        sheetName, this.BonusColumns.Span[i], row, ClearedBonusArray));
                }
            }

            return new SuccessResult<IEnumerable<ValueRange>>(ranges);
        }

        protected override List<string> GetClearRangesForBonus(string sheetName)
        {
            int columnsAfterInitial = this.PlayersPerTeamLimit - 1;
            return new List<string>()
            {
                // Add 5 to the column because the sheet supports 6 players. The first column counts as the first
                // player already.
                $"'{sheetName}'!{this.StartingColumns.Span[0]}4:{this.StartingColumns.Span[0] + columnsAfterInitial}31",
                $"'{sheetName}'!{this.StartingColumns.Span[1]}4:{this.StartingColumns.Span[1] + columnsAfterInitial}31",
            };
        }
    }
}
