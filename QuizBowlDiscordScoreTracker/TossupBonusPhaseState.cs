using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuizBowlDiscordScoreTracker
{
    public class TossupBonusPhaseState : PhaseState, ITossupBonusPhaseState
    {
        // Bonuses are generally 3 parts. We might support non-3 part bonuses in the future, so keep this flexible
        internal const int DefaultBonusLength = 3;

        private static readonly Regex SplitsRegex = new Regex(
            $"^(0|10)(\\s*/\\s*(0|10)){{{DefaultBonusLength - 1}}}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        private static readonly Regex BinarySplitsRegex = new Regex(
            $"^(0|1){{{DefaultBonusLength}}}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        private List<int> bonusScores;

        public TossupBonusPhaseState() : base()
        {
            this.bonusScores = null;
        }

        public bool HasBonus => this.BonusScores != null;

        public IReadOnlyCollection<int> BonusScores => this.bonusScores;

        public override PhaseStage CurrentStage
        {
            get
            {
                if (!this.HasBonus)
                {
                    return PhaseStage.Tossup;
                }

                return this.BonusScores.Count > 0 ? PhaseStage.Complete : PhaseStage.Bonus;
            }
        }

        public bool TryScoreBonus(string bonusScore)
        {
            Verify.IsNotNull(bonusScore, nameof(bonusScore));

            // Formats that are supported
            // - Total points when the parts can be inferred (0, 30)
            // - Splits (0/0/0, 0/0/10, 10/10/10, etc.)
            // - Binary-style splits (000, 001, 111, etc.)
            bonusScore = bonusScore.Trim();
            if (bonusScore == "0")
            {
                for (int i = 0; i < DefaultBonusLength; i++)
                {
                    this.bonusScores.Add(0);
                }

                return true;
            }
            else if (bonusScore == "30")
            {
                for (int i = 0; i < DefaultBonusLength; i++)
                {
                    this.bonusScores.Add(10);
                }

                return true;
            }

            Match match = SplitsRegex.Match(bonusScore);
            if (match.Success)
            {
                string[] values = bonusScore.Split('/', DefaultBonusLength);
                foreach (string value in values)
                {
                    string trimmedValue = value.Trim();

                    // We have a real problem if we can't parse 0 or 10
                    int numericValue = int.Parse(trimmedValue, CultureInfo.InvariantCulture);
                    this.bonusScores.Add(numericValue);
                }

                return true;
            }

            match = BinarySplitsRegex.Match(bonusScore);
            if (match.Success)
            {
                foreach (char value in bonusScore)
                {
                    // We have a real problem if we can't parse the digits
                    int numericValue = value == '1' ? 10 : 0;
                    this.bonusScores.Add(numericValue);
                }

                return true;
            }

            return false;
        }

        public override bool TryScoreBuzz(int score)
        {
            bool result = base.TryScoreBuzz(score);
            if (result && score > 0)
            {
                // We have bonuses
                this.bonusScores = new List<int>(DefaultBonusLength);
            }

            return result;
        }

        public override bool Undo(out ulong? userId)
        {
            if (this.HasBonus && this.BonusScores.Count > 0)
            {
                userId = null;
                this.bonusScores.Clear();
                return true;
            }

            // If we haven't scored the bonus, we're undoing something from the tossup phase.
            // Make sure the bonus scores collection is null, then undo whatever happened during the tossup.
            this.bonusScores = null;
            return base.Undo(out userId);
        }
    }
}
