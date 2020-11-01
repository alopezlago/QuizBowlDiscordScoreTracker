using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class ReaderCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(ReaderCommandHandler));

        public ReaderCommandHandler(
            ICommandContext context,
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IFileScoresheetGenerator scoresheetGenerator)
        {
            this.Context = context;
            this.Manager = manager;
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
            this.ScoresheetGenerator = scoresheetGenerator;
        }

        private ICommandContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IFileScoresheetGenerator ScoresheetGenerator { get; }

        public async Task AddTeamAsync(string teamName)
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                // This command only works during a game
                return;
            }

            if (!(game.TeamManager is ISelfManagedTeamManager teamManager))
            {
                // TODO: Should we look at the database and see if the team prefix is set?
                await this.Context.Channel.SendMessageAsync("Adding teams isn't supported in this mode.");
                return;
            }

            teamManager.TryAddTeam(teamName, out string message);
            await this.Context.Channel.SendMessageAsync(message);
        }

        public async Task RemoveTeamAsync(string teamName)
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                // This command only works during a game
                return;
            }

            if (!(game.TeamManager is ISelfManagedTeamManager teamManager))
            {
                // TODO: Should we look at the database and see if the team prefix is set?
                await this.Context.Channel.SendMessageAsync("Removing teams isn't supported in this mode.");
                return;
            }

            IReadOnlyDictionary<PlayerTeamPair, LastScoringSplit> lastSplits = await game.GetLastScoringSplits();
            IReadOnlyDictionary<string, string> teamIdToNames = await game.TeamManager.GetTeamIdToNames();
            string teamId = teamIdToNames.FirstOrDefault(kvp => kvp.Value.Trim() == teamName.Trim()).Key;
            string playerOnTeamWithScoreAction = lastSplits
                .Where(kvp => kvp.Key.TeamId == teamId)
                .Where(kvp => kvp.Value.Split.Negs != 0 ||
                    kvp.Value.Split.NoPenalties != 0 ||
                    kvp.Value.Split.Gets != 0 ||
                    kvp.Value.Split.Powers != 0 ||
                    kvp.Value.Split.Superpowers != 0)
                .Select(kvp => kvp.Key.PlayerDisplayName)
                .FirstOrDefault();
            if (playerOnTeamWithScoreAction != null)
            {
                await this.Context.Channel.SendMessageAsync(
                    $"Unable to remove the team. **{playerOnTeamWithScoreAction}** has already been scored, so the player cannot be removed without affecting the score.");
                return;
            }

            teamManager.TryRemoveTeam(teamName, out string message);
            await this.Context.Channel.SendMessageAsync(message);
        }

        public async Task RemovePlayerAsync(IGuildUser player)
        {
            Verify.IsNotNull(player, nameof(player));

            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                // This command only works during a game
                return;
            }

            if (!(game.TeamManager is ISelfManagedTeamManager teamManager))
            {
                // TODO: Should we look at the database and see if the team prefix is set?
                await this.Context.Channel.SendMessageAsync("Removing players isn't supported in this mode.");
                return;
            }

            string playerName = player.Nickname ?? player.Username;
            if (!teamManager.TryRemovePlayerFromTeam(player.Id))
            {
                await this.Context.Channel.SendMessageAsync(
                    $@"Couldn't remove player ""{playerName}"" from a team. Are they on a team?");
                return;
            }

            await this.Context.Channel.SendMessageAsync(
                $@"Player ""{playerName}"" removed from their team.");
        }

        public async Task DisableBonusesAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                // This command only works during a game
                return;
            }
            else if (game.Format.HighestPhaseIndexWithBonus < 0)
            {
                await this.Context.Channel.SendMessageAsync("Bonuses are already untracked.");
                return;
            }

            bool alwaysUseBonuses;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                alwaysUseBonuses = await action.GetUseBonuses(this.Context.Guild.Id);
            }

            if (alwaysUseBonuses)
            {
                await this.Context.Channel.SendMessageAsync(
                    "Bonuses are always tracked in this server. Run !disableBonusesAlways and restart the game to stop tracking bonuses.");
                return;
            }

            // TODO: We should look into cloning the format and changing the HighestPhaseIndexWithBonus field. This
            // would require another argument for the enable command, though, since it requires a number
            game.Format = Format.TossupShootout;
            await this.Context.Channel.SendMessageAsync(
                "Bonuses are no longer being tracked. Scores for the current question have been cleared.");
        }

        public async Task EnableBonusesAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                // This command only works during a game
                return;
            }
            else if (game.Format.HighestPhaseIndexWithBonus >= 0)
            {
                await this.Context.Channel.SendMessageAsync("Bonuses are already tracked.");
                return;
            }

            // TODO: We should look into cloning the format and changing the HighestPhaseIndexWithBonus field. This
            // would require an argument for how many bonuses to read
            game.Format = Format.TossupBonusesShootout;
            await this.Context.Channel.SendMessageAsync(
                "Bonuses are now being tracked. Scores for the current question have been cleared.");
        }

        public async Task ExportToFileAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                // This command only works during a game
                return;
            }

            if (!(this.Context.User is IGuildUser guildUser))
            {
                return;
            }

            if (!(this.Context.Channel is IGuildChannel guildChannel))
            {
                return;
            }

            Logger.Information($"User {this.Context.User.Id} attempting to export a scoresheet");

            IGuildUser guildBotUser = await this.Context.Guild.GetCurrentUserAsync();
            ChannelPermissions channelPermissions = guildBotUser.GetPermissions(guildChannel);
            if (!channelPermissions.AttachFiles)
            {
                Logger.Information(
                    $"User {this.Context.User.Id}'s export failed because channel {guildChannel.Id} didn't have the Attach Files permission");
                await this.Context.Channel.SendMessageAsync(
                    "This bot must have \"Attach Files\" permissions to export a scoresheet to a file");
                return;
            }

            int userExportCount;
            int guildExportCount;
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                int[] exportCounts = await Task.WhenAll(
                    action.GetGuildExportCount(this.Context.Guild.Id),
                    action.GetUserExportCount(this.Context.User.Id));
                userExportCount = exportCounts[0];
                guildExportCount = exportCounts[1];

                // The count starts at 0, so use >= to make sure we don't go over
                if (userExportCount >= this.Options.CurrentValue.DailyUserExportLimit)
                {
                    Logger.Information($"User {this.Context.User.Id}'s export failed because of a daily user limit");
                    await this.Context.Channel.SendMessageAsync(
                        "Cannot export a scoresheet. The user has already exceeded the number of scoresheets they can " +
                        $"export each day ({this.Options.CurrentValue.DailyUserExportLimit}). The limit resets at midnight GMT.");
                    return;
                }
                else if (guildExportCount >= this.Options.CurrentValue.DailyGuildExportLimit)
                {
                    Logger.Information(
                        $"User {this.Context.User.Id}'s export failed because of a daily server limit on server {this.Context.Guild.Id}");
                    await this.Context.Channel.SendMessageAsync(
                        "Cannot export a scoresheet. The server has already exceeded the number of scoresheets it can " +
                        $"export each day ({this.Options.CurrentValue.DailyGuildExportLimit}). The count resets at midnight GMT.");
                    return;
                }

                await Task.WhenAll(
                    action.IncrementGuildExportCount(this.Context.Guild.Id),
                    action.IncrementUserExportCount(this.Context.User.Id));
            }

            string readerName = guildUser.Nickname ?? guildUser.Username;
            IResult<Stream> spreadsheetResult = await this.ScoresheetGenerator.TryCreateScoresheet(
                game, readerName, this.Context.Channel.Name);
            if (!spreadsheetResult.Success)
            {
                Logger.Information($"User {this.Context.User.Id}'s export failed because of this error: {spreadsheetResult.ErrorMessage}");
                await this.Context.Channel.SendMessageAsync($"Export failed. Error: {spreadsheetResult.ErrorMessage}");
                return;
            }

            string readerNameInFilename = readerName.Length > 12 ? readerName.Substring(0, 12) : readerName;
            int newExportCount = userExportCount + 1;
            string filename = $"Scoresheet_{readerNameInFilename}_{newExportCount}.xlsx";
            await this.Context.Channel.SendFileAsync(
                spreadsheetResult.Value,
                filename,
                text: "Scoresheet for this game. This scoresheet is based on NAQT's electronic scoresheet (© National Academic Quiz Tournaments, LLC).");
            Logger.Information($"User {this.Context.User.Id}'s export succeeded");
        }

        public async Task SetNewReaderAsync(IGuildUser newReader)
        {
            if (newReader != null && this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                string readerRolePrefix;
                using (DatabaseAction action = this.DatabaseActionFactory.Create())
                {
                    readerRolePrefix = await action.GetReaderRolePrefixAsync(this.Context.Guild.Id);
                }

                if (!newReader.CanRead(this.Context.Guild, readerRolePrefix))
                {
                    await this.Context.Channel.SendMessageAsync(
                        @$"Cannot set {newReader.Mention} as the reader because they do not have a role with the reader prefix ""{readerRolePrefix}""");
                    return;
                }

                game.ReaderId = newReader.Id;
                await this.Context.Channel.SendMessageAsync($"{newReader.Mention} is now the reader.");
                return;
            }

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                    "New reader called in guild '{0}' in channel '{1}' with ID that could not be found: {2}",
                    guildChannel.Guild.Name,
                    guildChannel.Name,
                    newReader?.Id);
            }

            await this.Context.Channel.SendMessageAsync($"User could not be found. Could not set the new reader.");
        }

        public Task ClearAllAsync()
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) &&
                this.Manager.TryRemove(this.Context.Channel.Id)))
            {
                return Task.CompletedTask;
            }

            game.ClearAll();

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                    "Game ended in guild '{0}' in channel '{0}'", guildChannel.Guild.Name, guildChannel.Name);
            }

            return this.Context.Channel.SendMessageAsync($"Reading over. All stats cleared.");
        }

        public Task ClearAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                return Task.CompletedTask;
            }

            game.ClearCurrentRound();
            return this.Context.Channel.SendMessageAsync("Current cycle cleared of all buzzes.");
        }

        public async Task NextAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                return;
            }
            else if (game.PhaseNumber >= GameState.MaximumPhasesCount)
            {
                await this.Context.Channel.SendMessageAsync($"Reached the limit for games ({GameState.MaximumPhasesCount} questions)");
            }

            game.NextQuestion();

            // TODO: Consider having an event handler in GameState that will trigger when the phase is changed, so we
            // can avoid duplicating this code
            await this.Context.Channel.SendMessageAsync($"**TU {game.PhaseNumber}**");
        }

        public async Task UndoAsync()
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) && game.Undo(out ulong? nextUserId)))
            {
                return;
            }

            if (nextUserId == null && game.CurrentStage == PhaseStage.Bonus)
            {
                await this.Context.Channel.SendMessageAsync($"**Bonus for TU {game.PhaseNumber}**");
                return;
            }

            ulong userId = nextUserId.Value;
            IGuildUser user = await this.Context.Guild.GetUserAsync(userId);
            string name;
            string message;
            if (user == null)
            {
                // TODO: Need to test this case
                // Also unsure if this is really applicable. Could use status, but some people may play while
                // appearing offline.
                name = "<Unknown>";

                // Need to remove player from queue too, since they cannot answer
                // Maybe we need to find the next player in the queue?
                await game.WithdrawPlayer(userId);
                string nextPlayerMention = null;
                while (game.TryGetNextPlayer(out ulong nextPlayerId))
                {
                    IGuildUser nextPlayerUser = await this.Context.Guild.GetUserAsync(nextPlayerId);
                    if (nextPlayerUser != null)
                    {
                        nextPlayerMention = nextPlayerUser.Mention;
                        break;
                    }

                    // Player isn't here, so withdraw them
                    await game.WithdrawPlayer(nextPlayerId);
                }

                message = nextPlayerMention != null ?
                    $"Undid scoring for {name}. {nextPlayerMention}, your answer?" :
                    $"Undid scoring for {name}.";
            }
            else
            {
                name = user.Nickname ?? user.Username;
                message = $"Undid scoring for {name}. {user.Mention}, your answer?";
            }

            await this.Context.Channel.SendMessageAsync(message);
        }
    }
}
