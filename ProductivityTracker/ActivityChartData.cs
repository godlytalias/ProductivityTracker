using System;
using System.Collections.Generic;

namespace ProductivityTracker
{
    internal sealed record ActivityChartData(
        string[] Labels, double[] Active, double[] Inactive, double Maximum, string AxisLabel, string UnitLabel);

    internal static class ActivityChartPoints
    {
        public static IEnumerable<(int Index, double Value)[]> GetLineRuns(IReadOnlyList<double> values)
        {
            var run = new List<(int, double)>();
            for (var index = 0; index < values.Count; index++)
            {
                if (double.IsFinite(values[index]))
                {
                    run.Add((index, values[index]));
                }
                else if (run.Count > 0)
                {
                    yield return run.ToArray();
                    run.Clear();
                }
            }
            if (run.Count > 0)
            {
                yield return run.ToArray();
            }
        }
    }
}
