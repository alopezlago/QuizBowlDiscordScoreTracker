namespace QuizBowlDiscordScoreTracker
{
    public class Format
    {
        // This controls the format for games. Acf is unused for now, but will be used when we support a touranment mode
        public static readonly Format Acf = new Format()
        {
            HighestPhaseIndexWithBonus = 20 - 1,
            PhasesInRegulation = 20
        };

        public static readonly Format TossupShootout = new Format()
        {
            // No bonuses in a tossup shootout
            HighestPhaseIndexWithBonus = -1,
            PhasesInRegulation = int.MaxValue
        };

        public static readonly Format TossupBonusesShootout = new Format()
        {
            HighestPhaseIndexWithBonus = int.MaxValue,
            PhasesInRegulation = int.MaxValue
        };

        public int HighestPhaseIndexWithBonus { get; set; }

        public int PhasesInRegulation { get; set; }

        // TODO: Add information to handle tiebreakers
        // TODO: See if we can match the QB Schema to handle most cases

        // TODO: We may need the team scores to determine if we have more phases. Move to a TryCreate pattern then?
        public IPhaseState CreateNextPhase(int phaseIndex)
        {
            if (phaseIndex > this.HighestPhaseIndexWithBonus)
            {
                return new TossupOnlyPhaseState();
            }

            return new TossupBonusPhaseState();
        }
    }
}
