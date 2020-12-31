using System;

namespace QuizBowlDiscordScoreTracker.Scoresheet
{
    // We could represent it generally (from A-ZZZZ...), but we only really need two digits, and it simplifies the code
    /// <summary>
    /// This can represent cells from A-ZZ
    /// </summary>
    public class SpreadsheetColumn
    {
        public const char StartingColumn = 'A';
        public const int MaximumColumnNumber = 26 * 26 + 26;

        /// <summary>
        /// Represents a column from A to ZZ. The column number starts from 1 and goes up to MaximumColumnNumber.
        /// </summary>
        /// <param name="columnNumber">The number of the column. 1 is the lowest column number.</param>
        public SpreadsheetColumn(int columnNumber)
        {
            if (columnNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(columnNumber), $"{nameof(columnNumber)} must be >= 1");
            }
            else if (columnNumber > MaximumColumnNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnNumber), $"{nameof(columnNumber)} must be <= {MaximumColumnNumber}");
            }

            this.ColumnIndex = columnNumber - 1;
        }

        public SpreadsheetColumn(char column) : this((int)(column - 'A') + 1)
        {
        }

        private int ColumnIndex { get; set; }

        public static SpreadsheetColumn operator +(SpreadsheetColumn column, int value)
        {
            Verify.IsNotNull(column, nameof(column));

            // SpreadsheetColumn expects a number, so account for it in the result by adding 1 to each index
            int result = checked(column.ColumnIndex + 1 + value);
            return new SpreadsheetColumn(result);
        }

        public static SpreadsheetColumn operator -(SpreadsheetColumn column, int value)
        {
            Verify.IsNotNull(column, nameof(column));

            // The correction from index to number should cancel out
            int result = checked(column.ColumnIndex + 1 - value);
            return new SpreadsheetColumn(result);
        }

        public static SpreadsheetColumn Add(SpreadsheetColumn left, int right)
        {
            return left + right;
        }

        public static SpreadsheetColumn Subtract(SpreadsheetColumn left, int right)
        {
            return left - right;
        }

        public override string ToString()
        {
            // Handle the case where column < 26, and where column < 26 * 26
            // Probably easiest through an if statement
            // < 26 => (char)(65 + column) or 'A' + column
            // >= 26 -> ('A' + (column / 26))('A' + (column % 26)))
            if (this.ColumnIndex < 26)
            {
                char column = (char)(StartingColumn + this.ColumnIndex);
                return column.ToString();
            }

            // We know ColumnIndex >= 26, so this.ColumnIndex / 26 >= 1. We want to start off in the starting column,
            // so subtract 1 from the division so we start at A.
            int leadingDigitOffset = this.ColumnIndex / 26 - 1;
            return $"{(char)(StartingColumn + leadingDigitOffset)}{(char)(StartingColumn + (this.ColumnIndex % 26))}";
        }

        public override bool Equals(object obj)
        {
            if (!(obj is SpreadsheetColumn column))
            {
                return false;
            }

            return this.ColumnIndex == column.ColumnIndex;
        }

        public override int GetHashCode()
        {
            return this.ColumnIndex;
        }
    }
}
