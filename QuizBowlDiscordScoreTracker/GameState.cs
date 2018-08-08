using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace QuizBowlDiscordScoreTracker
{
    public class GameState
    {
        public const int ScoresListLimit = 10;
        public const int UndoStackLimit = 10;

        private readonly SortedSet<Buzz> buzzQueue;
        private readonly HashSet<ulong> alreadyBuzzedPlayers;
        private readonly Dictionary<ulong, int> score;
        private readonly LinkedList<ScoreAction> undoStack;

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
            this.undoStack = new LinkedList<ScoreAction>();
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
                this.undoStack.Clear();
            }
            
            this.ReaderId = null;
        }

        public void ClearCurrentRound()
        {
            lock (collectionLock)
            {
                this.buzzQueue.Clear();
                this.alreadyBuzzedPlayers.Clear();
                this.undoStack.Clear();
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

                // Push this before we modify the collections, so we can get the original state.
                this.PushToUndoQueue(buzz, score);

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

        public bool Undo(out ulong undoId)
        {
            lock (this.collectionLock)
            {
                if (undoStack.Count == 0)
                {
                    undoId = 0;
                    return false;
                }

                ScoreAction action = undoStack.First.Value;
                undoStack.RemoveFirst();

                this.score[action.Buzz.UserId] -= action.Score;
                if (action.Score > 0)
                {
                    // These fields are read-only, so clear and add the results.
                    this.alreadyBuzzedPlayers.Clear();
                    this.alreadyBuzzedPlayers.UnionWith(action.AlreadyBuzzedPlayers);
                    this.buzzQueue.Clear();
                    this.buzzQueue.UnionWith(action.BuzzQueue);
                }
                else
                {
                    this.alreadyBuzzedPlayers.Remove(action.Buzz.UserId);
                    this.buzzQueue.Add(action.Buzz);
                }

                undoId = action.Buzz.UserId;
                return true;
            }
        }

        // This should be called with the collection lock wrapping it
        private void PushToUndoQueue(Buzz buzz, int score)
        {
            if (undoStack.Count > UndoStackLimit)
            {
                undoStack.RemoveLast();
            }

            undoStack.AddFirst(new ScoreAction(buzz, score, this));
        }

        // TODO: Move this to a class we can test?
        // Could collect these in a list (remove last one), then in the object which stores this list, Undo would
        // take the id, update the score dictionary to undo the score, and remove it from alreadyBuzzedPlayers.
        // Alternative is to change the round state. Have separate RoundState object for already buzzed and current
        // player, and GameState stores this and Score.
        // we only need the set if the buzz was positive, because we have to add them back.
        private class ScoreAction
        {
            // Tracks who buzzed in, what the score was, and who was in the already buzzed list.
            // We may want read-only/invariant collections for already-buzzed.
            // An alternative is to keep track of all of the structures, so you can undo clears/ends

            public ScoreAction(Buzz buzz, int score, GameState state)
            {
                // Don't modify the existing collections.
                this.Buzz = buzz;
                this.Score = score;
                this.AlreadyBuzzedPlayers = new HashSet<ulong>(state.alreadyBuzzedPlayers);
                this.BuzzQueue = new SortedSet<Buzz>(state.buzzQueue);
            }

            public Buzz Buzz { get; private set; }

            public int Score { get; private set; }

            // This will only have a value if score > 0, because we need to remember who buzzed in.
            public HashSet<ulong> AlreadyBuzzedPlayers { get; private set; }

            public SortedSet<Buzz> BuzzQueue { get; private set; }
        }
    }
}
