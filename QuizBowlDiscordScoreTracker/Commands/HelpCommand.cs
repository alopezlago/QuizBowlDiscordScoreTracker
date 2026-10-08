using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireContext(ContextType.Guild)]
    public class HelpCommand : InteractionModuleBase
    {
        private readonly InteractionService interactionService;

        public HelpCommand(InteractionService interactionService)
        {
            this.interactionService = interactionService;
        }

        [SlashCommand("help", "Lists available commands and how to use them.")]
        public async Task HelpAsync(
            [Summary("command-name", "Optional command name to look up")] string rawCommandName = null)
        {
            IEnumerable<SlashCommandInfo> commands = this.interactionService.SlashCommands;
            bool userIsBotOwner = this.Context.User.Id == (await this.Context.Client.GetApplicationInfoAsync()).Owner.Id;
            if (!userIsBotOwner)
            {
                commands = commands
                    .Where(command => !command.Module.Preconditions.Any(attribute => attribute is RequireOwnerAttribute));
            }

            if (!string.IsNullOrWhiteSpace(rawCommandName))
            {
                string commandName = rawCommandName.Trim().TrimStart('/');
                commands = commands.Where(command => command.Name.Contains(commandName, StringComparison.OrdinalIgnoreCase));
            }

            SlashCommandInfo[] matchingCommands = commands.OrderBy(command => command.Name).ToArray();
            if (matchingCommands.Length == 0)
            {
                await this.Context.Interaction.RespondOrFollowupAsync("No matching commands found.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(rawCommandName))
            {
                EmbedBuilder howToPlay = new EmbedBuilder()
                {
                    Title = "How to play",
                    Color = Color.Gold,
                    Description = "1. The reader starts a game with /read.\n" +
                        "2. Players buzz with /buzz and withdraw with /withdraw.\n" +
                        "3. The reader uses /score with -5, 0, 10, 15, or 20 for tossups, " +
                        "and splits such as 10/0/10 or 101 for bonuses.\n" +
                        "4. Use /next if no one answers correctly, or /clear to reset the current question.\n" +
                        "5. Use /undo to undo a scoring action and /get-score to see the score.\n" +
                        "6. Export results before using /end, which clears the game.\n\n" +
                        "Select a slash command in Discord to see its parameters. Plain-text buzzes and scores are not supported."
                };
                await this.Context.Interaction.RespondOrFollowupAsync(embed: howToPlay.Build());
            }

            await this.Context.Channel.SendAllEmbeds(
                matchingCommands,
                () => new EmbedBuilder()
                {
                    Title = "Commands",
                    Color = Color.Gold
                },
                (command, index) => new EmbedFieldBuilder()
                {
                    Name = $"/{command.Name}",
                    Value = command.Description + (command.Parameters.Count == 0 ? string.Empty :
                        "\n" + string.Join("\n", command.Parameters.Select(parameter =>
                            $"**{parameter.Name}**: {parameter.Description}")))
                });
        }
    }
}
