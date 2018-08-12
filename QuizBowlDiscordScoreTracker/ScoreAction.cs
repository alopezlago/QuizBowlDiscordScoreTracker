namespace QuizBowlDiscordScoreTracker
{
    // TODO: Look into storing only the deltas of the collections, so that we don't copy the collection for each
    // action.
    // Tracks who buzzed in, what the score was, and who was in the already buzzed list.
    public class ScoreAction
    {
        public ScoreAction(Buzz buzz, int score)
        {
            this.Buzz = buzz;
            this.Score = score;
        }

        public Buzz Buzz { get; private set; }

        public int Score { get; private set; }
    }
}
