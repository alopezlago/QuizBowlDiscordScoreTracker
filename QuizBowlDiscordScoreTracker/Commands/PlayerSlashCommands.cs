using System.Threading.Tasks;
using Discord.Interactions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Web;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireContext(ContextType.Guild)]
    public class PlayerSlashCommands : InteractionModuleBase
    {
        public PlayerSlashCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext)
        {
            this.Manager = manager;
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
            this.HubContext = hubContext;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IHubContext<MonitorHub> HubContext { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        // This should now store the interaction, so we can respond (epheremally) to them saying someone else got the
        // buzz.
        [RequirePlayer]
        [SlashCommand("buzz", "Buzzes in for a tossup")]
        public Task BuzzAsync()
        {
            return this.GetHandler().Buzz();
        }

        [SlashCommand("withdraw", "Withdraws a buzz")]
        public Task WithdrawAsync()
        {
            return this.GetHandler().Withdraw();
        }

        private PlayerCommandHandler GetHandler()
        {
            return new PlayerCommandHandler(
                this.Context, this.Manager, this.Options, this.DatabaseActionFactory, this.HubContext);
        }
    }
}
