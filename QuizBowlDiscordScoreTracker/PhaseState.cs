using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace QuizBowlDiscordScoreTracker
{
    public abstract class PhaseState : IPhaseState
    {
        // We may be able to get rid of this lock, if we rely on the host keeping it consistent.
        private readonly object collectionLock = new object();

        public PhaseState()
        {
            this.BuzzQueue = new SortedSet<Buzz>();
            this.AlreadyBuzzedPlayerIds = new HashSet<ulong>();
            this.AlreadyScoredTeamIds = new HashSet<string>();
            this.Actions = new Stack<ScoreAction>();
        }

        public abstract PhaseStage CurrentStage { get; }

        public IEnumerable<ScoreAction> OrderedScoreActions => this.Actions.Reverse();

        // These classes need to be used by the inheriting class
        internal Stack<ScoreAction> Actions { get; }

        internal HashSet<ulong> AlreadyBuzzedPlayerIds { get; }

        internal HashSet<string> AlreadyScoredTeamIds { get; }

        internal SortedSet<Buzz> BuzzQueue { get; }

        // We could add players to it, but if the team has buzzed already, we can skip/eject them from the queue
        public bool AddBuzz(Buzz player)
        {
            lock (this.collectionLock)
            {
                Verify.IsNotNull(player, nameof(player));
                string teamId = GetTeamId(player);
                if (player == null ||
                    this.AlreadyBuzzedPlayerIds.Contains(player.UserId) ||
                    this.AlreadyScoredTeamIds.Contains(teamId))
                {
                    return false;
                }

                this.BuzzQueue.Add(player);
                this.AlreadyBuzzedPlayerIds.Add(player.UserId);
            }

            return true;
        }

        public void Clear()
        {
            lock (this.collectionLock)
            {
                this.Actions.Clear();
                this.AlreadyBuzzedPlayerIds.Clear();
                this.BuzzQueue.Clear();
            }
        }

        public virtual bool TryScoreBuzz(int score)
        {
            lock (this.collectionLock)
            {
                Buzz buzz = this.GetNextPlayerToPrompt();
                if (buzz == null)
                {
                    // This is a bug we should log when logging is added.
                    Debug.Fail($"{nameof(this.TryScoreBuzz)} should not be called when there are no players in the queue.");
                    return false;
                }

                this.BuzzQueue.Remove(buzz);

                ScoreAction action = new ScoreAction(buzz, score);
                this.Actions.Push(action);
                this.AlreadyScoredTeamIds.Add(GetTeamId(buzz));
                return true;
            }
        }

        public bool TryGetNextPlayer(out ulong nextPlayerId)
        {
            Buzz next = null;
            lock (this.collectionLock)
            {
                // Only get the next player if there hasn't been a buzz that was correct.
                if (!this.Actions.TryPeek(out ScoreAction action) || action.Score <= 0)
                {
                    next = this.GetNextPlayerToPrompt();
                }
            }

            if (next == null)
            {
                nextPlayerId = 0;
                return false;
            }

            nextPlayerId = next.UserId;
            return true;
        }

        public bool WithdrawPlayer(ulong userId, string userTeamId)
        {
            int count = 0;
            lock (this.collectionLock)
            {
                if (this.AlreadyBuzzedPlayerIds.Remove(userId))
                {
                    // Unless we change Buzz's Equals to only take the User into account then we have to go through the
                    // whole set to withdraw.
                    count = this.BuzzQueue.RemoveWhere(buzz => buzz.UserId == userId);
                    this.AlreadyScoredTeamIds.Remove(GetTeamId(userTeamId, userId));
                    Debug.Assert(count <= 1, "The same user should not be in the queue more than once.");
                }
            }

            return count > 0;
        }

        /// <summary>
        /// Undoes the last action in the phase, if possible
        /// </summary>
        /// <returns>True if we could undo an action. False if there were no actions to undo.</returns>
        public virtual bool Undo(out ulong? userId)
        {
            lock (this.collectionLock)
            {
                if (!this.Actions.TryPop(out ScoreAction action))
                {
                    userId = null;
                    return false;
                }

                userId = action.Buzz.UserId;

                // We shouldn't need to change the list of already buzzed players, because in order to have an action the
                // player must've buzzed in. We do need to add the player back to the queue. The queue should be sorted by
                // the timing of the buzz, so consecutive undos should place the players back in the right order.
                this.BuzzQueue.Add(action.Buzz);
                this.AlreadyScoredTeamIds.Remove(GetTeamId(action.Buzz));
                return true;
            }
        }

        internal static string GetTeamId(Buzz player)
        {
            // If the player isn't on a team, treat them as being on their own team (they're player ID, which should be
            // distinct in Discord)
            return GetTeamId(player.TeamId, player.UserId);
        }

        internal static string GetTeamId(string teamId, ulong userId)
        {
            return teamId ?? userId.ToString(CultureInfo.InvariantCulture);
        }

        internal Buzz GetNextPlayerToPrompt()
        {
            // This would normally be buzzQueue.Min, and without teams we could do that, but because we support Undo
            // we sometimes have to keep buzzes that don't get prompted in the queue.
            // The scenario: The buzzes are A1 B1 B2 C1 -> A1 gets -5, B1 gets -5, C1 is prompted, reader undos and
            // withdraws B1 (perhaps because their connection dropped)
            // If we want to prompt B2, then we cannot remove B2 from the buzz queue when we prompt C1. This means we
            // need to keep these buzzes around, so we must search for the next correct buzz
            // In realistic games this should basically be .Min
            return this.BuzzQueue.FirstOrDefault(buzz => !this.AlreadyScoredTeamIds.Contains(GetTeamId(buzz)));
        }
    }
}
