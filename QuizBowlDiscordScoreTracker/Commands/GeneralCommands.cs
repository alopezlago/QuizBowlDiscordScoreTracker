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

        [Command("read")]
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
