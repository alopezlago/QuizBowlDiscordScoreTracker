using System;
using System.Collections.Generic;

namespace QuizBowlDiscordScoreTracker
{
    public class PhaseScore
    {
        public PhaseScore()
        {
            this.ScoringSplitsOnActions = Array.Empty<ScoringSplitOnScoreAction>();
            this.BonusScores = Array.Empty<int>();
        }

        public IEnumerable<ScoringSplitOnScoreAction> ScoringSplitsOnActions { get; set; }

        public string BonusTeamId { get; set; }

        public IEnumerable<int> BonusScores { get; set; }
    }
}
