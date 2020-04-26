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

        // Help doesn't use the command handler, because 
        // 1. CommandService and CommandInfo are not mockable, so you can't unit test them
        // 2. This should be a quick event to handle.

        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task Help()
        {
            return this.SendHelpInformation();
        }

        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task Help([Remainder] [Summary("Command name")] string rawCommandName)
        {
            return this.SendHelpInformation(rawCommandName);
        }

        private Task SendHelpInformation(string rawCommandName = null)
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();
            IEnumerable<CommandInfo> commands = this.commandService.Commands
                .Where(command => command.Name != "help");

            if (rawCommandName != null)
            {
                string commandName = rawCommandName.Trim();
                commands = commands
                    .Where(command => command.Name.Equals(commandName, StringComparison.CurrentCultureIgnoreCase));
            }

            foreach (CommandInfo commandInfo in commands)
            {
                string parameters = string.Join(' ', commandInfo.Parameters.Select(parameter => $"*{parameter.Name}*"));
                string name = $"{commandInfo.Name} {parameters}";
                embedBuilder.AddField(name, commandInfo.Summary ?? "<undocumented>");
            }

            // DM the user, so that we can't spam the channel.
            return this.Context.User.SendMessageAsync(embed: embedBuilder.Build());
        }
    }
}
