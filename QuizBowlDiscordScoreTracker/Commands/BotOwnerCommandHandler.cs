using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class BotOwnerCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext<BotOwnerCommandHandler>();

        public BotOwnerCommandHandler(
            IInteractionContext context, IOptionsMonitor<BotConfiguration> options, IDatabaseActionFactory dbActionFactory)
        {
            this.Context = context;
            this.DatabaseActionFactory = dbActionFactory;
            this.Options = options;
        }

        private IInteractionContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        public async Task BanUserAsync(ulong userId)
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.AddCommandBannedUser(userId);
            }

            Logger.Information($"User {this.Context.User.Id} banned user {userId} from using commands");
            await this.Context.Interaction.RespondOrFollowupAsync($"Banned user with ID {userId} from running commands.");
        }

        public async Task UnbanUserAsync(ulong userId)
        {
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                await action.RemoveCommandBannedUser(userId);
            }

            Logger.Information($"User {this.Context.User.Id} unbanned user {userId} from using commands");
            await this.Context.Interaction.RespondOrFollowupAsync($"Unbanned user with ID {userId} from running commands.");
        }
    }
}
