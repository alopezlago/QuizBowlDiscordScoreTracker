namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    public interface IGoogleSheetsGeneratorFactory
    {
        IGoogleSheetsGenerator Create(GoogleSheetsType sheetsType);
    }
}
