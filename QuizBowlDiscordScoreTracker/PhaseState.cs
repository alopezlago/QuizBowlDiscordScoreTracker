using System.Collections.Generic;
using System.Diagnostics;

namespace QuizBowlDiscordScoreTracker
{
    public class PhaseState
    {
        private readonly SortedSet<Buzz> buzzQueue;
        private readonly HashSet<ulong> alreadyBuzzedPlayers;
        private readonly Stack<ScoreAction> actions;
        private readonly Dictionary<ulong, ScoreAction> scores;

        // We may be able to get rid of this lock, if we rely on the host keeping it consistent.
        private readonly object collectionLock = new object();

        public PhaseState()
        {
            this.buzzQueue = new SortedSet<Buzz>();
            this.alreadyBuzzedPlayers = new HashSet<ulong>();
            this.actions = new Stack<ScoreAction>();
            this.scores = new Dictionary<ulong, ScoreAction>();
        }

        // We don't need to order them here.
        public IEnumerable<KeyValuePair<ulong, ScoreAction>> Scores => this.scores;

        public bool AddBuzz(Buzz player)
        {
            lock (this.collectionLock)
            {
                if (player == null || this.alreadyBuzzedPlayers.Contains(player.UserId))
                {
                    return false;
                }

                this.buzzQueue.Add(player);
                this.alreadyBuzzedPlayers.Add(player.UserId);
            }

            return true;
        }

        public void Clear()
        {
            lock (this.collectionLock)
            {
                this.actions.Clear();
                this.alreadyBuzzedPlayers.Clear();
                this.buzzQueue.Clear();
            }
        }

        public bool TryScore(int score)
        {
            Buzz buzz = this.buzzQueue.Min;
            if (buzz == null)
            {
                // This is a bug we should log when logging is added.
                Debug.Fail($"{nameof(this.TryScore)} should not be called when there are no players in the queue.");
                return false;
            }

            this.buzzQueue.Remove(buzz);

            // TODO: Should we verify that this.scores doesn't have an action for that user already?
            ScoreAction action = new ScoreAction(buzz, score);
            this.scores[buzz.UserId] = action;
            this.actions.Push(action);

            return true;
        }

        public bool TryGetNextPlayer(out ulong nextPlayerId)
        {
            Buzz next = null;
            lock (this.collectionLock)
            {
                // Only get the next player if there hasn't been a buzz that was correct.
                if (!this.actions.TryPeek(out ScoreAction action) || action.Score <= 0)
                {
                    next = this.buzzQueue.Min;
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

        public bool WithdrawPlayer(ulong userId)
        {
            int count = 0;
            lock (this.collectionLock)
            {
                if (this.alreadyBuzzedPlayers.Remove(userId))
                {
                    // Unless we change Buzz's Equals to only take the User into account then we have to go through the
                    // whole set to withdraw.
                    count = this.buzzQueue.RemoveWhere(buzz => buzz.UserId == userId);
                    Debug.Assert(count <= 1, "The same user should not be in the queue more than once.");
                }
            }

            return count > 0;
        }

        /// <summary>
        /// Undoes the last action in the phase, if possible
        /// </summary>
        /// <returns>True if we could undo an action. False if there were no actions to undo.</returns>
        public bool Undo(out ulong userId)
        {
            if (!this.actions.TryPop(out ScoreAction action))
            {
                userId = 0;
                return false;
            }

            userId = action.Buzz.UserId;
            this.scores.Remove(userId);

            // We shouldn't need to change the list of already buzzed players, because in order to have an action the
            // player must've buzzed in. We do need to add the player back to the queue. The queue should be sorted by
            // the timing of the buzz, so consecutive undos should place the players back in the right order.
            this.buzzQueue.Add(action.Buzz);
            return true;
        }
    }
}
