using LiveSplit.Model;
using LiveSplit.Model.Comparisons;

namespace LiveSplit.UI.Components;

/// <summary>Label tables ported from LiveSplit's RunPrediction component (MIT)
/// so Reckoning presents identically for every comparison.
/// Deviation from stock (Andrew, 2026-07-31 live review): unmapped comparisons display their own name instead of "Current Pace (name)".</summary>
internal static class ComparisonNaming
{
    // Ported from LiveSplit's RunPrediction component (MIT).
    public static string GetDisplayedName(string comparison) => comparison switch
    {
        "Current Comparison" => "Current Pace",
        Run.PersonalBestComparisonName => "Current Pace",
        BestSegmentsComparisonGenerator.ComparisonName => "Best Possible Time",
        WorstSegmentsComparisonGenerator.ComparisonName => "Worst Possible Time",
        AverageSegmentsComparisonGenerator.ComparisonName => "Predicted Time",
        _ => CompositeComparisons.GetShortComparisonName(comparison),
    };

    public static string[] GetAbbreviations(string comparison) => comparison switch
    {
        BestSegmentsComparisonGenerator.ComparisonName => new[] { "Best Poss. Time", "Best Time", "BPT" },
        WorstSegmentsComparisonGenerator.ComparisonName => new[] { "Worst Poss. Time", "Worst Time" },
        AverageSegmentsComparisonGenerator.ComparisonName => new[] { "Pred. Time" },
        "Current Comparison" => new[] { "Cur. Pace", "Pace" },
        Run.PersonalBestComparisonName => new[] { "Cur. Pace", "Pace" },
        _ => new[] { CompositeComparisons.GetShortComparisonName(comparison) },
    };

    /// <summary>Stock RunPrediction gates "show blank before the run starts" on the displayed
    /// name starting with "Current Pace" — true for Current Comparison, Personal Best, and any
    /// unmapped/custom comparison, false for the three Segments generators. Our display strings
    /// deviate from stock's (unmapped comparisons show their own name, not "Current Pace (name)"),
    /// so that string check no longer selects the right set. This reproduces the same semantic
    /// membership test directly against the comparison, decoupled from the display string.</summary>
    internal static bool IsPaceLike(string comparison) => comparison switch
    {
        BestSegmentsComparisonGenerator.ComparisonName => false,
        WorstSegmentsComparisonGenerator.ComparisonName => false,
        AverageSegmentsComparisonGenerator.ComparisonName => false,
        _ => true,
    };
}
