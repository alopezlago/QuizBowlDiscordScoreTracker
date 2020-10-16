namespace QuizBowlDiscordScoreTracker
{
    public class TossupOnlyPhaseState : PhaseState
    {
        public override PhaseStage CurrentStage => this.Actions.TryPeek(out ScoreAction result) && result.Score > 0 ?
                    PhaseStage.Complete :
                    PhaseStage.Tossup;
    }
}
