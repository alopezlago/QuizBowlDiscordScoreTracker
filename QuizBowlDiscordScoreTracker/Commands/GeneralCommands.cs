using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireContext(ContextType.Guild)]
    public class GeneralCommands : ModuleBase
    {
        public GeneralCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
        {
            this.DatabaseActionFactory = dbActionFactory;
            this.Manager = manager;
            this.Options = options;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        [Command("about")]
        [Summary("Gets the version of the bot and a link to the changes in this version.")]
        public Task AboutAsync()
        {
            return this.GetHandler().AboutAsync();
        }

        [Command("join")]
        [Summary("Join the team (not available if the team role prefix is set).")]
        public Task JoinAsync([Remainder] string teamName)
        {
            return this.GetHandler().JoinTeamAsync(teamName);
        }

        [Command("leave")]
        [Summary("Leave your team (not available if the team role prefix is set).")]
        public Task LeaveAsync()
        {
            return this.GetHandler().LeaveTeamAsync();
        }

        [Command("getTeams")]
        [Summary("Gets the teams players can join (not available if the team role prefix is set).")]
        public Task GetTeamsAsync()
        {
            return this.GetHandler().GetTeamsAsync();
        }

        [Command("gameReport")]
        [Summary("Gets a question-by-question report of the game.")]
        public Task GetGameReportAsync()
        {
            return this.GetHandler().GetGameReportAsync();
        }

        [Command("start")]
        [Alias("read")]
        [Summary("Set yourself as the reader.")]
        public Task SetReaderAsync()
        {
            return this.GetHandler().SetReaderAsync();
        }

        [Command("score")]
        [Summary("Get the top scores in the current game.")]
        public Task GetScoreAsync()
        {
            return this.GetHandler().GetScoreAsync();
        }

        private GeneralCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            return new GeneralCommandHandler(this.Context, this.Manager, this.Options, this.DatabaseActionFactory);
        }
    }
}
