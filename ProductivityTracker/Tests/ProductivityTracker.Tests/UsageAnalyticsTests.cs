using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class UsageAnalyticsTests
    {
        [TestMethod]
        public void HourlyTotalsClipMidnightAndLeaveUnrecordedHoursUnknown()
        {
            var result = UsageAnalytics.Analyze(new[]
            {
                Segment(Utc(29, 23, 50), Utc(30, 0, 10), true),
                Segment(Utc(30, 0, 10), Utc(30, 0, 40), false),
                Segment(Utc(30, 0, 55), Utc(30, 1, 10), true),
                Segment(Utc(30, 23, 50), Utc(31, 0, 10), true),
            }, new DateTime(2026, 10, 30), TimeZoneInfo.Utc);

            Assert.AreEqual(35 * 60, result.ActiveSeconds, 0.001);
            Assert.AreEqual(65 * 60, result.RecordedSeconds, 0.001);
            Assert.AreEqual(15, result.Hours[0].ActiveMinutes, 0.001);
            Assert.AreEqual(30, result.Hours[0].InactiveMinutes, 0.001);
            Assert.AreEqual(10, result.Hours[1].ActiveMinutes, 0.001);
            Assert.AreEqual(10, result.Hours[23].ActiveMinutes, 0.001);
            Assert.IsTrue(double.IsNaN(result.Hours[2].ActiveMinutes));
            Assert.IsTrue(double.IsNaN(result.Hours[2].InactiveMinutes));
            Assert.AreEqual(1, result.MostActiveHour!.Hour);
            Assert.AreEqual(0, result.LeastActiveHour!.Hour);
        }

        [TestMethod]
        public void ActiveStretchesMergeAppSwitchesButNotUnrecordedGaps()
        {
            var firstApp = Segment(Utc(30, 9), Utc(30, 9, 20), true);
            firstApp.AppName = "Editor";
            var secondApp = Segment(Utc(30, 9, 20, 1), Utc(30, 9, 45), true);
            secondApp.AppName = "Browser";
            var result = UsageAnalytics.Analyze(new[]
            {
                Segment(Utc(30, 10, 5), Utc(30, 10, 15), true),
                secondApp,
                Segment(Utc(30, 9, 45, 1), Utc(30, 9, 50), false),
                Segment(Utc(30, 9, 50, 1), Utc(30, 10), true),
                firstApp,
            }, new DateTime(2026, 10, 30), TimeZoneInfo.Utc);

            Assert.AreEqual(Utc(30, 9), result.LongestActiveStretch!.StartUtc);
            Assert.AreEqual(Utc(30, 9, 45), result.LongestActiveStretch.EndUtc);
            Assert.AreEqual(45 * 60, result.LongestActiveStretch.Seconds, 0.001);
            Assert.AreEqual(299, result.LongestInactiveStretch!.Seconds, 0.001);
        }

        [TestMethod]
        public void AnObservedIdleSecondEndsAnActiveStretch()
        {
            var result = UsageAnalytics.Analyze(new[]
            {
                Segment(Utc(30, 8), Utc(30, 8, 10), true),
                Segment(Utc(30, 8, 10), Utc(30, 8, 10, 1), false),
                Segment(Utc(30, 8, 10, 1), Utc(30, 8, 25), true),
            }, new DateTime(2026, 10, 30), TimeZoneInfo.Utc);

            Assert.AreEqual(Utc(30, 8, 10, 1), result.LongestActiveStretch!.StartUtc);
            Assert.AreEqual(899, result.LongestActiveStretch.Seconds, 0.001);
        }

        [TestMethod]
        public void WeekdayScoresUseRecordedTimeAndExcludeMissingAndOutOfRangeDays()
        {
            var result = UsageAnalytics.Analyze(new[]
            {
                Segment(Utc(26, 9), Utc(26, 10), true),
                Segment(Utc(26, 10), Utc(26, 11), false),
                Segment(new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc), true),
                Segment(new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc), true),
                Segment(new DateTime(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 3, 10, 30, 0, DateTimeKind.Utc), false),
                Segment(Utc(1, 9), Utc(1, 23), true),
                Segment(new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 4, 23, 0, 0, DateTimeKind.Utc), true),
            }, new DateTime(2026, 11, 3), TimeZoneInfo.Utc);

            Assert.AreEqual(3, result.RecordedDays);
            Assert.AreEqual(DayOfWeek.Monday, result.MostActiveWeekday!.Day);
            Assert.AreEqual(2, result.MostActiveWeekday.RecordedDays);
            Assert.AreEqual(2.0 / 3, result.MostActiveWeekday.ActiveShare, 0.001);
            Assert.AreEqual(3600, result.MostActiveWeekday.AverageActiveSeconds, 0.001);
            Assert.AreEqual(DayOfWeek.Tuesday, result.LeastActiveWeekday!.Day);
            Assert.AreEqual(1.0 / 3, result.LeastActiveWeekday.ActiveShare, 0.001);
        }

        [TestMethod]
        public void EmptyHistoryDoesNotInventRankingsOrRecordedHours()
        {
            var result = UsageAnalytics.Analyze(
                Array.Empty<UsageSegment>(), new DateTime(2026, 10, 30), TimeZoneInfo.Utc);

            Assert.AreEqual(0, result.RecordedDays);
            Assert.IsNull(result.ActiveShare);
            Assert.IsNull(result.MostActiveHour);
            Assert.IsNull(result.LeastActiveHour);
            Assert.IsNull(result.MostActiveWeekday);
            Assert.IsNull(result.LeastActiveWeekday);
            Assert.IsNull(result.LongestActiveStretch);
            Assert.IsTrue(result.Hours.All(hour => double.IsNaN(hour.ActiveMinutes)));
        }

        [TestMethod]
        public void AUniformRecordingDoesNotInventAQuietHourOrWeekday()
        {
            var result = UsageAnalytics.Analyze(new[]
            {
                Segment(Utc(30, 9, 15), Utc(30, 10, 30), true),
            }, new DateTime(2026, 10, 30), TimeZoneInfo.Utc);

            Assert.AreEqual(9, result.MostActiveHour!.Hour);
            Assert.IsNull(result.LeastActiveHour);
            Assert.IsNull(result.LeastActiveWeekday);
            Assert.AreEqual(1.0, result.ActiveShare!.Value, 0.001);
        }

        [DataTestMethod]
        [DataRow(2026, 3, 8, 23)]
        [DataRow(2026, 11, 1, 25)]
        public void DaylightSavingDaysKeepTheirActualDuration(int year, int month, int day, int hours)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            var date = new DateTime(year, month, day);
            var start = TimeZoneInfo.ConvertTimeToUtc(date, zone);
            var end = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1), zone);
            var result = UsageAnalytics.Analyze(new[]
            {
                Segment(start.AddHours(-1), end.AddHours(1), true),
            }, date, zone);

            Assert.AreEqual(hours * 3600, result.ActiveSeconds, 0.001);
            Assert.AreEqual(hours * 3600, result.LongestActiveStretch!.Seconds, 0.001);
            if (hours == 23)
            {
                Assert.IsTrue(double.IsNaN(result.Hours[2].ActiveMinutes));
            }
            else
            {
                Assert.AreEqual(120, result.Hours[1].ActiveMinutes, 0.001);
            }
        }

        [TestMethod]
        public void PoweredDownTimeAppearsInChartsButDoesNotLowerProductivityScores()
        {
            var sleep = Segment(Utc(30, 0), Utc(30, 8), false);
            sleep.InactiveReason = InactivityReason.Sleep;
            var locked = Segment(Utc(30, 9), Utc(30, 10), false);
            locked.InactiveReason = InactivityReason.Locked;
            var off = Segment(Utc(29), Utc(30), false);
            off.InactiveReason = InactivityReason.PoweredOff;
            var result = UsageAnalytics.Analyze(new[]
            {
                sleep, locked, off,
                Segment(Utc(30, 8), Utc(30, 9), true),
            }, new DateTime(2026, 10, 30), TimeZoneInfo.Utc);

            Assert.AreEqual(10 * 3600, result.RecordedSeconds, 0.001);
            Assert.AreEqual(2 * 3600, result.AwakeRecordedSeconds, 0.001);
            Assert.AreEqual(0.5, result.ActiveShare!.Value, 0.001);
            Assert.AreEqual(60, result.Hours[2].InactiveMinutes, 0.001);
            Assert.AreEqual(0, result.Hours[2].AwakeRecordedSeconds, 0.001);
            Assert.AreEqual(8, result.MostActiveHour!.Hour);
            Assert.AreEqual(9, result.LeastActiveHour!.Hour);
            Assert.AreEqual(1, result.RecordedDays);
            Assert.AreEqual(DayOfWeek.Friday, result.MostActiveWeekday!.Day);
            Assert.AreEqual(0.5, result.MostActiveWeekday.ActiveShare, 0.001);
            Assert.IsNull(result.LeastActiveWeekday);
            Assert.AreEqual(8 * 3600, result.LongestInactiveStretch!.Seconds, 0.001);
        }

        [TestMethod]
        public void EntirelyPoweredDownDayHasEvidenceButNoAwakeRankings()
        {
            var off = Segment(Utc(30), Utc(31), false);
            off.InactiveReason = InactivityReason.PoweredOff;
            var result = UsageAnalytics.Analyze(new[] { off }, Utc(30), TimeZoneInfo.Utc);

            Assert.AreEqual(24 * 3600, result.RecordedSeconds, 0.001);
            Assert.IsNull(result.ActiveShare);
            Assert.IsNull(result.MostActiveHour);
            Assert.IsNull(result.LeastActiveHour);
            Assert.IsNull(result.MostActiveWeekday);
            Assert.AreEqual(0, result.RecordedDays);
            Assert.IsTrue(result.Hours.All(hour => hour.InactiveMinutes == 60));
        }

        private static DateTime Utc(int day, int hour = 0, int minute = 0, int second = 0)
        {
            return new DateTime(2026, 10, day, hour, minute, second, DateTimeKind.Utc);
        }

        private static UsageSegment Segment(DateTime start, DateTime end, bool active)
        {
            return new UsageSegment { StartUtc = start, EndUtc = end, IsActive = active };
        }
    }
}
