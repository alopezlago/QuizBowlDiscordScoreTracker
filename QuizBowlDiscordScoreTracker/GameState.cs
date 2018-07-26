using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace QuizBowlDiscordScoreTracker
{
    // TODO: Add interfaces for DiscordUser/DiscordChannel so we can add unit tests.
    public class GameState
    {
        public const int ScoresListLimit = 10;

        private readonly SortedSet<Buzz> buzzQueue;
        private readonly HashSet<DiscordUser> alreadyBuzzedPlayers;
        private readonly Dictionary<DiscordUser, int> score;

        private DiscordUser reader;
        // TODO: We may want to add a set of people who have retrieved the score to prevent spamming. May be better
        // at the controller/bot level.

        private object collectionLock = new object();
        private object readerLock = new object();

        public GameState()
        {
            this.buzzQueue = new SortedSet<Buzz>();
            this.alreadyBuzzedPlayers = new HashSet<DiscordUser>();
            this.score = new Dictionary<DiscordUser, int>();
            this.Reader = null;
        }

        public DiscordUser Reader
        {
            get
            {
                lock (this.readerLock)
                {
                    return this.reader;
                }
            }
            set
            {
                lock (this.readerLock)
                {
                    this.reader = value;
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
            
            this.Reader = null;
        }

        public void ClearCurrentRound()
        {
            lock (collectionLock)
            {
                this.buzzQueue.Clear();
                this.alreadyBuzzedPlayers.Clear();
            }
        }

        public bool AddPlayer(DiscordUser user)
        {
            // readers cannot add themselves
            if (user == this.Reader)
            {
                return false;
            }

            Buzz player = new Buzz()
            {
                // TODO: Consider taking this from the message. This would require passing in another parameter.
                Timestamp = DateTime.Now,
                User = user
            };

            lock (collectionLock)
            {
                if (this.alreadyBuzzedPlayers.Contains(user))
                {
                    return false;
                }

                this.buzzQueue.Add(player);
                this.alreadyBuzzedPlayers.Add(user);
            }
            
            return true;
        }

        public bool WithdrawPlayer(DiscordUser user)
        {
            // readers cannot withdraw themselves
            if (user == this.Reader)
            {
                return false;
            }

            int count = 0;
            lock (collectionLock)
            {
                if (alreadyBuzzedPlayers.Remove(user))
                {
                    // Unless we change Buzz's Equals to only take the User into account then we have to go through the
                    // whole set to withdraw.
                    count = this.buzzQueue.RemoveWhere(buzz => buzz.User == user);
                    Debug.Assert(count <= 1, "The same user should not be in the queue more than once.");
                }
            }

            return count > 0;
        }

        public IEnumerable<KeyValuePair<DiscordUser, int>> GetScores()
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
                if (!this.score.TryGetValue(buzz.User, out int currentScore))
                {
                    currentScore = 0;
                }

                this.score[buzz.User] = currentScore + score;
            }
        }

        public bool TryGetNextPlayer(out DiscordUser nextPlayer)
        {
            Buzz next;
            lock (collectionLock)
            {
                next = this.buzzQueue.Min;
            }
            
            nextPlayer = next?.User;
            return nextPlayer != null;
        }
    }
}
