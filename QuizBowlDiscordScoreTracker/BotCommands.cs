using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace QuizBowlDiscordScoreTracker
{
    public class BotCommands
    {
        // Will need:
        // !reader to set themselves as the reader
        // !stop or !end to reset and remove themselves as the reader
        // !score to get the score
        // !next or !clear to move to the next question

        // May just want this in the Bot class, since we want the queues.
        ////[Command("hi")]
        ////public async Task Hi(CommandContext ctx)
        ////{
        ////    await ctx.RespondAsync($"Hi there, {ctx.User.Mention}!");
        ////}

        [Command("reader")]
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
                await ctx.RespondAsync($"{ctx.User.Username} is the reader.");
            }
        }

        [Command("stop")]
        public async Task Stop(CommandContext ctx)
        {
            await ClearAll(ctx);
        }

        [Command("end")]
        public async Task End(CommandContext ctx)
        {
            await ClearAll(ctx);
        }

        [Command("score")]
        public async Task GetScore(CommandContext ctx)
        {
            GameState state = ctx.Dependencies.GetDependency<GameState>();
            await ctx.RespondAsync(state.GetScores());
        }

        [Command("clear")]
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
