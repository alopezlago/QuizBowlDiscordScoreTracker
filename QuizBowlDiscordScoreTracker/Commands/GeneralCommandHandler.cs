using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.TeamManager;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class GeneralCommandHandler
    {
        internal const int MaxTeamsShown = 10;
        private static readonly ILogger Logger = Log.ForContext<GeneralCommandHandler>();

        public GeneralCommandHandler(
            IInteractionContext context,
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext)
        {
            this.Context = context;
            this.DatabaseActionFactory = dbActionFactory;
            this.Manager = manager;
            this.Options = options;
            this.HubContext = hubContext;
        }

        private IInteractionContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IHubContext<MonitorHub> HubContext { get; }

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

            return this.Context.Interaction.RespondOrFollowupAsync(embed: embedBuilder.Build());
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
                await this.Context.Interaction.RespondOrFollowupAsync("Joining teams isn't supported in this mode.");
                return;
            }

            if (!teamManager.TryAddPlayerToTeam(
                this.Context.User.Id, guildUser.Nickname ?? guildUser.Username, teamName))
            {
                await this.Context.Interaction.RespondOrFollowupAsync(
                    $@"Couldn't join team ""{teamName}"". Make sure it is not misspelled.");
                return;
            }

            string teamId = await game.TeamManager.GetTeamIdOrNull(this.Context.User.Id);
            IReadOnlyDictionary<string,string> teamNames= await game.TeamManager.GetTeamIdToNames();
            teamName = teamNames[teamId];
            await this.Context.Interaction.RespondOrFollowupAsync($@"{guildUser.Mention} is on team ""{teamName}""");
        }

        public async Task LeaveTeamAsync()
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
                await this.Context.Interaction.RespondOrFollowupAsync("Leaving teams isn't supported in this mode.");
                return;
            }

            string name = guildUser.Nickname ?? guildUser.Username;
            if (!teamManager.TryRemovePlayerFromTeam(this.Context.User.Id))
            {
                await this.Context.Interaction.RespondOrFollowupAsync($@"""{name}"" isn't on a team.");
                return;
            }

            if (this.Context.Channel is ITextChannel textChannel)
            {
                await PromptHandler.WithdrawPlayerAsync(textChannel, this.Context.Client.CurrentUser.Id,
                    game, guildUser.Id, this.Options, this.DatabaseActionFactory, this.HubContext);
            }

            // We don't want to ping the user when they left, so use their nickname/username
            await this.Context.Interaction.RespondOrFollowupAsync($@"""{name}"" left their team.");
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
                await this.Context.Interaction.RespondOrFollowupAsync(game.TeamManager.JoinTeamDescription);
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

            await this.Context.Interaction.RespondOrFollowupAsync($"Teams: {teams}");
        }

        public Task GetGameReportAsync()
        {
            return ScoreHandler.GetGameReportAsync(this.Context, this.Manager);
        }

        public async Task SetReaderAsync()
        {
            IGuildUser user = this.Context.User as IGuildUser;
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
                await this.Context.Interaction.RespondOrFollowupAsync(
                    @$"{user.Mention} can't read because they don't have a role starting with the prefix ""{readerRolePrefix}"".");
                return;
            }

            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState state) ||
                this.Manager.TryCreate(this.Context.Channel.Id, out state)))
            {
                // Couldn't add a new reader.
                await this.Context.Interaction.RespondOrFollowupAsync("Couldn't add a new reader, as no game exists.");
                return;
            }
            else if (state.ReaderId != null)
            {
                // We already have a reader, so do nothing.
                await this.Context.Interaction.RespondOrFollowupAsync("Someone is already the reader.");
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
                Logger.Error(
                     "Tried to start game, but we're not in a guild channel. Channel ID: {0}", this.Context.Channel.Id);
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
                "The reader can add teams through /add-team *teamName*, and players can join teams with /join *teamName*. See /help for more team-based commands." :
                $@"Teams are set by the server role. Team roles begin with ""{teamRolePrefix}"".";
            await this.Context.Interaction.RespondOrFollowupAsync($"{baseMessage}{Environment.NewLine}{teamManagementMessage}");
        }

        public Task GetScoreAsync()
        {
            return ScoreHandler.GetScoreAsync(this.Context, this.Manager);
        }
    }
}
