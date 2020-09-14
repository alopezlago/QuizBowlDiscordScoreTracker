namespace QuizBowlDiscordScoreTracker
{
    public class ScoringSplit
    {
        public int Negs { get; set; }

        public int NoPenalties { get; set; }

        public int Gets { get; set; }

        public int Powers { get; set; }

        public int Superpowers { get; set; }

        public int Points => checked(-5 * this.Negs + 10 * this.Gets + 15 * this.Powers + 20 * this.Superpowers);

        public ScoringSplit Clone()
        {
            return (ScoringSplit)this.MemberwiseClone();
        }
    }
}
