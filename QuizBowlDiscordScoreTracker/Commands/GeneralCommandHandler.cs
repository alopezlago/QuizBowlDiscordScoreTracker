using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
        internal const int MaxTeamsShown = 10;
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
                $"https://github.com/alopezlago/QuizBowlDiscordScoreTracker/releases/tag/v{version}. For the privacy" +
                $"policy, visit https://www.quizbowlreader.com/privacy.html."
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

        public Task GetGameReportAsync()
        {
            return ScoreHandler.GetGameReportAsync(this.Context.Guild, this.Context.Channel, this.Manager);
        }

        public async Task SetReaderAsync()
        {
            IGuildUser user = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
            if (user == null)
            {
                // If the reader doesn't exist anymore, don't start a game.
                return;
            }

            // This needs to happen before we try creating a game
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
            else
            {
                return;
            }

            // Prevent a cold start on the first buzz, and eagerly get the team prefix and channel pair
            string teamRolePrefix;
            bool useBonuses;
            bool disableBuzzQueue;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.GetPairedVoiceChannelIdOrNullAsync(this.Context.Channel.Id);
                teamRolePrefix = await action.GetTeamRolePrefixAsync(this.Context.Guild.Id);

                bool[] getTasks = await Task.WhenAll(
                    action.GetUseBonusesAsync(this.Context.Guild.Id),
                    action.GetDisabledBuzzQueueAsync(this.Context.Guild.Id));
                useBonuses = getTasks[0];
                disableBuzzQueue = getTasks[1];
            }

            // Set teams here, if they are using roles.
            if (teamRolePrefix != null)
            {
                state.TeamManager = new ByRoleTeamManager(guildChannel, teamRolePrefix);
            }
            else
            {
                state.TeamManager = new ByCommandTeamManager();
            }

            // Set the format here. Eventually we'll want to use more information to determine the format
            state.Format = useBonuses ?
                Format.CreateTossupBonusesShootout(disableBuzzQueue) :
                Format.CreateTossupShootout(disableBuzzQueue);

            string baseMessage = this.Options.CurrentValue.WebBaseURL == null ?
                $"{this.Context.User.Mention} is the reader." :
                $"{this.Context.User.Mention} is the reader. Please visit {this.Options.CurrentValue.WebBaseURL}?{this.Context.Channel.Id} to hear buzzes.";
            string teamManagementMessage = teamRolePrefix == null ?
                "The reader can add teams through !addTeam *teamName*, and players can join teams with !join *teamName*. See !help for more team-based commands." :
                $@"Teams are set by the server role. Team roles begin with ""{teamRolePrefix}"".";
            await this.Context.Channel.SendMessageAsync($"{baseMessage}\n{teamManagementMessage}");
        }

        public Task GetScoreAsync()
        {
            return ScoreHandler.GetScoreAsync(this.Context.Guild, this.Context.Channel, this.Manager);
        }
    }
}
