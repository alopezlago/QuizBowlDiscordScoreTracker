using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public sealed class GoogleSheetsApi : IGoogleSheetsApi
    {
        private static readonly Serilog.ILogger Logger = Log.ForContext(typeof(GoogleSheetsApi));
        internal const int MaxRetries = 5;

        public GoogleSheetsApi(IOptionsMonitor<BotConfiguration> options)
        {
            this.IsDisposed = false;
            this.Options = options;

            this.InitializeService();

            this.OnConfigurationChangeHandler = this.Options.OnChange(this.OnConfigurationChange);
        }

        private bool IsDisposed { get; set; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IDisposable OnConfigurationChangeHandler { get; }

        private SheetsService Service { get; set; }

        public void Dispose()
        {
            if (this.IsDisposed)
            {
                this.Service?.Dispose();
                this.OnConfigurationChangeHandler.Dispose();
            }
        }

        public Task<IResult<string>> UpdateGoogleSheet(
            List<ValueRange> ranges, IList<string> rangesToClear, Uri sheetsUri)
        {
            Verify.IsNotNull(ranges, nameof(ranges));
            Verify.IsNotNull(rangesToClear, nameof(rangesToClear));
            Verify.IsNotNull(sheetsUri, nameof(sheetsUri));

            return this.UpdateGoogleSheet(ranges, rangesToClear, sheetsUri, attemptedRetries: 0);
        }

        private void InitializeService()
        {
            string email = this.Options.CurrentValue.GoogleAppEmail;
            if (email == null)
            {
                return;
            }

            string privateKey = this.Options.CurrentValue.GoogleAppPrivateKey;
            if (privateKey == null)
            {
                return;
            }

            ServiceAccountCredential credential = new ServiceAccountCredential(
               new ServiceAccountCredential.Initializer(email)
               {
                   Scopes = new[] { SheetsService.Scope.Spreadsheets }
               }.FromPrivateKey(privateKey));

            // If this takes time to initialize, we should make it Lazy
            this.Service = new SheetsService(new BaseClientService.Initializer()
            {
                ApplicationName = "Quiz Bowl Score Tracker",
                HttpClientInitializer = credential
            });
        }

        private void OnConfigurationChange(BotConfiguration newConfiguration, string value)
        {
            SheetsService oldService = this.Service;
            this.InitializeService();
            oldService?.Dispose();
        }

        private async Task<IResult<string>> UpdateGoogleSheet(
            List<ValueRange> ranges, IList<string> rangesToClear, Uri sheetsUri, int attemptedRetries)
        {
            if (this.Service == null)
            {
                return new FailureResult<string>(
                    "This instance of the bot doesn't support Google Sheets, because the Google account information for the bot isn't configured.");
            }

            IResult<string> sheetsIdResult = TryGetSheetsId(sheetsUri);
            if (!sheetsIdResult.Success)
            {
                return sheetsIdResult;
            }

            string sheetsId = sheetsIdResult.Value;

            try
            {
                BatchUpdateValuesRequest updateValuesData = new BatchUpdateValuesRequest()
                {
                    Data = ranges,
                    ValueInputOption = "RAW"
                };
                SpreadsheetsResource.ValuesResource.BatchUpdateRequest batchUpdateRequest = new SpreadsheetsResource.ValuesResource.BatchUpdateRequest(
                    this.Service, updateValuesData, sheetsId);

                if (rangesToClear.Count > 0)
                {
                    BatchClearValuesRequest clearValuesData = new BatchClearValuesRequest()
                    {
                        Ranges = rangesToClear
                    };
                    SpreadsheetsResource.ValuesResource.BatchClearRequest clearRequest = new SpreadsheetsResource.ValuesResource.BatchClearRequest(
                        this.Service, clearValuesData, sheetsId);
                    await clearRequest.ExecuteAsync();
                }

                BatchUpdateValuesResponse batchUpdateResponse = await batchUpdateRequest.ExecuteAsync();
                if (batchUpdateResponse.Responses.Any(response => response.UpdatedCells == 0))
                {
                    return new FailureResult<string>("Could only partially update the spreadsheet. Try again.");
                }

                return new SuccessResult<string>("Export successful");
            }
            catch (Google.GoogleApiException exception)
            {
                // See https://developers.google.com/drive/api/v3/handle-errors
                int errorCode = exception.Error?.Code ?? 0;
                if (errorCode == 403 &&
                    exception.Error.Errors != null &&
                    exception.Error.Errors.Any(error => error.Reason == "appNotAuthorizedToFile" || error.Reason == "forbidden" || error.Reason == "insufficientFilePermissions"))
                {
                    Logger.Error(exception, $"Error writing to the UCSD scoresheet: bot doesn't have permission");
                    return new FailureResult<string>(
                        $"The bot doesn't have write permissions to the Google Sheet. Please give `{this.Options.CurrentValue.GoogleAppEmail}` access to the Sheet by sharing it with them as an Editor.");
                }
                else if (attemptedRetries < MaxRetries && (errorCode == 403 || errorCode == 429))
                {
                    // Retry
                    attemptedRetries++;
                    Logger.Error(
                        exception,
                        $"Retry attempt {attemptedRetries} after getting a {errorCode} error for the UCSD scoresheet at the URL {sheetsUri.AbsoluteUri}");

                    // Use exponential back-off: wait for 2 seconds, then 5, then 9, etc.
                    await Task.Delay(1000 * (1 + (int)Math.Pow(2, attemptedRetries)));
                    return await this.UpdateGoogleSheet(ranges, rangesToClear, sheetsUri, attemptedRetries);
                }

                // Log
                Logger.Error(exception, $"Error writing to the UCSD scoresheet for URL {sheetsUri.AbsoluteUri}");
                return new FailureResult<string>($"Error writing to the Google Sheet: \"{exception.Message}\"");
            }
        }

        private static IResult<string> TryGetSheetsId(Uri sheetsUri)
        {
            // Format of a sheetsUrl: https://docs.google.com/spreadsheets/d/ID/edit#gid=87173672
            if (sheetsUri.Segments.Length < 4)
            {
                return new FailureResult<string>(
                    "The URL doesn't have the sheets ID in it. Be sure to copy the full URL from the address bar.");
            }
            else if (!sheetsUri.Segments[1].Equals("spreadsheets/", StringComparison.InvariantCultureIgnoreCase))
            {
                return new FailureResult<string>(
                    "The URL isn't for a spreadsheet. Be sure to copy the full URL from the address bar.");
            }

            string sheetsId = sheetsUri.Segments[3];
            if (sheetsId.EndsWith("/", StringComparison.InvariantCultureIgnoreCase))
            {
                sheetsId = sheetsId.Substring(0, sheetsId.Length - 1);
            }

            return new SuccessResult<string>(sheetsId);
        }
    }
}
