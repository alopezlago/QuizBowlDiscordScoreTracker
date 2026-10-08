using System.Globalization;
using System.Threading.Tasks;
using Discord.Interactions;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public class BotOwnerSlashCommands : InteractionModuleBase
    {
        public BotOwnerSlashCommands(IOptionsMonitor<BotConfiguration> options, IDatabaseActionFactory dbActionFactory)
        {
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
        }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        [SlashCommand("ban-user", "Bans a user from using commands.")]
        public Task BanUserAsync([Summary("user-id", "The Discord ID of the user to ban")] string userId)
        {
            return this.SetCommandBanAsync(userId, true);
        }

        [SlashCommand("unban-user", "Unbans a user from using commands.")]
        public Task UnbanUserAsync([Summary("user-id", "The Discord ID of the user to unban")] string userId)
        {
            return this.SetCommandBanAsync(userId, false);
        }

        private Task SetCommandBanAsync(string userId, bool banned)
        {
            if (!ulong.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsedUserId) ||
                parsedUserId == 0)
            {
                return this.Context.Interaction.RespondOrFollowupAsync(
                    "Invalid user ID. Enter a positive Discord user ID containing only digits.", ephemeral: true);
            }

            BotOwnerCommandHandler handler = this.GetHandler();
            return banned ? handler.BanUserAsync(parsedUserId) : handler.UnbanUserAsync(parsedUserId);
        }

        private BotOwnerCommandHandler GetHandler()
        {
            return new BotOwnerCommandHandler(this.Context, this.Options, this.DatabaseActionFactory);
        }
    }
}
