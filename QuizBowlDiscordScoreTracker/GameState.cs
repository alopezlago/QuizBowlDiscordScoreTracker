using System;
using System.Collections.Generic;

namespace QuizBowlDiscordScoreTracker
{
    public partial class GameState
    {
        public const int ScoresListLimit = 200;

        private readonly LinkedList<PhaseState> phases;

        private ulong? readerId;
        private IDictionary<PlayerTeamPair, ScoringSplitOnScoreAction> cachedLastScoringSplit;
        private IEnumerable<IEnumerable<ScoringSplitOnScoreAction>> cachedSplitsPerPhase;

        private readonly object phasesLock = new object();
        private readonly object readerLock = new object();

        public GameState()
        {
            this.phases = new LinkedList<PhaseState>();
            this.cachedLastScoringSplit = null;
            this.cachedSplitsPerPhase = null;

            this.SetupInitialPhases();
            this.ReaderId = null;
        }

        public int PhaseNumber
        {
            get
            {
                lock (this.phasesLock)
                {
                    return this.phases.Count;
                }
            }
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

        private PhaseState CurrentPhase => this.phases.Last.Value;

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
                this.ClearCaches();
            }
        }

        public bool AddPlayer(ulong userId, string playerDisplayName, ulong? teamId = null)
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
                PlayerDisplayName = playerDisplayName,
                TeamId = teamId,
                UserId = userId
            };

            lock (this.phasesLock)
            {
                return this.CurrentPhase.AddBuzz(player);
            }
        }

        public bool WithdrawPlayer(ulong userId, ulong? userTeamId = null)
        {
            // readers cannot withdraw themselves
            if (userId == this.ReaderId)
            {
                return false;
            }

            lock (this.phasesLock)
            {
                return this.CurrentPhase.WithdrawPlayer(userId, userTeamId);
            }
        }

        public void EnsureCachedCollectionsExist()
        {
            if (this.cachedLastScoringSplit != null && this.cachedSplitsPerPhase != null)
            {
                return;
            }

            List<IEnumerable<ScoringSplitOnScoreAction>> splitsPerPhase = new List<IEnumerable<ScoringSplitOnScoreAction>>();
            IDictionary<PlayerTeamPair, ScoringSplitOnScoreAction> lastScoringSplits =
                new Dictionary<PlayerTeamPair, ScoringSplitOnScoreAction>();
            foreach (PhaseState phase in this.phases)
            {
                List<ScoringSplitOnScoreAction> splitsInPhase = new List<ScoringSplitOnScoreAction>();
                foreach (ScoreAction scoreAction in phase.OrderedScoreActions)
                {
                    // Try to get the split and clone it. If it doesn't exist, just make a new one.
                    PlayerTeamPair pair = new PlayerTeamPair(scoreAction.Buzz.UserId, scoreAction.Buzz.TeamId);
                    lastScoringSplits.TryGetValue(pair, out ScoringSplitOnScoreAction splitActionPair);
                    ScoringSplit newSplit = splitActionPair?.Split.Clone() ?? new ScoringSplit();
                    switch (scoreAction.Score)
                    {
                        case -5:
                            newSplit.Negs++;
                            break;
                        case 0:
                            newSplit.NoPenalties++;
                            break;
                        case 10:
                            newSplit.Gets++;
                            break;
                        case 15:
                            newSplit.Powers++;
                            break;
                        case 20:
                            newSplit.Superpowers++;
                            break;
                        default:
                            break;
                    }

                    ScoringSplitOnScoreAction newPair = new ScoringSplitOnScoreAction()
                    {
                        Action = scoreAction,
                        Split = newSplit
                    };

                    lastScoringSplits[pair] = newPair;
                    splitsInPhase.Add(newPair);
                }

                splitsPerPhase.Add(splitsInPhase);
            }

            this.cachedSplitsPerPhase = splitsPerPhase;
            this.cachedLastScoringSplit = lastScoringSplits;
        }

        public IDictionary<PlayerTeamPair, ScoringSplitOnScoreAction> GetLastScoringSplits()
        {
            lock (this.phasesLock)
            {
                this.EnsureCachedCollectionsExist();
                return this.cachedLastScoringSplit;
            }
        }

        public IEnumerable<IEnumerable<ScoringSplitOnScoreAction>> GetScoringActionsByPhase()
        {
            lock (this.phasesLock)
            {
                this.EnsureCachedCollectionsExist();
                return this.cachedSplitsPerPhase;
            }
        }

        public void NextQuestion()
        {
            lock (this.phasesLock)
            {
                // Add a new phase, since the last one is over
                this.phases.AddLast(new PhaseState());
            }
        }

        public void ScorePlayer(int score)
        {
            lock (this.phasesLock)
            {
                if (this.CurrentPhase.TryScore(score))
                {
                    this.ClearCaches();
                    // Player was correct, so move on to the next phase.
                    if (score > 0)
                    {
                        this.phases.AddLast(new PhaseState());
                    }
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

                // In the only case where nothing was undone, there's no score to calculate, so clearing the cache is harmless
                this.ClearCaches();

                return couldUndo;
            }
        }

        private void ClearCaches()
        {
            // TODO: If calculating the splits is too expensive, then look into just clearing the last
            // phase (or undoing the changes to the dictionary)
            this.cachedLastScoringSplit = null;
            this.cachedSplitsPerPhase = null;
        }

        private void SetupInitialPhases()
        {
            // We must always have one phase.
            this.ClearCaches();
            this.phases.Clear();
            this.phases.AddFirst(new PhaseState());
        }
    }
}
