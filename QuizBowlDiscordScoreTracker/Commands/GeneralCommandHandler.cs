using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class GeneralCommandHandler
    {
        internal const int MaxLeadersShown = 5;
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

        public async Task GetGameReportAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState state))
            {
                return;
            }

            IEnumerable<IEnumerable<ScoringSplitOnScoreAction>> scoresByPhases = state.GetScoringActionsByPhase();

            // If there's been no buzzes in the last question, don't show it in the report (could be end of the packet)
            IEnumerable<ScoringSplitOnScoreAction> lastQuestion = scoresByPhases.LastOrDefault();
            if (lastQuestion?.Any() != true)
            {
                scoresByPhases = scoresByPhases.SkipLast(1);
            }

            IEnumerable<ScoringSplit> splits = scoresByPhases
                .SelectMany(pairs => pairs.Select(pair => pair.Split));
            HighestPointsLevel highestPointsLevel = FindHighestPointLevel(splits);

            int scoresByQuestionCount = scoresByPhases.Count();
            int questionsReported = await this.Context.Channel.SendAllEmbeds(
                scoresByPhases,
                () => new EmbedBuilder()
                {
                    Title = GameReportTitle,
                    Color = Color.Gold
                },
                (pairs, index) =>
                    GetEmbedFieldForPhase(state, pairs, highestPointsLevel, index, index == scoresByQuestionCount - 1));

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

            state.ReaderId = this.Context.User.Id;

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                     "Game started in guild '{0}' in channel '{1}'", guildChannel.Guild.Name, guildChannel.Name);
            }

            // Prevent a cold start on the first buzz, and eagerly get the team prefix and channel pair
            Task[] dbTasks = new Task[2];
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                dbTasks[0] = action.GetPairedVoiceChannelIdOrNullAsync(this.Context.Channel.Id);
                dbTasks[1] = action.GetTeamRolePrefixAsync(this.Context.Guild.Id);
            }

            await Task.WhenAll(dbTasks);

            string message = this.Options.CurrentValue.WebBaseURL == null ?
                $"{this.Context.User.Mention} is the reader." :
                $"{this.Context.User.Mention} is the reader. Please visit {this.Options.CurrentValue.WebBaseURL}?{this.Context.Channel.Id} to hear buzzes.";
            await this.Context.Channel.SendMessageAsync(message);
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

            IOrderedEnumerable<KeyValuePair<ulong, ScoringSplitOnScoreAction>> orderedScoringSplits = currentGame.GetLastScoringSplits()
                .OrderByDescending(kvp => kvp.Value.Split.Points);
            IEnumerable<KeyValuePair<ulong, ScoringSplitOnScoreAction>> topOrderedScoringSplits = orderedScoringSplits
                .Take(GameState.ScoresListLimit);
            int[] topThreeScores = GetTopThreeScores(orderedScoringSplits);

            HighestPointsLevel highestPointsLevel = FindHighestPointLevel(
                orderedScoringSplits.Select(kvp => kvp.Value.Split));

            // We could have more than the embed limit, so split them up if necessary
            int embedsSentCount = await this.Context.Channel.SendAllEmbeds(
                topOrderedScoringSplits,
                () =>
                {
                    return new EmbedBuilder
                    {
                        Title = orderedScoringSplits.Take(checked(GameState.ScoresListLimit + 1)).Count() > GameState.ScoresListLimit ?
                            $"Top {GameState.ScoresListLimit} Scores" :
                            "Scores",
                        Color = Color.Gold
                    };
                },
                (kvp, index) =>
                {
                    StringBuilder valueBuilder = new StringBuilder();
                    ScoringSplit split = kvp.Value.Split;
                    string name = EscapePlayerName(kvp.Value.Action.Buzz.PlayerDisplayName);

                    // If the player has one of the top three scores (or is tied with one of them), show the medal for
                    // it.
                    int topScoreIndex = Array.IndexOf(topThreeScores, split.Points);
                    if (topScoreIndex >= 0)
                    {
                        name = $"{Medals[topScoreIndex]} {name}";
                    }

                    valueBuilder.Append($"**{split.Points}** ");
                    AddSplits(valueBuilder, split, highestPointsLevel);

                    int noPenalties = split.NoPenalties;
                    if (noPenalties > 0)
                    {
                        valueBuilder.Append($" ({noPenalties} no penalty buzz{(noPenalties != 1 ? "es" : "")})");
                    }

                    return new EmbedFieldBuilder()
                    {
                        Name = name,
                        Value = valueBuilder.ToString()
                    };
                });

            if (embedsSentCount == 0)
            {
                await this.Context.Channel.SendMessageAsync("No one has scored yet");
            }
        }

        private static void AppendLeadersMessage(GameState state, StringBuilder valueBuilder)
        {
            IEnumerable<ScoringSplitOnScoreAction> lastSplits = state.GetLastScoringSplits().Values;
            IGrouping<int, ScoringSplitOnScoreAction> topLastSplits = state.GetLastScoringSplits().Values
                .GroupBy(pair => pair.Split.Points)
                .OrderByDescending(grouping => grouping.Key)
                .FirstOrDefault();
            if (topLastSplits == null)
            {
                return;
            }

            IEnumerable<string> boldedNames = topLastSplits
                .Take(MaxLeadersShown)
                .Select(split => $"**{EscapePlayerName(split.Action.Buzz.PlayerDisplayName)}**");
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
        
        private static string EscapePlayerName(string name)
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

        private static EmbedFieldBuilder GetEmbedFieldForPhase(
            GameState state,
            IEnumerable<ScoringSplitOnScoreAction> pairs,
            HighestPointsLevel highestPointsLevel,
            int index,
            bool isLastPhase)
        {
            StringBuilder valueBuilder = new StringBuilder();
            bool answeredCorrectly = false;
            foreach (ScoringSplitOnScoreAction pair in pairs)
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

                valueBuilder.Append($"**{EscapePlayerName(pair.Action.Buzz.PlayerDisplayName)}** ");
                AddSplits(valueBuilder, pair.Split, highestPointsLevel);
                valueBuilder.AppendLine(".");
            }

            if (!answeredCorrectly)
            {
                valueBuilder.AppendLine("> Question went dead.");
            }

            if (isLastPhase)
            {
                AppendLeadersMessage(state, valueBuilder);
            }

            return new EmbedFieldBuilder()
            {
                Name = $"**Question {index + 1}**",
                Value = valueBuilder.ToString()
            };
        }

        private static int[] GetTopThreeScores(
            IOrderedEnumerable<KeyValuePair<ulong, ScoringSplitOnScoreAction>> orderedScoringSplits)
        {
            // We may not have 3 scorers in our splits, so fill in the values with the previous scores or 0
            int[] topThreeScores = new int[3];
            int[] topExistingScores = orderedScoringSplits
                .Select(kvp => kvp.Value.Split.Points)
                .Take(topThreeScores.Length)
                .ToArray();
            int i = 0;
            while (i < topExistingScores.Length)
            {
                topThreeScores[i] = topExistingScores[i];
                i++;
            }

            int lastTopScore = topExistingScores.Length > 0 ? topExistingScores[topExistingScores.Length - 1] : 0;
            while (i < topThreeScores.Length)
            {
                topThreeScores[i] = lastTopScore;
                i++;
            }

            return topThreeScores;
        }

        private enum HighestPointsLevel
        {
            Get = 0,
            Power = 1,
            Superpower = 2
        }
    }
}
