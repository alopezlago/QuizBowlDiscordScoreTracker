namespace QuizBowlDiscordScoreTracker
{
    /// <summary>
    /// Stores the player's scoring split on a given score action.
    /// This is useful for cases where youw want to track the splits that occur on each buzz.
    /// </summary>
    public class ScoringSplitOnScoreAction
    {
        public ScoringSplit Split { get; set; }

        public ScoreAction Action { get; set; }
    }
}
