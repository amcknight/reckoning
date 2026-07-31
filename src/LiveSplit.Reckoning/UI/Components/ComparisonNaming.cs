using LiveSplit.Model;
using LiveSplit.Model.Comparisons;

namespace LiveSplit.UI.Components;

/// <summary>Label tables ported from LiveSplit's RunPrediction component (MIT)
/// so Reckoning presents identically for every comparison.</summary>
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
        _ => $"Current Pace ({CompositeComparisons.GetShortComparisonName(comparison)})",
    };

    public static string[] GetAbbreviations(string comparison) => comparison switch
    {
        BestSegmentsComparisonGenerator.ComparisonName => new[] { "Best Poss. Time", "Best Time", "BPT" },
        WorstSegmentsComparisonGenerator.ComparisonName => new[] { "Worst Poss. Time", "Worst Time" },
        AverageSegmentsComparisonGenerator.ComparisonName => new[] { "Pred. Time" },
        "Current Comparison" => new[] { "Cur. Pace", "Pace" },
        Run.PersonalBestComparisonName => new[] { "Cur. Pace", "Pace" },
        _ => new[] { "Current Pace", "Cur. Pace", "Pace" },
    };
}
