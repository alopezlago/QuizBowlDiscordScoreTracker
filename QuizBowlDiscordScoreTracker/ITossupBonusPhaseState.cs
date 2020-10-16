using System.Collections.Generic;

namespace QuizBowlDiscordScoreTracker
{
    public interface ITossupBonusPhaseState
    {
        IReadOnlyCollection<int> BonusScores { get; }
        bool HasBonus { get; }

        bool TryScoreBonus(string bonusScore);
    }
}