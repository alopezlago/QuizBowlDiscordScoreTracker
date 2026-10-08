using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.Web;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireReader]
    [RequireContext(ContextType.Guild)]
    public class ReaderSlashCommands : InteractionModuleBase
    {
        public ReaderSlashCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext,
            IFileScoresheetGenerator scoresheetGenerator,
            IGoogleSheetsGeneratorFactory googleSheetsGeneratorFactory)
        {
            this.Manager = manager;
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
            this.HubContext = hubContext;
            this.ScoresheetGenerator = scoresheetGenerator;
            this.GoogleSheetsGeneratorFactory = googleSheetsGeneratorFactory;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IGoogleSheetsGeneratorFactory GoogleSheetsGeneratorFactory { get; }

        private IHubContext<MonitorHub> HubContext { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IFileScoresheetGenerator ScoresheetGenerator { get; }

        // TODO: Look into adding a /score command, which takes -5/0/10/15/20 or the bonus stuff
        // Maybe some aliases, like /0, /-5, /10, /15, /20
        // /score is already used by something, so need another command, or replace score with get-score

        [SlashCommand("add-team", "Adds a team to the game (not available if the team role prefix is set).")]
        public Task AddTeamAsync([Summary("team-name", "Name of the team you are adding")] string teamName)
        {
            return this.GetHandler().AddTeamAsync(teamName);
        }

        [SlashCommand("remove-team", "Removes a team from the game (not available if the team role prefix is set).")]
        public Task RemoveTeamAsync([Summary("team-name", "Name of the team you are removing")] string teamName)
        {
            return this.GetHandler().RemoveTeamAsync(teamName);
        }

        [SlashCommand("reload-team-roles", "Reload the teams of the game (not available if the team role prefix isn't set).")]
        public Task ReloadTeamsAsync()
        {
            return this.GetHandler().ReloadTeamRoles();
        }

        [SlashCommand("remove-player", "Removes a player from the given team (not available if the team role prefix is set).")]
        public Task RemovePlayerAsync([Summary(description: "Mention of the user to remove")] IGuildUser player)
        {
            return this.GetHandler().RemovePlayerAsync(player);
        }

        [SlashCommand("disable-bonuses",
            "Makes the current game track only tossups from now on. This command will reset the current cycle.")]
        public Task DisableBonusesAsync()
        {
            return this.GetHandler().DisableBonusesAsync();
        }

        [SlashCommand("enable-bonuses",
            "Makes the current game track bonuses from now on. This command will reset the current cycle.")]
        public Task EnableBonusesAsync()
        {
            return this.GetHandler().EnableBonusesAsync();
        }

        [SlashCommand("set-new-reader", "Set another user as the reader.")]
        public Task SetNewReaderAsync([Summary("new-reader", "The new reader to switch to")] IGuildUser newReader)
        {
            return this.GetHandler().SetNewReaderAsync(newReader);
        }

        [SlashCommand("end", "Ends the game, clearing the stats and allowing others to read.")]
        public Task EndAsync()
        {
            return this.GetHandler().ClearAllAsync();
        }

        [SlashCommand("stop", "Ends the game, clearing the stats and allowing others to read.")]
        public Task StopAsync()
        {
            return this.GetHandler().ClearAllAsync();
        }

        [HumanOnly]
        [SlashCommand("export-to-file", "Exports the scoresheet to a spreadsheet file (based on NAQT's electronic scoresheet)")]
        public Task ExportToFileAsync()
        {
            return this.GetHandler().ExportToFileAsync();
        }

        [HumanOnly]
        [SlashCommand("export-to-tj", "Exports the scoresheet to the TJ Scoresheet.")]
        public Task ExportToTJAsync(
            [Summary("sheets-url", "The URL to the TJ Google Sheet.")] string sheetsUrl,
            [Summary(description: "The round number, starting from 1")] int round)
        {
            return this.GetHandler().ExportToTJ(sheetsUrl, round);
        }

        [HumanOnly]
        [SlashCommand("export-to-ucsd",
            "Exports the scoresheet to the UCSD Scoresheet.")]
        public Task ExportToUCSDAsync(
            [Summary("sheets-url", "The URL to the UCSD Google Sheet.")] string sheetsUrl,
            [Summary(description: "The round number, starting from 1")] int round)
        {
            return this.GetHandler().ExportToUCSD(sheetsUrl, round);
        }

        [SlashCommand("clear",
            "Clears the player queue and answers from this question, including scores from this question.")]
        public Task ClearAsync()
        {
            return this.GetHandler().ClearAsync();
        }

        [SlashCommand("next",
            "Clears the player queue and moves to the next question. Use this if no one answered correctly.")]
        public Task NextAsync()
        {
            return this.GetHandler().NextAsync();
        }

        [SlashCommand("undo", "Undoes a scoring operation.")]
        public Task UndoAsync()
        {
            return this.GetHandler().UndoAsync();
        }

        [SlashCommand("-5", "Shortcut for giving -5 points for a tossup")]
        public Task ScoreNeg5Async()
        {
            return this.GetHandler().Score("-5");
        }

        [SlashCommand("0", "Shortcut for giving 0 points for a tossup")]
        public Task Score0Async()
        {
            return this.GetHandler().Score("0");
        }

        [SlashCommand("10", "Shortcut for giving 10 points for a tossup")]
        public Task Score10Async()
        {
            return this.GetHandler().Score("10");
        }

        [SlashCommand("15", "Shortcut for giving 15 points for a tossup")]
        public Task Score15Async()
        {
            return this.GetHandler().Score("15");
        }

        [SlashCommand("20", "Shortcut for giving 20 points for a tossup")]
        public Task Score20Async()
        {
            return this.GetHandler().Score("20");
        }

        // If this is fixed, we may just want to add choices directly. -5/0/10/15/20 can be choices, and maybe some for
        // the bonuses, though that's more challenging (need to know which one we are in, unless we have separate
        // commands for bonuses and tossups)
        [SlashCommand("score", "Score a player for a tossup or a team for a bonus")]
        public Task ScoreAsync(
            [Summary(description: "The number of points in a tossup, or the splits for a bonus")] string points)
        {
            return this.GetHandler().Score(points);
        }

        private ReaderCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            return new ReaderCommandHandler(
                this.Context,
                this.Manager,
                this.Options,
                this.DatabaseActionFactory,
                this.HubContext,
                this.ScoresheetGenerator,
                this.GoogleSheetsGeneratorFactory);
        }
    }
}
