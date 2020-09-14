using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class HelpCommand : ModuleBase
    {
        private readonly CommandService commandService;

        public HelpCommand(CommandService commandService)
        {
            this.commandService = commandService;
        }

        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task HelpAsync()
        {
            return this.SendHelpInformationAsync();
        }

        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task HelpAsync([Remainder][Summary("Command name")] string rawCommandName)
        {
            return this.SendHelpInformationAsync(rawCommandName);
        }

        private async Task SendHelpInformationAsync(string rawCommandName = null)
        {
            IEnumerable<CommandInfo> commands = this.commandService.Commands.Where(command => command.Name != "help");
            if (rawCommandName != null)
            {
                string commandName = rawCommandName.Trim();
                commands = commands
                    .Where(command => command.Name.Equals(commandName, StringComparison.CurrentCultureIgnoreCase));
            }

            bool userIsBotOwner = this.Context.User.Id == (await this.Context.Client.GetApplicationInfoAsync()).Owner.Id;
            if (!userIsBotOwner)
            {
                // Only let owners see owner-only commands
                commands = commands
                    .Where(command => !command.Module.Preconditions.Any(attribute => attribute is RequireOwnerAttribute));
            }

            commands = commands.OrderBy(command => command.Name);

            // Only send the "how to play" information if the user isn't looking up information for a command
            if (rawCommandName == null)
            {
                // Send two messages: a "how to play", and then the commands
                EmbedBuilder howToPlayEmbedBuilder = new EmbedBuilder()
                {
                    Title = "How to play",
                    Color = Color.Gold,
                    Description = "1. The reader should use the !read command.\n" +
                        "2. When a player wants to buzz in, they should type in \"buzz\" (near equivalents like \"bzz\" are acceptable).\n" +
                        "3. The reader scores the buzz by typing in the value (-5, 0, 10, 15, 20).\n" +
                        "4. If someone gets the question correct, the buzz queue is cleared. If no one answers the question, then use the !next command to clear the queue and start the next question.\n" +
                        "5. If the reader needs to undo a scoring action, they should use the !undo command.\n" +
                        "6. To see the score, use the !score command.\n" +
                        "7. Once the packet reading is over, the reader should use !end to end the game."
                };
                await this.Context.Channel.SendMessageAsync(embed: howToPlayEmbedBuilder.Build());
            }

            await this.Context.Channel.SendAllEmbeds(
                commands,
                () => new EmbedBuilder()
                {
                    Title = "Commands",
                    Color = Color.Gold
                },
                (commandInfo, index) =>
                {
                    string parameters = string.Join(' ', commandInfo.Parameters
                        .Select(parameter => GetParameterFieldName(parameter)));
                    return new EmbedFieldBuilder()
                    {
                        Name = $"{commandInfo.Name} {parameters}",
                        Value = commandInfo.Summary ?? "<undocumented>"
                    };
                });
        }

        private static string GetParameterFieldName(ParameterInfo parameter)
        {
            bool isChannel = parameter.Type.GetInterface(nameof(IGuildChannel)) != null;
            bool isMention = !isChannel && parameter.Type.GetInterface(nameof(IUser)) != null;
            return $"*{(isChannel ? "#" : string.Empty)}{(isMention ? "@" : string.Empty)}{parameter.Name}*";
        }
    }
}
