namespace QuizBowlDiscordScoreTracker
{
    public class TossupOnlyPhaseState : PhaseState
    {
        public TossupOnlyPhaseState(bool buzzQueueDisabled) : base(buzzQueueDisabled)
        {
        }

        public override PhaseStage CurrentStage => this.Actions.TryPeek(out ScoreAction result) && result.Score > 0 ?
                    PhaseStage.Complete :
                    PhaseStage.Tossup;
    }
}
