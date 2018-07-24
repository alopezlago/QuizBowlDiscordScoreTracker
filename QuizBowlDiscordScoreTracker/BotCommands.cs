using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    public class BotCommands
    {
        [Command("read")]
        [Description("Set yourself as the reader.")]
        public async Task SetReader(CommandContext context)
        {
            GameState state = context.Dependencies.GetDependency<GameState>();
            if (state.Reader == null)
            {
                state.Reader = context.User;
                state.Channel = context.Channel;
                await context.RespondAsync($"{context.User.Mention} is the reader.");
            }
        }

        [Command("setnewreader")]
        [Description("Set another user as the reader.")]
        public async Task SetNewReader(CommandContext context, DiscordMember newReader)
        {
            GameState state = context.Dependencies.GetDependency<GameState>();
            if (CanPerformReaderActions(state, context))
            {
                if (newReader?.Presence?.User != null)
                {
                    state.Reader = newReader.Presence.User;
                    await context.RespondAsync($"{state.Reader.Mention} is now the reader.");
                }
                else
                {
                    await context.RespondAsync($"User could not be found. Could not set the new reader.");
                }
            }
        }

        [Command("stop")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public async Task Stop(CommandContext context)
        {
            await ClearAll(context);
        }

        [Command("end")]
        [Description("Ends the game, clearing the stats and allowing others to read.")]
        public async Task End(CommandContext context)
        {
            await ClearAll(context);
        }

        [Command("score")]
        [Description("Get the top scores in the current game.")]
        public async Task GetScore(CommandContext context)
        {
            GameState state = context.Dependencies.GetDependency<GameState>();
            if (state.Reader != null)
            {
                IEnumerable<KeyValuePair<DiscordUser, int>> scores = state.GetScores();

                DiscordEmbedBuilder builder = new DiscordEmbedBuilder();
                builder.Title = scores.Count() > GameState.ScoresListLimit ?
                    $"Top {GameState.ScoresListLimit} Scores" :
                    "Scores";
                builder.WithColor(DiscordColor.Gold);
                foreach (KeyValuePair<DiscordUser, int> score in scores)
                {
                    DiscordMember member = await context.Guild.GetMemberAsync(score.Key.Id);
                    string name = member.Nickname ?? member.DisplayName;
                    builder.AddField(member.DisplayName, score.Value.ToString());
                }

                DiscordEmbed embed = builder.Build();
                await context.Message.RespondAsync(embed: embed);
            }
        }

        [Command("clear")]
        [Description("Clears the player queue. Use this if no one answered correctly.")]
        public Task Clear(CommandContext context)
        {
            GameState state = context.Dependencies.GetDependency<GameState>();
            if (CanPerformReaderActions(state, context))
            {
                state.ClearCurrentRound();
            }

            return Task.CompletedTask;
        }

        [Command("next")]
        [Description("Moves to the next player in the queue. This is the same as scoring 0.")]
        public Task Next(CommandContext context)
        {
            GameState state = context.Dependencies.GetDependency<GameState>();
            if (CanPerformReaderActions(state, context))
            {
                state.ScorePlayer(0);
            }

            return Task.CompletedTask;
        }

        private static async Task ClearAll(CommandContext context)
        {
            GameState state = context.Dependencies.GetDependency<GameState>();
            if (CanPerformReaderActions(state, context))
            {
                state.ClearAll();
                await context.RespondAsync($"Reading over. All stats cleared.");
            }
        }

        private static bool CanPerformReaderActions(GameState state, CommandContext context)
        {
            if (state.Reader == context.User ||
                context.Channel.PermissionsFor(context.Member) == Permissions.Administrator)
            {
                return true;
            }

            // We can't rely on Email because the bot may not have acess to it
            ConfigOptions options = context.Dependencies.GetDependency<ConfigOptions>();
            return options.AdminIds != null &&
                options.AdminIds.Contains(context.User.Id.ToString(CultureInfo.InvariantCulture));
        }
    }
}
