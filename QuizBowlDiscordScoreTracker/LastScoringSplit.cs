namespace QuizBowlDiscordScoreTracker
{
    public class LastScoringSplit
    {
        public string PlayerDisplayName { get; set; }

        public ulong PlayerId { get; set; }

        public ScoringSplit Split { get; set; }

        public string TeamId { get; set; }
    }
}
