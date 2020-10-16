using System;

namespace QuizBowlDiscordScoreTracker
{
    public class BonusStats
    {
        public static readonly BonusStats Default = new BonusStats()
        {
            Heard = 0,
            Total = 0
        };

        public int Heard { get; set; }

        public int Total { get; set; }

        // Don't show NaN, show 0 instead if no bonuses have been heard
        public double PointsPerBonus => this.Heard == 0 ? 0 : Math.Round(((double)this.Total) / this.Heard, 2);
    }
}
