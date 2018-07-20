using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace QuizBowlDiscordScoreTracker
{
    public class BotCommands
    {
        [Command("read")]
        [Description("Set yourself as the reader.")]
        public async Task SetReader(CommandContext ctx)
        {
            GameState state = ctx.Dependencies.GetDependency<GameState>();

            // TODO: Determine if we want to allow admins/those who can kick to set the reader, to take it away from
            // someone who shouldn't read.
            // The code to calculate this is:
            // ctx.Channel.PermissionsFor(ctx.Member) == Permissions.Administrator | Permissions.KickMembers | Permissions.BanMembers;

            if (state.Reader == null)
            {
                state.Reader = ctx.User;
                await ctx.RespondAsync($"{ctx.User.Mention} is the reader.");
            }
        }

        [Command("stop")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public async Task Stop(CommandContext ctx)
        {
            await ClearAll(ctx);
        }

        [Command("end")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public async Task End(CommandContext ctx)
        {
            await ClearAll(ctx);
        }

        [Command("score")]
        [Description("Get the top scores in the current game.")]
        public async Task GetScore(CommandContext ctx)
        {
            GameState state = ctx.Dependencies.GetDependency<GameState>();
            // Only show scores when there's a game going on, i.e. there's a reader
            if (state.Reader != null)
            {
                await ctx.RespondAsync(state.GetScores());
            }
        }

        [Command("clear")]
        [Description("Clears the player queue. Use this if no one answered correctly.")]
        public Task Clear(CommandContext ctx)
        {
            GameState state = ctx.Dependencies.GetDependency<GameState>();
            if (CanPerformReaderActions(state, ctx))
            {
                state.ClearCurrentRound();
            }

            return Task.CompletedTask;
        }

        [Command("next")]
        [Description("Moves to the next player in the queue. This is the same as scoring 0.")]
        public Task Next(CommandContext ctx)
        {
            GameState state = ctx.Dependencies.GetDependency<GameState>();
            if (CanPerformReaderActions(state, ctx))
            {
                state.ScorePlayer(0);
            }

            return Task.CompletedTask;
        }

        private static async Task ClearAll(CommandContext ctx)
        {
            GameState state = ctx.Dependencies.GetDependency<GameState>();

            // TODO: Determine if we want to allow admins/those who can kick to set the reader, to take it away from
            // someone who shouldn't read.
            // The code to calculate this is:
            if (CanPerformReaderActions(state, ctx))
            {
                state.ClearAll();
                await ctx.RespondAsync($"Reading over. All stats cleared.");
            }
        }

        private static bool CanPerformReaderActions(GameState state, CommandContext ctx)
        {
            return state.Reader == ctx.User ||
                ctx.Channel.PermissionsFor(ctx.Member) == Permissions.Administrator;
        }
    }
}
