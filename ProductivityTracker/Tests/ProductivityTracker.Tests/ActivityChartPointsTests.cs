using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class ActivityChartPointsTests
    {
        [TestMethod]
        public void MissingHoursBreakLinesWithoutMovingRecordedHours()
        {
            var runs = ActivityChartPoints.GetLineRuns(new[] { double.NaN, 10.0, 20, double.NaN, 30, 40 }).ToArray();
            Assert.AreEqual(2, runs.Length);
            CollectionAssert.AreEqual(new[] { 1, 2 }, runs[0].Select(point => point.Index).ToArray());
            CollectionAssert.AreEqual(new[] { 4, 5 }, runs[1].Select(point => point.Index).ToArray());
            Assert.AreEqual(30, runs[1][0].Value);
        }

        [TestMethod]
        public void RecordedZerosAndIsolatedHoursStillProduceMarkers()
        {
            var runs = ActivityChartPoints.GetLineRuns(new[] { 0.0, double.NaN, 0, 25 }).ToArray();
            Assert.AreEqual(2, runs.Length);
            Assert.AreEqual(1, runs[0].Length);
            Assert.AreEqual(0, runs[0][0].Value);
            Assert.AreEqual(2, runs[1][0].Index);
            Assert.AreEqual(25, runs[1][1].Value);
        }

        [TestMethod]
        public void EntirelyUnrecordedHoursDoNotDrawAnInactiveLine()
        {
            Assert.IsFalse(ActivityChartPoints.GetLineRuns(
                new[] { double.NaN, double.NaN, double.PositiveInfinity }).Any());
            Assert.IsFalse(ActivityChartPoints.GetLineRuns(Array.Empty<double>()).Any());
        }
    }
}
