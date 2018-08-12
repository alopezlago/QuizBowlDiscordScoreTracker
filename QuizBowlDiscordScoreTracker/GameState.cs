using System;
using System.Collections.Generic;
using System.Linq;

namespace QuizBowlDiscordScoreTracker
{
    public class GameState
    {
        public const int ScoresListLimit = 10;

        private readonly LinkedList<PhaseState> phases;
        
        private ulong? readerId;
        // TODO: We may want to add a set of people who have retrieved the score to prevent spamming. May be better
        // at the controller/bot level.

        // TODO: Investigate whether we really need phasesLock if the PhaseState has locks on its own collection.
        private object phasesLock = new object();
        private object readerLock = new object();

        public GameState()
        {
            this.phases = new LinkedList<PhaseState>();
            this.SetupInitialPhases();
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

        private PhaseState CurrentPhase
        {
            get
            {
                return this.phases.Last.Value;
            }
        }

        public void ClearAll()
        {
            lock (this.phasesLock)
            {
                this.SetupInitialPhases();
            }
            
            this.ReaderId = null;
        }

        public void ClearCurrentRound()
        {
            lock (this.phasesLock)
            {
                this.CurrentPhase.Clear();
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

            lock (this.phasesLock)
            {
                return this.CurrentPhase.AddBuzz(player);
            }
        }

        public bool WithdrawPlayer(ulong userId)
        {
            // readers cannot withdraw themselves
            if (userId == this.ReaderId)
            {
                return false;
            }

            lock (this.phasesLock)
            {
                return this.CurrentPhase.WithdrawPlayer(userId);
            }
        }

        public IEnumerable<KeyValuePair<ulong, int>> GetScores()
        {
            lock (this.phasesLock)
            {
                // TODO: Investigate the performance of this approach.
                //     - Quick test on my machine shows that even with 1 million phases it takes ~35 ms. That's still
                //       not great, but it's about the same as looping through with a for loop. We may want to cache thes
                //       results, though it will need to be synchronized with Undo.
                // This gets all of the score pairs from the phases, groups them together, sums the values in the
                // grouping, and then sorts it.
                return this.phases
                    .SelectMany(phase => phase.Scores)
                    .GroupBy(kvp => kvp.Key, kvp => kvp.Value)
                    .Select(grouping => new KeyValuePair<ulong, int>(grouping.Key, grouping.Sum()))
                    .OrderByDescending(kvp => kvp.Value);
            }
        }

        public void ScorePlayer(int score)
        {
            lock (this.phasesLock)
            {
                if (this.CurrentPhase.TryScore(score) && score > 0)
                {
                    // Player was correct, so move on to the next phase.
                    this.phases.AddLast(new PhaseState());
                }
            }
        }

        public bool TryGetNextPlayer(out ulong nextPlayerId)
        {
            lock (this.phasesLock)
            {
                return this.CurrentPhase.TryGetNextPlayer(out nextPlayerId);
            }
        }

        public bool Undo(out ulong userId)
        {
            lock (this.phasesLock)
            {
                // There are three cases:
                // - The phase has actions that we can undo. Just undo the action and return true.
                // - The phase does not have actions to undo, but the previous phase does. Remove the current phase, go
                //   to the previous one, and undo that one.
                // - We haven't had any actions (start of 1st phase), so there is nothing to undo.
                bool couldUndo = this.CurrentPhase.Undo(out userId);
                while (!couldUndo && this.phases.Count > 1)
                {
                    this.phases.RemoveLast();
                    couldUndo = this.CurrentPhase.Undo(out userId);
                }

                return couldUndo;
            }
        }

        private void SetupInitialPhases()
        {
            // We must always have one phase.
            this.phases.Clear();
            this.phases.AddFirst(new PhaseState());
        }
    }
}
