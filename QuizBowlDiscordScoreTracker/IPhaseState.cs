using System.Collections.Generic;

namespace QuizBowlDiscordScoreTracker
{
    public interface IPhaseState
    {
        IEnumerable<ScoreAction> OrderedScoreActions { get; }
        PhaseStage CurrentStage { get; }

        bool AddBuzz(Buzz player);
        void Clear();
        bool TryGetNextPlayer(out ulong nextPlayerId);
        bool TryScoreBuzz(int score);
        bool Undo(out ulong? userId);
        bool WithdrawPlayer(ulong userId, string userTeamId);
    }
}
