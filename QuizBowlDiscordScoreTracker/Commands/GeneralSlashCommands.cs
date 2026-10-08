using System.Threading.Tasks;
using Discord.Interactions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Web;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireContext(ContextType.Guild)]
    public class GeneralSlashCommands : InteractionModuleBase
    {
        public GeneralSlashCommands(
           GameStateManager manager,
           IOptionsMonitor<BotConfiguration> options,
           IDatabaseActionFactory dbActionFactory,
           IHubContext<MonitorHub> hubContext)
        {
            this.DatabaseActionFactory = dbActionFactory;
            this.Manager = manager;
            this.Options = options;
            this.HubContext = hubContext;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IHubContext<MonitorHub> HubContext { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        [SlashCommand("about", "Gets the version of the bot and a link to the changes in this version.")]
        public async Task AboutAsync()
        {
            await this.GetHandler().AboutAsync();
        }

        [SlashCommand("join", "Join the team (not available if the team role prefix is set).")]
        public Task JoinAsync(string teamName)
        {
            return this.GetHandler().JoinTeamAsync(teamName);
        }

        [SlashCommand("leave", "Leave your team (not available if the team role prefix is set).")]
        public Task LeaveAsync()
        {
            return this.GetHandler().LeaveTeamAsync();
        }

        [SlashCommand("get-teams", "Gets the teams players can join (not available if the team role prefix is set).")]
        public Task GetTeamsAsync()
        {
            return this.GetHandler().GetTeamsAsync();
        }

        [SlashCommand("game-report", "Gets a question-by-question report of the game.")]
        public Task GetGameReportAsync()
        {
            return this.GetHandler().GetGameReportAsync();
        }

        [SlashCommand("start", "Set yourself as the reader.")]
        public Task SetReaderStartAsync()
        {
            return this.GetHandler().SetReaderAsync();
        }

        [SlashCommand("read", "Set yourself as the reader.")]
        public Task SetReaderReadAsync()
        {
            return this.GetHandler().SetReaderAsync();
        }

        [SlashCommand("get-score", "Get the top scores in the current game.")]
        public Task GetScoreAsync()
        {
            return this.GetHandler().GetScoreAsync();
        }

        private GeneralCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            // Need a separate handler, since the interface is different?
            return new GeneralCommandHandler(this.Context, this.Manager, this.Options, this.DatabaseActionFactory,
                this.HubContext);
        }
    }
}
