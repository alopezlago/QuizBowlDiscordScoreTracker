using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker.Scoresheet;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class SpreadsheetColumnTests
    {
        [TestMethod]
        public void ThrowIfLessThanOne()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new SpreadsheetColumn(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new SpreadsheetColumn(-1));
        }

        [TestMethod]
        public void ThrowIfGreaterThanLimit()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => new SpreadsheetColumn(SpreadsheetColumn.MaximumColumnNumber + 1));
        }

        [TestMethod]
        public void MaximumLimit()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(SpreadsheetColumn.MaximumColumnNumber);
            Assert.AreEqual("ZZ", column.ToString(), "Unexpected column");
        }

        [TestMethod]
        public void MinimumLimit()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(1);
            Assert.AreEqual("A", column.ToString(), "Unexpected column");
        }

        [TestMethod]
        public void Column27IsAA()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(27);
            Assert.AreEqual("AA", column.ToString(), "Unexpected column");
        }

        [TestMethod]
        public void AddTwo()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(26);
            Assert.AreEqual("Z", column.ToString(), "Unexpected column for #26");

            SpreadsheetColumn addition = column + 1;
            Assert.AreEqual("AA", addition.ToString(), "Unexpected added column");
        }

        [TestMethod]
        public void SubtractTwo()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(28);
            Assert.AreEqual("AB", column.ToString(), "Unexpected column for #28");

            SpreadsheetColumn subtraction = column - 2;
            Assert.AreEqual("Z", subtraction.ToString(), "Unexpected subtracted column");
        }

        [TestMethod]
        public void AddPastLimit()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(SpreadsheetColumn.MaximumColumnNumber);
            Assert.AreEqual("ZZ", column.ToString(), "Unexpected column for the limit");

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => column + 1);
        }

        [TestMethod]
        public void SubtractPastLimit()
        {
            SpreadsheetColumn column = new SpreadsheetColumn(1);
            Assert.AreEqual("A", column.ToString(), "Unexpected column for #1");

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => column - 1);
        }
    }
}
