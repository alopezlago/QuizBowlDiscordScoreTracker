using System;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public class GoogleSheetsGeneratorFactory : IGoogleSheetsGeneratorFactory
    {
        public GoogleSheetsGeneratorFactory(IGoogleSheetsApi sheetsApi)
        {
            this.SheetsApi = sheetsApi;
        }

        private IGoogleSheetsApi SheetsApi { get; }

        public IGoogleSheetsGenerator Create(GoogleSheetsType sheetsType)
        {
            return sheetsType switch
            {
                GoogleSheetsType.UCSD => new UCSDGoogleSheetsGenerator(this.SheetsApi),
                _ => throw new ArgumentException(
$"Cannot create a generator for type {Enum.GetName(typeof(GoogleSheetsType), sheetsType)}"),
            };
        }
    }
}
