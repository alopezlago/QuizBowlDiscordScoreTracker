using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTracker
{
    public class GameState
    {
        // Add a maximum number of phases, to prevent any denial-of-service issues
        public const int MaximumPhasesCount = 2000;
        public const int ScoresListLimit = 200;

        private static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create();

        private readonly LinkedList<IPhaseState> phases;

        private ulong? readerId;
        private Format format;

        private IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> cachedLastScoringSplit;
        private IEnumerable<PhaseScore> cachedPhaseScoresPerPhase;
        private IDictionary<string, BonusStats> cachedBonusStats;

        private readonly object phasesLock = new object();
        private readonly object readerLock = new object();

        public GameState(bool disableBuzzQueue = false)
        {
            this.phases = new LinkedList<IPhaseState>();
            this.cachedLastScoringSplit = null;
            this.cachedPhaseScoresPerPhase = null;
            this.format = Format.CreateTossupShootout(disableBuzzQueue);
            this.TeamManager = SoloOnlyTeamManager.Instance;

            // Generate a 64-bit cryptographically secure random string for the ID
            byte[] randomBytes = new byte[8];
            Random.GetBytes(randomBytes);

            // TODO: Should the replacement happen when we need to escape this (for a URL)? If we do it here, escape
            // = and / and replace it with something like @ and ~. Alternatively, just return a number
            this.Id = Convert.ToBase64String(randomBytes);

            this.SetupInitialPhases();
            this.ReaderId = null;
        }

        public PhaseStage CurrentStage
        {
            get
            {
                lock (this.phasesLock)
                {
                    return this.CurrentPhase.CurrentStage;
                }
            }
        }

        public string Id { get; }

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

        public Format Format
        {
            get => this.format;
            set
            {
                // We need to remove the current phase and use the new format
                this.format = value;
                this.phases.RemoveLast();
                this.TryAddNextPhase();
            }
        }

        public ITeamManager TeamManager { get; set; }

        private IPhaseState CurrentPhase => this.phases.Last.Value;

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

        public async Task<bool> AddPlayer(ulong userId, string playerDisplayName)
        {
            // readers cannot add themselves
            if (userId == this.ReaderId)
            {
                return false;
            }

            string teamId = await this.TeamManager.GetTeamIdOrNull(userId);
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

        public async Task<bool> WithdrawPlayer(ulong userId)
        {
            // readers cannot withdraw themselves
            if (userId == this.ReaderId)
            {
                return false;
            }

            string teamId = await this.TeamManager.GetTeamIdOrNull(userId);

            lock (this.phasesLock)
            {
                return this.CurrentPhase.WithdrawPlayer(userId, teamId);
            }
        }

        public async Task<IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit>> GetLastScoringSplits()
        {
            IEnumerable<PlayerTeamPair> knownPlayers = await this.TeamManager.GetKnownPlayers();

            lock (this.phasesLock)
            {
                this.EnsureCachedCollectionsExist(knownPlayers);
                return this.cachedLastScoringSplit;
            }
        }

        public async Task<IEnumerable<PhaseScore>> GetPhaseScores()
        {
            IEnumerable<PlayerTeamPair> knownPlayers = await this.TeamManager.GetKnownPlayers();

            lock (this.phasesLock)
            {
                this.EnsureCachedCollectionsExist(knownPlayers);
                return this.cachedPhaseScoresPerPhase;
            }
        }

        public async Task<IReadOnlyDictionary<string, BonusStats>> GetBonusStats()
        {
            IEnumerable<PlayerTeamPair> knownPlayers = await this.TeamManager.GetKnownPlayers();

            lock (this.phasesLock)
            {
                this.EnsureCachedCollectionsExist(knownPlayers);
                return (IReadOnlyDictionary<string, BonusStats>)this.cachedBonusStats;
            }
        }

        public void NextQuestion()
        {
            lock (this.phasesLock)
            {
                // If we're on a bonus, score it as a 0
                if (this.CurrentStage == PhaseStage.Bonus &&
                    this.CurrentPhase is ITossupBonusPhaseState tossupBonusPhaseState)
                {
                    tossupBonusPhaseState.TryScoreBonus("0");
                    this.ClearCaches();
                }

                // Add a new phase, since the last one is over
                this.TryAddNextPhase();
            }
        }

        public bool TryScoreBonus(string bonusScore)
        {
            lock (this.phasesLock)
            {
                if (this.CurrentPhase.CurrentStage != PhaseStage.Bonus ||
                    !(this.CurrentPhase is ITossupBonusPhaseState bonusPhaseState) ||
                    !bonusPhaseState.TryScoreBonus(bonusScore))
                {
                    return false;
                }

                this.ClearCaches();
                return this.TryAddNextPhase();
            }
        }

        public void ScorePlayer(int score)
        {
            lock (this.phasesLock)
            {
                if (this.CurrentPhase.TryScoreBuzz(score))
                {
                    this.ClearCaches();

                    // Player was correct, so move on to the next phase.
                    if (score > 0 && this.CurrentStage == PhaseStage.Complete)
                    {
                        this.TryAddNextPhase();
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

        public bool Undo(out ulong? userId)
        {
            lock (this.phasesLock)
            {
                // There are four cases:
                // - The phase has actions that we can undo. Just undo the action and return true.
                // - The phase does not have actions to undo, but the previous phase does. Remove the current phase, go
                //   to the previous one, and undo that one.
                // - The phase does not have any actions to undo, nor does the previous phase, but a previous phase
                //   exists. Go back to that phase, and return no user to prompt.
                // - If we couldn't undo the current phase and we're at the beginning, then there's nothing to undo
                bool couldUndo = this.CurrentPhase.Undo(out userId);
                if (!couldUndo && this.phases.Count > 1)
                {
                    this.phases.RemoveLast();

                    // If the new CurrentPhase isn't complete, then we must've used NextQuestion, since the bonus or
                    // tossup would've been scored otherwise. Don't undo the phase again, since we're undoing NextQuestion
                    couldUndo = this.CurrentStage != PhaseStage.Complete || this.CurrentPhase.Undo(out userId);
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
            this.cachedPhaseScoresPerPhase = null;
            this.cachedBonusStats = null;
        }

        private void EnsureCachedCollectionsExist(IEnumerable<PlayerTeamPair> knownPlayers)
        {
            if (this.cachedLastScoringSplit != null &&
                this.cachedPhaseScoresPerPhase != null &&
                this.cachedBonusStats != null)
            {
                return;
            }

            List<PhaseScore> splitsPerPhase = new List<PhaseScore>();
            IDictionary<PlayerTeamPair, LastScoringSplit> lastScoringSplits = knownPlayers
                .ToDictionary(
                    pair => pair,
                    pair => new LastScoringSplit()
                    {
                        PlayerId = pair.PlayerId,
                        Split = new ScoringSplit(),
                        TeamId = pair.TeamId
                    });
            IDictionary<string, BonusStats> combinedBonusStats = new Dictionary<string, BonusStats>();

            foreach (PhaseState phase in this.phases)
            {
                List<ScoringSplitOnScoreAction> scoringSplitsOnActions = new List<ScoringSplitOnScoreAction>();
                foreach (ScoreAction scoreAction in phase.OrderedScoreActions)
                {
                    // Try to get the split and clone it. If it doesn't exist, just make a new one.
                    PlayerTeamPair pair = new PlayerTeamPair(
                        scoreAction.Buzz.UserId, scoreAction.Buzz.PlayerDisplayName, scoreAction.Buzz.TeamId);
                    bool hasLastSplit = lastScoringSplits.TryGetValue(pair, out LastScoringSplit lastSplit);
                    ScoringSplit newSplit = hasLastSplit ? lastSplit.Split.Clone() : new ScoringSplit();
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

                    // Use the buzz's TeamId instead of the pair's, since the pair will fill in the playerID if the
                    // teamID was null
                    lastSplit = new LastScoringSplit()
                    {
                        PlayerDisplayName = scoreAction.Buzz.PlayerDisplayName,
                        PlayerId = pair.PlayerId,
                        Split = newSplit,
                        TeamId = scoreAction.Buzz.TeamId
                    };

                    lastScoringSplits[pair] = lastSplit;
                    scoringSplitsOnActions.Add(newPair);
                }

                PhaseScore phaseScore = new PhaseScore()
                {
                    ScoringSplitsOnActions = scoringSplitsOnActions,
                    BonusScores = Array.Empty<int>()
                };

                if (phase is ITossupBonusPhaseState tossupBonusPhase && tossupBonusPhase.HasBonus)
                {
                    // Someone must've buzzed in correctly at the end if we have a bonus
                    ScoreAction action = phase.Actions.Peek();
                    phaseScore.BonusTeamId = action.Buzz.TeamId ?? action.Buzz.UserId.ToString(CultureInfo.InvariantCulture);
                    phaseScore.BonusScores = tossupBonusPhase.BonusScores;

                    if (!combinedBonusStats.TryGetValue(phaseScore.BonusTeamId, out BonusStats bonusStats))
                    {
                        bonusStats = new BonusStats();
                    }

                    bonusStats.Heard++;
                    bonusStats.Total += tossupBonusPhase.BonusScores.Sum();
                    combinedBonusStats[phaseScore.BonusTeamId] = bonusStats;
                }

                splitsPerPhase.Add(phaseScore);
            }

            this.cachedPhaseScoresPerPhase = splitsPerPhase;
            this.cachedLastScoringSplit = (IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit>)lastScoringSplits;
            this.cachedBonusStats = combinedBonusStats;
        }

        private bool TryAddNextPhase()
        {
            if (this.phases.Count > MaximumPhasesCount)
            {
                return false;
            }

            this.phases.AddLast(this.format.CreateNextPhase(this.phases.Count));
            return true;
        }

        private void SetupInitialPhases()
        {
            // We must always have one phase.
            this.ClearCaches();
            this.phases.Clear();
            this.phases.AddFirst(this.Format.CreateNextPhase(0));
        }
    }
}
