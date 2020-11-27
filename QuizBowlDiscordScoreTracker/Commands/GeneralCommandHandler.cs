﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.TeamManager;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class GeneralCommandHandler
    {
        internal const int MaxLeadersShown = 5;
        internal const int MaxTeamsShown = 10;
        private const string GameReportTitle = "Game Report";

        // These characters are two characters in length, so we can't use string indexing as a trick
        // Used by tests
        internal static readonly string[] Medals = new string[] { "🥇", "🥈", "🥉" };

        private static readonly ILogger Logger = Log.ForContext(typeof(GeneralCommandHandler));

        public GeneralCommandHandler(
            ICommandContext context,
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
        {
            this.Context = context;
            this.DatabaseActionFactory = dbActionFactory;
            this.Manager = manager;
            this.Options = options;
        }

        private ICommandContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        public Task AboutAsync()
        {
            string version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
            EmbedBuilder embedBuilder = new EmbedBuilder()
            {
                Color = Color.Gold,
                Title = "About",
                Description = $"QuizBowlScoreTracker v{version}. For the list of changes in this version, visit " +
                $"https://github.com/alopezlago/QuizBowlDiscordScoreTracker/releases/tag/v{version}"
            };
            return this.Context.Channel.SendMessageAsync(embed: embedBuilder.Build());
        }

        public async Task JoinTeamAsync(string teamName)
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) &&
                this.Context.User is IGuildUser guildUser))
            {
                // This command only works during a game
                return;
            }

            if (!(game.TeamManager is ISelfManagedTeamManager teamManager))
            {
                // TODO: Should we look at the database and see if the team prefix is set?
                await this.Context.Channel.SendMessageAsync("Joining teams isn't supported in this mode.");
                return;
            }

            if (!teamManager.TryAddPlayerToTeam(
                this.Context.User.Id, guildUser.Nickname ?? guildUser.Username, teamName))
            {
                await this.Context.Channel.SendMessageAsync(
                    $@"Couldn't join team ""{teamName}"". Make sure it is not misspelled.");
                return;
            }

            string teamId = await game.TeamManager.GetTeamIdOrNull(this.Context.User.Id);
            IReadOnlyDictionary<string,string> teamNames= await game.TeamManager.GetTeamIdToNames();
            teamName = teamNames[teamId];
            await this.Context.Channel.SendMessageAsync($@"{guildUser.Mention} is on team ""{teamName}""");
        }

        public Task LeaveTeamAsync()
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) &&
                this.Context.User is IGuildUser guildUser))
            {
                // This command only works during a game
                return Task.CompletedTask;
            }

            if (!(game.TeamManager is ISelfManagedTeamManager teamManager))
            {
                // TODO: Should we look at the database and see if the team prefix is set?
                return this.Context.Channel.SendMessageAsync("Leaving teams isn't supported in this mode.");
            }

            string name = guildUser.Nickname ?? guildUser.Username;
            if (!teamManager.TryRemovePlayerFromTeam(this.Context.User.Id))
            {
                return this.Context.Channel.SendMessageAsync($@"""{name}"" isn't on a team.");
            }

            // We don't want to ping the user when they left, so use their nickname/username
            return this.Context.Channel.SendMessageAsync($@"""{name}"" left their team.");
        }

        public async Task GetTeamsAsync()
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) &&
                this.Context.User is IGuildUser guildUser))
            {
                // This command only works during a game
                return;
            }

            IEnumerable<string> teamNames = (await game.TeamManager.GetTeamIdToNames()).Values;
            if (!teamNames.Any())
            {
                await this.Context.Channel.SendMessageAsync(game.TeamManager.JoinTeamDescription);
                return;
            }

            string teams;
            IEnumerable<string> orderedTeamNames = teamNames.OrderBy(name => name).Take(MaxTeamsShown);
            int teamsCount = teamNames.Count();
            if (teamsCount > MaxTeamsShown)
            {
                int remainingTeamsCount = teamsCount - MaxTeamsShown;
                teams = $"{string.Join(", ", orderedTeamNames)}, and {remainingTeamsCount} " +
                    $"other{(remainingTeamsCount == 1 ? string.Empty : "s")}...";
            }
            else
            {
                teams = string.Join(", ", orderedTeamNames);
            }

            await this.Context.Channel.SendMessageAsync($"Teams: {teams}");
        }

        public async Task GetGameReportAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState currentGame) ||
                currentGame?.ReaderId == null ||
                !(this.Context.Channel is IGuildChannel guildChannel))
            {
                return;
            }

            IEnumerable<PhaseScore> phaseScores = await currentGame.GetPhaseScores();

            // If there's been no buzzes in the last question, don't show it in the report (could be end of the packet)
            PhaseScore lastQuestion = phaseScores.LastOrDefault();
            if (lastQuestion?.ScoringSplitsOnActions?.Any() != true)
            {
                phaseScores = phaseScores.SkipLast(1);
            }

            IEnumerable<ScoringSplit> splits = phaseScores
                .SelectMany(pairs => pairs.ScoringSplitsOnActions.Select(pair => pair.Split));
            HighestPointsLevel highestPointsLevel = FindHighestPointLevel(splits);

            // Because we only have the scoring splits here, we have to rely on buzzes having a team ID
            bool hasTeams = phaseScores
                .Any(scoresInPhase => scoresInPhase.ScoringSplitsOnActions.Any(pair => pair.Action.Buzz.TeamId != null));
            IReadOnlyDictionary<string, string> teamIdToName = await currentGame.TeamManager.GetTeamIdToNames();

            int scoresByQuestionCount = phaseScores.Count();
            int questionsReported = await this.Context.Channel.SendAllEmbeds(
                phaseScores,
                () => new EmbedBuilder()
                {
                    Title = GameReportTitle,
                    Color = Color.Gold
                },
                (phaseScore, index) =>
                    GetEmbedFieldForPhase(
                        currentGame, phaseScore, teamIdToName, highestPointsLevel, index, index == scoresByQuestionCount - 1));

            if (questionsReported > 0)
            {
                return;
            }

            EmbedBuilder embedBuilder = new EmbedBuilder()
            {
                Title = GameReportTitle,
                Color = Color.Gold,
                Description = "No questions read or answered yet."
            };
            await this.Context.Channel.SendMessageAsync(embed: embedBuilder.Build());
        }

        public async Task SetReaderAsync()
        {
            IGuildUser user = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
            if (user == null)
            {
                // If the reader doesn't exist anymore, don't start a game.
                return;
            }

            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState state) ||
                this.Manager.TryCreate(this.Context.Channel.Id, out state)))
            {
                // Couldn't add a new reader.
                return;
            }
            else if (state.ReaderId != null)
            {
                // We already have a reader, so do nothing.
                return;
            }

            string readerRolePrefix;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                readerRolePrefix = await action.GetReaderRolePrefixAsync(this.Context.Guild.Id);
            }

            if (!user.CanRead(this.Context.Guild, readerRolePrefix))
            {
                await this.Context.Channel.SendMessageAsync(
                    @$"{user.Mention} can't read because they don't have a role starting with the prefix ""{readerRolePrefix}"".");
                return;
            }

            state.ReaderId = this.Context.User.Id;

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                     "Game started in guild '{0}' in channel '{1}'", guildChannel.Guild.Name, guildChannel.Name);
            }

            // Prevent a cold start on the first buzz, and eagerly get the team prefix and channel pair
            string teamRolePrefix;
            bool useBonuses;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.GetPairedVoiceChannelIdOrNullAsync(this.Context.Channel.Id);
                teamRolePrefix = await action.GetTeamRolePrefixAsync(this.Context.Guild.Id);
                useBonuses = await action.GetUseBonuses(this.Context.Guild.Id);
            }

            // Set teams here, if they are using roles.
            if (teamRolePrefix != null)
            {
                state.TeamManager = new ByRoleTeamManager(this.Context.Guild, teamRolePrefix);
            }
            else
            {
                state.TeamManager = new ByCommandTeamManager();
            }

            // Set the format here. Eventually we'll want to use more information to determine the format
            state.Format = useBonuses ? Format.TossupBonusesShootout : Format.TossupShootout;

            string baseMessage = this.Options.CurrentValue.WebBaseURL == null ?
                $"{this.Context.User.Mention} is the reader." :
                $"{this.Context.User.Mention} is the reader. Please visit {this.Options.CurrentValue.WebBaseURL}?{this.Context.Channel.Id} to hear buzzes.";
            string teamManagementMessage = teamRolePrefix == null ?
                "The reader can add teams through !addTeam *teamName*, and players can join teams with !join *teamName*. See !help for more team-based commands." :
                $@"Teams are set by the server role. Team roles begin with ""{teamRolePrefix}"".";
            await this.Context.Channel.SendMessageAsync($"{baseMessage}\n{teamManagementMessage}");
        }

        public async Task GetScoreAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState currentGame) ||
                currentGame?.ReaderId == null ||
                !(this.Context.Channel is IGuildChannel guildChannel))
            {
                return;
            }

            IGuildUser guildBotUser = await this.Context.Guild.GetCurrentUserAsync();
            ChannelPermissions channelPermissions = guildBotUser.GetPermissions(guildChannel);
            if (!channelPermissions.EmbedLinks)
            {
                await this.Context.Channel.SendMessageAsync(
                    "This bot must have \"Embed Links\" permissions to show the score");
                return;
            }

            IEnumerable<KeyValuePair<PlayerTeamPair, LastScoringSplit>> scoringSplits =
                await currentGame.GetLastScoringSplits();
            HighestPointsLevel highestPointsLevel = FindHighestPointLevel(
                scoringSplits.Where(kvp => kvp.Value != null).Select(kvp => kvp.Value.Split));

            bool hasTeams = scoringSplits.Any(kvp => kvp.Key.IsOnTeam);
            int embedsSentCount = hasTeams ?
                await this.ShowScoreForTeams(currentGame, scoringSplits, highestPointsLevel) :
                await this.ShowScoreForShootout(currentGame, scoringSplits, highestPointsLevel);

            if (embedsSentCount == 0)
            {
                await this.Context.Channel.SendMessageAsync("No one has scored yet");
            }
        }

        private static async Task AppendIndividualLeadersMessage(GameState state, StringBuilder valueBuilder)
        {
            IEnumerable<LastScoringSplit> lastSplits = (await state.GetLastScoringSplits()).Values;
            IReadOnlyDictionary<string, BonusStats> bonusStats = await state.GetBonusStats();

            IDictionary<string, LastScoringSplit> playerIdSplitPairs = lastSplits
                .Where(lastSplit => lastSplit.PlayerDisplayName != null)
                .ToDictionary(
                    lastSplit => lastSplit.PlayerId.ToString(CultureInfo.InvariantCulture),
                    lastSplit => lastSplit);
            IDictionary<LastScoringSplit, int> playerTotalPoints = new Dictionary<LastScoringSplit, int>(
                playerIdSplitPairs.Count);
            foreach (KeyValuePair<string, LastScoringSplit> playerIdSplitPair in playerIdSplitPairs)
            {
                int total = playerIdSplitPair.Value.Split.Points;
                if (bonusStats.TryGetValue(playerIdSplitPair.Key, out BonusStats bonusStat))
                {
                    total += bonusStat.Total;
                }

                playerTotalPoints[playerIdSplitPair.Value] = total;
            }

            IGrouping<int, LastScoringSplit> topLastSplits = playerTotalPoints
                .GroupBy(kvp => kvp.Value, kvp => kvp.Key)
                .OrderByDescending(grouping => grouping.Key)
                .FirstOrDefault();
            if (topLastSplits == null)
            {
                return;
            }

            IEnumerable<string> boldedNames = topLastSplits
                .Take(MaxLeadersShown)
                .Select(split => $"**{EscapeText(split.PlayerDisplayName)}**");
            int boldedNamesCount = boldedNames.Count();
            string verb = boldedNamesCount > 1 ? "are" : "is";
            string playersList = string.Join(", ", boldedNames);

            int topLastSplitsCount = topLastSplits.Count();
            int countDifferences = topLastSplitsCount - boldedNamesCount;
            string playersEtAlList = countDifferences == 0 ?
                playersList :
                $"{playersList}, and {countDifferences} other{(countDifferences == 1 ? string.Empty : "s")}";
            valueBuilder.AppendLine($"> {playersEtAlList} {verb} in the lead.");
        }

        private static async Task AppendTeamLeadersMessage(
            GameState state, IReadOnlyDictionary<string, string> teamIdToName, StringBuilder valueBuilder)
        {
            // Group the teams by their score, then select the grouping with the highest score
            IDictionary<string, int> teamScores = await GetTeamScores(state);
            IEnumerable<KeyValuePair<string, int>> topTeamScores = teamScores
                .GroupBy(kvp => kvp.Value)
                .OrderByDescending(grouping => grouping.Key)
                .SelectMany(grouping => grouping);

            IEnumerable<LastScoringSplit> lastSplits = (await state.GetLastScoringSplits()).Values;
            Dictionary<string, string> playerIdToName = new Dictionary<string, string>();
            foreach (LastScoringSplit lastSplit in lastSplits.Where(lastSplit => lastSplit.TeamId == null))
            {
                string key = lastSplit.PlayerId.ToString(CultureInfo.InvariantCulture);
                playerIdToName[key] = lastSplit.PlayerDisplayName;
            }

            IEnumerable<string> teamNamesAndScores = topTeamScores.Take(MaxLeadersShown).Select(kvp =>
            {
                if (!(teamIdToName.TryGetValue(kvp.Key, out string teamName) ||
                    playerIdToName.TryGetValue(kvp.Key, out teamName)))
                {
                    teamName = "*<Unknown>*";
                }

                return $"**{EscapeText(teamName)}** {kvp.Value}";
            });

            string teamScoresText = teamScores.Count > MaxLeadersShown ?
                $"{string.Join(", ", teamNamesAndScores)}, ..." :
                string.Join(", ", teamNamesAndScores);
            valueBuilder.AppendLine($"> Top score{(teamScores.Count != 1 ? "s" : string.Empty)}: {teamScoresText}");
        }

        private static void AddBonusStatline(StringBuilder valueBuilder, BonusStats bonusStats)
        {
            // This makes us always display the first two decimal places. Unfortunately, it shows 0 as .00, so we need
            // to handle 0 as a special case
            string pointsPerBonus = bonusStats.Total == 0 ?
                "0.00" :
                string.Format(CultureInfo.InvariantCulture, "{0:#.00}", bonusStats.PointsPerBonus);
            valueBuilder.AppendLine(
                $"Bonuses heard: {bonusStats.Heard}    Points: {bonusStats.Total}    PPB: {pointsPerBonus}");
        }

        private static void AddSplits(
            StringBuilder valueBuilder, ScoringSplit split, HighestPointsLevel highestPointsLevel)
        {
            valueBuilder.Append('(');
            if (highestPointsLevel == HighestPointsLevel.Superpower)
            {
                valueBuilder.Append(split.Superpowers);
                valueBuilder.Append('/');
            }

            if (highestPointsLevel != HighestPointsLevel.Get)
            {
                valueBuilder.Append(split.Powers);
                valueBuilder.Append('/');
            }

            valueBuilder.Append($"{split.Gets}/{split.Negs})");
        }

        private static string EscapeText(string name)
        {
            if (name == null)
            {
                return "<Unknown>";
            }

            // Escape all characters that are used for formatting
            return Regex.Replace(name, "([*_~|`\\>])", "\\$1", RegexOptions.Compiled);
        }

        private static HighestPointsLevel FindHighestPointLevel(IEnumerable<ScoringSplit> splits)
        {
            HighestPointsLevel level = HighestPointsLevel.Get;

            foreach (ScoringSplit split in splits)
            {
                if (split.Superpowers > 0)
                {
                    // super-powers implies there are powers.
                    return HighestPointsLevel.Superpower;
                }

                if (level == HighestPointsLevel.Get && split.Powers > 0)
                {
                    level = HighestPointsLevel.Power;
                }
            }

            return level;
        }

        private static async Task<EmbedFieldBuilder> GetEmbedFieldForPhase(
            GameState state,
            PhaseScore phaseScore,
            IReadOnlyDictionary<string, string> teamIdToName,
            HighestPointsLevel highestPointsLevel,
            int index,
            bool isLastPhase)
        {
            StringBuilder valueBuilder = new StringBuilder();
            bool answeredCorrectly = false;
            foreach (ScoringSplitOnScoreAction pair in phaseScore.ScoringSplitsOnActions)
            {
                valueBuilder.Append("> ");
                switch (pair.Action.Score)
                {
                    case -5:
                        valueBuilder.Append("Negged by ");
                        break;
                    case 0:
                        valueBuilder.Append("Incorrectly answered by ");
                        break;
                    case 10:
                        valueBuilder.Append("Correctly answered by ");
                        answeredCorrectly = true;
                        break;
                    case 15:
                        valueBuilder.Append("Powered by ");
                        answeredCorrectly = true;
                        break;
                    case 20:
                        valueBuilder.Append("Superpowered by ");
                        answeredCorrectly = true;
                        break;
                    default:
                        break;
                }

                valueBuilder.Append($"**{EscapeText(pair.Action.Buzz.PlayerDisplayName)}** ");

                if (pair.Action.Buzz.TeamId != null &&
                    teamIdToName.TryGetValue(pair.Action.Buzz.TeamId, out string teamName))
                {
                    valueBuilder.Append($"({teamName}) ");
                }

                AddSplits(valueBuilder, pair.Split, highestPointsLevel);
                valueBuilder.AppendLine(".");
            }

            if (!answeredCorrectly)
            {
                valueBuilder.AppendLine("> Question went dead.");
            }
            else if (phaseScore.BonusTeamId != null)
            {
                valueBuilder.Append("> ");

                int totalPoints = phaseScore.BonusScores.Sum();
                string splits = string.Join('/', phaseScore.BonusScores);
                string text = teamIdToName.TryGetValue(phaseScore.BonusTeamId, out string teamName) ?
                    $"**{teamName}** scored {totalPoints} on the bonus ({splits})." :
                    $"Scored {totalPoints} on the bonus ({splits}).";
                valueBuilder.AppendLine(text);
            }

            if (isLastPhase)
            {
                if (teamIdToName.Count == 0)
                {
                    await AppendIndividualLeadersMessage(state, valueBuilder);
                }
                else
                {
                    await AppendTeamLeadersMessage(state, teamIdToName, valueBuilder);
                }
            }

            return new EmbedFieldBuilder()
            {
                Name = $"**Question {index + 1}**",
                Value = valueBuilder.ToString()
            };
        }

        private static async Task<IDictionary<string, int>> GetTeamScores(GameState state)
        {
            IEnumerable<IGrouping<string, int>> lastScoringSplits =
                (await state.GetLastScoringSplits())
                    .GroupBy(
                        kvp => kvp.Key.TeamId ?? kvp.Key.PlayerId.ToString(CultureInfo.InvariantCulture),
                        kvp => kvp.Value.Split.Points);
            IDictionary<string, int> scores = lastScoringSplits
                .ToDictionary(grouping => grouping.Key, grouping => grouping.Sum());

            IReadOnlyDictionary<string, BonusStats> bonusStats = await state.GetBonusStats();
            foreach (KeyValuePair<string, BonusStats> kvp in bonusStats)
            {
                if (!scores.TryGetValue(kvp.Key, out int total))
                {
                    total = 0;
                }

                scores[kvp.Key] = total + kvp.Value.Total;
            }

            return scores;
        }

        private static int[] GetTopThreeScores(IOrderedEnumerable<KeyValuePair<string, int>> orderedTeamScores)
        {
            // We may not have 3 scorers in our splits, so fill in the values with the previous scores or 0
            int[] topThreeScores = new int[3];
            int[] topExistingScores = orderedTeamScores
                .Select(kvp => kvp.Value)
                .Take(topThreeScores.Length)
                .ToArray();
            int i = 0;
            while (i < topExistingScores.Length)
            {
                topThreeScores[i] = topExistingScores[i];
                i++;
            }

            int lastTopScore = topExistingScores.Length > 0 ? topExistingScores[^1] : 0;
            while (i < topThreeScores.Length)
            {
                topThreeScores[i] = lastTopScore;
                i++;
            }

            return topThreeScores;
        }

        private async Task<int> ShowScoreForShootout(
            GameState currentGame,
            IEnumerable<KeyValuePair<PlayerTeamPair, LastScoringSplit>> scoringSplits,
            HighestPointsLevel highestPointsLevel)
        {
            IDictionary<string, int> teamScores = await GetTeamScores(currentGame);

            IOrderedEnumerable<KeyValuePair<string, int>> orderedTeamScores = teamScores
                .OrderByDescending(kvp => kvp.Value);
            IDictionary<string, int> topOrderedTeamScoresMap = teamScores
                .OrderByDescending(kvp => kvp.Value)
                .Take(GameState.ScoresListLimit)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // We should take the top ones from the "team", since it includes bonus points
            IOrderedEnumerable<KeyValuePair<PlayerTeamPair, LastScoringSplit>> topOrderedScoringSplits = scoringSplits
                .Where(kvp => topOrderedTeamScoresMap.ContainsKey(kvp.Key.TeamId))
                .OrderByDescending(kvp => topOrderedTeamScoresMap[kvp.Key.TeamId]);

            IOrderedEnumerable<KeyValuePair<PlayerTeamPair, LastScoringSplit>> orderedIndividualScoringSplits = scoringSplits
                .OrderByDescending(kvp => kvp.Value.Split.Points);
            IReadOnlyDictionary<string, BonusStats> bonusStats = await currentGame.GetBonusStats();
            int[] topThreeScores = GetTopThreeScores(orderedTeamScores);

            // We could have more than the embed limit, so split them up if necessary
            return await this.Context.Channel.SendAllEmbeds(
                topOrderedScoringSplits,
                () =>
                {
                    return new EmbedBuilder
                    {
                        Title = orderedIndividualScoringSplits.Take(checked(GameState.ScoresListLimit + 1)).Count() > GameState.ScoresListLimit ?
                            $"Top {GameState.ScoresListLimit} Scores" :
                            "Scores",
                        Color = Color.Gold
                    };
                },
                async (kvp, index) =>
                {
                    StringBuilder valueBuilder = new StringBuilder();
                    ScoringSplit split = kvp.Value.Split;
                    string name = kvp.Value.PlayerDisplayName;
                    if (name == null)
                    {
                        IGuildUser user = await this.Context.Guild.GetUserAsync(kvp.Key.PlayerId);
                        name = user != null ? (user.Nickname ?? user.Username) : "<Unknown>";
                    }

                    string escapedName = EscapeText(name);

                    if (!teamScores.TryGetValue(kvp.Key.TeamId, out int points))
                    {
                        points = split.Points;
                    }

                    // If the player has one of the top three scores (or is tied with one of them), show the medal for
                    // it.
                    int topScoreIndex = Array.IndexOf(topThreeScores, points);
                    if (topScoreIndex >= 0)
                    {
                        escapedName = $"{Medals[topScoreIndex]} {escapedName}";
                    }

                    if (!bonusStats.TryGetValue(kvp.Key.TeamId, out BonusStats bonusStat))
                    {
                        bonusStat = BonusStats.Default;
                    }

                    int totalPoints = split.Points + bonusStat.Total;

                    valueBuilder.Append($"**{totalPoints}** ");
                    AddSplits(valueBuilder, split, highestPointsLevel);

                    int noPenalties = split.NoPenalties;
                    if (noPenalties > 0)
                    {
                        valueBuilder.Append($" ({noPenalties} no penalty buzz{(noPenalties != 1 ? "es" : "")})");
                    }

                    if (bonusStat != BonusStats.Default)
                    {
                        valueBuilder.AppendLine();
                        AddBonusStatline(valueBuilder, bonusStat);
                    }

                    return new EmbedFieldBuilder()
                    {
                        Name = escapedName,
                        Value = valueBuilder.ToString()
                    };
                });
        }

        private async Task<int> ShowScoreForTeams(
            GameState state,
            IEnumerable<KeyValuePair<PlayerTeamPair, LastScoringSplit>> scoringSplits,
            HighestPointsLevel highestPointsLevel)
        {
            IDictionary<string, int> teamScores = await GetTeamScores(state);
            IDictionary<string, int> topOrderedTeamScoresMap = teamScores
                .OrderByDescending(kvp => kvp.Value)
                .Take(GameState.ScoresListLimit)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Order the splits by the team score, using the map of teams to scores.
            IOrderedEnumerable<IGrouping<string, LastScoringSplit>> topOrderedScoringSplits = scoringSplits
                .GroupBy(
                    kvp => kvp.Key.TeamId ?? kvp.Key.PlayerId.ToString(CultureInfo.InvariantCulture),
                    kvp => kvp.Value)
                .Where(grouping => topOrderedTeamScoresMap.ContainsKey(grouping.Key))
                .OrderByDescending(grouping => topOrderedTeamScoresMap[grouping.Key]);

            IReadOnlyDictionary<string, string> teamIdToName = await state.TeamManager.GetTeamIdToNames();
            IReadOnlyDictionary<string, BonusStats> bonusStats = await state.GetBonusStats();

            // Show top teams in description
            (string teamName, int points)[] topTeamScores = await Task.WhenAll(topOrderedScoringSplits
                .Select(async grouping =>
                {
                    // This is where we should use teamScores
                    LastScoringSplit lastSplit = grouping.FirstOrDefault();
                    string teamId = lastSplit == null ?
                        grouping.Key :
                        lastSplit.TeamId ?? lastSplit.PlayerId.ToString(CultureInfo.InvariantCulture);
                    if (!teamScores.TryGetValue(teamId, out int points))
                    {
                        points = grouping?.Sum(value => value.Split.Points) ?? 0;
                    }

                    // TODO: See if we can simplify this
                    if (lastSplit == null)
                    {
                        // TODO: See if this will just show the player ID sometimes
                        return (EscapeText(grouping.Key), 0);
                    }

                    if (teamIdToName.TryGetValue(teamId, out string teamName))
                    {
                        return (EscapeText(teamName), points);
                    }

                    teamName = grouping.FirstOrDefault()?.PlayerDisplayName;
                    if (teamName == null)
                    {
                        IGuildUser user = await this.Context.Guild.GetUserAsync(lastSplit.PlayerId);
                        teamName = user != null ? (user.Nickname ?? user.Username) : "<Unknown>";
                    }

                    return (EscapeText(teamName), points);
                }));

            IEnumerable<string> teamScoresInTitle = topTeamScores.Take(3).Select(tuple => $"{tuple.teamName} {tuple.points}");
            string title = topTeamScores.Length > 3 ?
                $"{string.Join(", ", teamScoresInTitle)}, ..." :
                string.Join(", ", teamScoresInTitle);

            // We could have more than the embed limit, so split them up if necessary
            return await this.Context.Channel.SendAllEmbeds(
                topOrderedScoringSplits,
                () =>
                {
                    return new EmbedBuilder
                    {
                        Title = title,
                        Color = Color.Gold,
                        Description = "Individual splits and scores below"
                    };
                },
                async (grouping, index) =>
                {
                    StringBuilder valueBuilder = new StringBuilder();
                    foreach (LastScoringSplit lastSplit in grouping)
                    {
                        ScoringSplit split = lastSplit.Split;
                        string name = lastSplit.PlayerDisplayName;

                        if (name == null)
                        {
                            // TODO: Find a way to not await in the loop
                            IGuildUser user = await this.Context.Guild.GetUserAsync(lastSplit.PlayerId);
                            name = user != null ? (user.Nickname ?? user.Username) : "<Unknown>";
                        }

                        valueBuilder.Append(
                            $"> {EscapeText(name)}:    {split.Points} ");
                        AddSplits(valueBuilder, split, highestPointsLevel);
                        int noPenalties = split.NoPenalties;
                        if (noPenalties > 0)
                        {
                            valueBuilder.Append($" ({noPenalties} no penalty buzz{(noPenalties != 1 ? "es" : "")})");
                        }
                        valueBuilder.AppendLine();
                    }

                    (string teamName, int points) = topTeamScores[index];
                    if (bonusStats.TryGetValue(grouping.Key, out BonusStats bonusStat))
                    {
                        AddBonusStatline(valueBuilder, bonusStat);
                    }
                    else if (bonusStats.Count > 0)
                    {
                        // We do have bonuses, so add the default bonus statline
                        AddBonusStatline(valueBuilder, BonusStats.Default);
                    }

                    string score = $"**{teamName}** ({points})";
                    return new EmbedFieldBuilder()
                    {
                        Name = score,
                        Value = valueBuilder.ToString()
                    };
                });
        }

        private enum HighestPointsLevel
        {
            Get = 0,
            Power = 1,
            Superpower = 2
        }
    }
}
