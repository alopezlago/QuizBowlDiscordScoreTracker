namespace QuizBowlDiscordScoreTracker
{
    public class Format
    {
        public int HighestPhaseIndexWithBonus { get; set; }

        public int PhasesInRegulation { get; set; }

        public bool DisableBuzzQueue { get; set; }

        // TODO: Add information to handle tiebreakers
        // TODO: See if we can match the QB Schema to handle most cases

        public static Format CreateTossupShootout(bool disableBuzzQueue)
        {
            return new Format()
            {
                // No bonuses in a tossup shootout
                HighestPhaseIndexWithBonus = -1,
                PhasesInRegulation = int.MaxValue,
                DisableBuzzQueue = disableBuzzQueue
            };
        }

        public static Format CreateTossupBonusesShootout(bool disableBuzzQueue)
        {
            return new Format()
            {
                HighestPhaseIndexWithBonus = int.MaxValue,
                PhasesInRegulation = int.MaxValue,
                DisableBuzzQueue = disableBuzzQueue
            };
        }

        // TODO: We may need the team scores to determine if we have more phases. Move to a TryCreate pattern then?
        public IPhaseState CreateNextPhase(int phaseIndex)
        {
            if (phaseIndex > this.HighestPhaseIndexWithBonus)
            {
                return new TossupOnlyPhaseState(this.DisableBuzzQueue);
            }

            return new TossupBonusPhaseState(this.DisableBuzzQueue);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Format other))
            {
                return false;
            }

            return this.DisableBuzzQueue == other.DisableBuzzQueue &&
                this.HighestPhaseIndexWithBonus == other.HighestPhaseIndexWithBonus &&
                this.PhasesInRegulation == other.PhasesInRegulation;
        }

        public override int GetHashCode()
        {
            return this.DisableBuzzQueue.GetHashCode() ^
                this.HighestPhaseIndexWithBonus.GetHashCode() ^
                this.PhasesInRegulation.GetHashCode();
        }
    }
}
