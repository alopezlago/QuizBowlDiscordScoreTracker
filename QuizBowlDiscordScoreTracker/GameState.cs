using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    // TODO: Come up with the state machine. Some methods are here now, but the flow isn't solid yet.

    // TODO: Once we add tests add an interface for this.
    public class GameState
    {
        public const int ScoresListLimit = 10;

        private readonly SortedSet<Buzz> buzzQueue;
        private readonly HashSet<DiscordUser> alreadyBuzzedPlayers;
        private readonly Dictionary<DiscordUser, int> score;

        // TODO: We may want to add a set of people who have retrieved the score to prevent spamming. May be better
        // at the controller/bot level.

        private object collectionLock = new object();

        public GameState()
        {
            this.buzzQueue = new SortedSet<Buzz>();
            this.alreadyBuzzedPlayers = new HashSet<DiscordUser>();
            this.score = new Dictionary<DiscordUser, int>();
            this.Reader = null;
        }

        // We may want a lock for this, but conflicts here are much less likely
        public DiscordUser Reader { get; set; }

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
                // TODO: Should this come from the message?
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
            }
            
            return true;
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
                    return;
                }

                if (score > 0)
                {
                    // Correct
                    this.buzzQueue.Clear();
                    this.alreadyBuzzedPlayers.Clear();
                }
                else
                {
                    this.buzzQueue.Remove(buzz);
                    this.alreadyBuzzedPlayers.Add(buzz.User);
                }

                // TODO: We may want to limit what score can be, to protect against typos.
                // This could be something passed in through a command, too. Like a set/array of allowed scores.
                if (!this.score.TryGetValue(buzz.User, out int currentScore))
                {
                    currentScore = 0;
                }

                // We may have to make this a thread-safe dictionary.
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
