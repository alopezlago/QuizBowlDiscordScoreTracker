using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Apis.Sheets.v4.Data;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public interface IGoogleSheetsApi : IDisposable
    {
        /// <summary>
        /// Clears the sheet, then updates it with new values. The cells in rangesToClear will be cleared before any
        /// updates occur.
        /// </summary>
        /// <param name="ranges">The cells to update with their new values</param>
        /// <param name="rangesToClear">The cells to clear</param>
        /// <param name="sheetsUri">The URI to the Google Sheets workbook/param>
        /// <returns></returns>
        Task<IResult<string>> UpdateGoogleSheet(List<ValueRange> ranges, List<string> rangesToClear, Uri sheetsUri);
    }
}
