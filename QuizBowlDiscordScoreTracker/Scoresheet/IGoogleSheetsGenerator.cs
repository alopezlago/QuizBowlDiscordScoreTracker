using System;
using System.Threading.Tasks;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public interface IGoogleSheetsGenerator
    {
        // Future work:
        // It would be great if we could do live scorekeeping, which requires going off of a timer
        // Google APIs restrict us to 100 API calls per 100 seconds, so we need to do the following
        // - Batch update
        // - Take a diff between what's been updated, and what needs writing
        //    - For this, might be best to have an event to call when we have scoring updates. This needs to include the
        //      reverse for updates, so maybe just include phases that need to be rewritten?
        // We can always do a pure "full export" first, then work on live updates
        // Could have two interfaces for it: GoogleSheetFullGenerator, GoogleSheetPartialGenerator
        // For the latter, we need a way to get the diffs

        /// <summary>
        /// Create the scoresheet for the game in the given Google Sheet worksheet
        /// </summary>
        /// <param name="game">Game to create the scoresheet from</param>
        /// <param name="sheetsUri">URI to the Google Sheet with the worksheet for the tournament's rosters</param>
        /// <param name="sheetName">Name of the worksheet for the round we're creating a scoresheet for</param>
        /// <returns>A result with an error code if the update failed, or a result with an empty string on success</returns>
        Task<IResult<string>> TryCreateScoresheet(GameState game, Uri sheetsUri, int roundNumber);

        /// <summary>
        /// Updates the rosters spreadsheet in the Google Sheet
        /// </summary>
        /// <param name="teamManager">Team Manager used in the server or game.</param>
        /// <param name="sheetsUri">URI to the Google Sheet with the worksheet for the tournament's rosters</param>
        /// <returns>A result with an error code if the update failed, or a result with an empty string on success</returns>
        Task<IResult<string>> TryUpdateRosters(IByRoleTeamManager teamManager, Uri sheetsUri);
    }
}
