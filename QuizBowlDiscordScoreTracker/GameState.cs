using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace QuizBowlDiscordScoreTracker
{
    public class GameState
    {
        public const int ScoresListLimit = 10;

        private readonly SortedSet<Buzz> buzzQueue;
        private readonly HashSet<ulong> alreadyBuzzedPlayers;
        private readonly Dictionary<ulong, int> score;

        // TODO: To support undo we need to keep a stack of "score events", which says who scored when.
        // We could store deltas, but storing copies of the structures may be easier for now.

        private ulong? readerId;
        // TODO: We may want to add a set of people who have retrieved the score to prevent spamming. May be better
        // at the controller/bot level.

        private object collectionLock = new object();
        private object readerLock = new object();

        public GameState()
        {
            this.buzzQueue = new SortedSet<Buzz>();
            this.alreadyBuzzedPlayers = new HashSet<ulong>();
            this.score = new Dictionary<ulong, int>();
            this.ReaderId = null;
        }

        public ulong? ReaderId
        {
            get
            {
                lock (this.readerLock)
                {
                    return this.readerId;
                }
            }
            set
            {
                lock (this.readerLock)
                {
                    this.readerId = value;
                }
            }
        }

        public void ClearAll()
        {
            lock (collectionLock)
            {
                this.buzzQueue.Clear();
                this.alreadyBuzzedPlayers.Clear();
                this.score.Clear();
            }
            
            this.ReaderId = null;
        }

        public void ClearCurrentRound()
        {
            lock (collectionLock)
            {
                this.buzzQueue.Clear();
                this.alreadyBuzzedPlayers.Clear();
            }
        }

        public bool AddPlayer(ulong userId)
        {
            // readers cannot add themselves
            if (userId == this.ReaderId)
            {
                return false;
            }

            Buzz player = new Buzz()
            {
                // TODO: Consider taking this from the message. This would require passing in another parameter.
                Timestamp = DateTime.Now,
                UserId = userId
            };

            lock (collectionLock)
            {
                if (this.alreadyBuzzedPlayers.Contains(userId))
                {
                    return false;
                }

                this.buzzQueue.Add(player);
                this.alreadyBuzzedPlayers.Add(userId);
            }
            
            return true;
        }

        public bool WithdrawPlayer(ulong userId)
        {
            // readers cannot withdraw themselves
            if (userId == this.ReaderId)
            {
                return false;
            }

            int count = 0;
            lock (collectionLock)
            {
                if (alreadyBuzzedPlayers.Remove(userId))
                {
                    // Unless we change Buzz's Equals to only take the User into account then we have to go through the
                    // whole set to withdraw.
                    count = this.buzzQueue.RemoveWhere(buzz => buzz.UserId == userId);
                    Debug.Assert(count <= 1, "The same user should not be in the queue more than once.");
                }
            }

            return count > 0;
        }

        public IEnumerable<KeyValuePair<ulong, int>> GetScores()
        {
            lock (collectionLock)
            {
                // Return a sorted copy.
                return this.score.OrderByDescending(kvp => kvp.Value);
            }
        }

        public void ScorePlayer(int score)
        {
            lock (collectionLock)
            {
                Buzz buzz = this.buzzQueue.Min;
                if (buzz == null)
                {
                    // This is a bug we should log when logging is added.
                    Debug.Fail($"{nameof(this.ScorePlayer)} should not be called when there are no players in the queue.");
                    return;
                }

                if (score > 0)
                {
                    this.buzzQueue.Clear();
                    this.alreadyBuzzedPlayers.Clear();
                }
                else
                {
                    this.buzzQueue.Remove(buzz);
                }

                // TODO: We may want to limit what score can be, to protect against typos.
                // This could be something passed in through a command, too. Like a set/array of allowed scores.
                if (!this.score.TryGetValue(buzz.UserId, out int currentScore))
                {
                    currentScore = 0;
                }

                this.score[buzz.UserId] = currentScore + score;
            }
        }

        public bool TryGetNextPlayer(out ulong nextPlayerId)
        {
            Buzz next;
            lock (collectionLock)
            {
                next = this.buzzQueue.Min;
            }

            if (next == null)
            {
                nextPlayerId = 0;
                return false;
            }
            
            nextPlayerId = next.UserId;
            return true;
        }
    }
}
