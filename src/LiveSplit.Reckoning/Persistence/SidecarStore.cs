using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using LiveSplit.Reckoning.Engine;

namespace LiveSplit.Reckoning.Persistence;

/// <summary>Sidecar JSON next to the splits file. Learned data is precious but
/// replaceable: any load problem degrades to an empty (unlearned) store and a
/// fresh save rebuilds the file — never crash the component over it.</summary>
internal static class SidecarStore
{
    private const int SchemaVersion = 1;
    private const string Suffix = ".reckoning.json";

    public static string PathFor(string lssPath) => lssPath + Suffix;

    public static BestsStore Load(string sidecarPath)
    {
        var store = new BestsStore();
        try
        {
            if (!File.Exists(sidecarPath)) return store;
            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(sidecarPath))
                as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("segments", out var segsObj) && segsObj is object[] segs))
                return store;
            foreach (var segObj in segs)
            {
                if (segObj is not Dictionary<string, object> seg) continue;
                if (!(seg.TryGetValue("index", out var idxObj) && idxObj is int segIndex)) continue;
                if (!(seg.TryGetValue("markers", out var marksObj) && marksObj is object[] marks)) continue;
                foreach (var markObj in marks)
                {
                    if (markObj is not Dictionary<string, object> mark) continue;
                    if (!(mark.TryGetValue("marker", out var mObj) && mObj is int marker)) continue;
                    if (!(mark.TryGetValue("bestMs", out var bObj) && bObj is int or long)) continue;
                    if (!(mark.TryGetValue("attempts", out var aObj) && aObj is int attempts)) continue;
                    Variant variant;
                    switch (mark.TryGetValue("variant", out var vObj) ? vObj as string : null)
                    {
                        case "hot": variant = Variant.Hot; break;
                        case "cold": variant = Variant.Cold; break;
                        default: continue;   // unknown variant: skip entry, keep the rest
                    }
                    store.SetEntry(new MarkerKey(segIndex, marker, variant),
                        new BestEntry(Convert.ToInt64(bObj), attempts));
                }
            }
        }
        catch
        {
            // Corrupt sidecar: degrade to unlearned (spec §Persistence).
            return new BestsStore();
        }
        return store;
    }

    public static void Save(string sidecarPath, BestsStore store, string lssPath,
        string game, string category, IReadOnlyList<string> segmentNames)
    {
        var segments = new List<object>();
        foreach (var group in store.Keys.GroupBy(k => k.SegmentIndex).OrderBy(g => g.Key))
        {
            var markers = new List<object>();
            foreach (var key in group.OrderBy(k => k.MarkerIndex).ThenBy(k => k.Variant))
            {
                store.TryGetEntry(key, out var entry);
                markers.Add(new Dictionary<string, object>
                {
                    ["marker"] = key.MarkerIndex,
                    ["variant"] = key.Variant == Variant.Hot ? "hot" : "cold",
                    ["bestMs"] = entry.BestMs,
                    ["attempts"] = entry.Attempts,
                });
            }
            segments.Add(new Dictionary<string, object>
            {
                ["index"] = group.Key,
                ["name"] = group.Key < (segmentNames?.Count ?? 0) ? segmentNames[group.Key] : "",
                ["markers"] = markers,
            });
        }
        var root = new Dictionary<string, object>
        {
            ["version"] = SchemaVersion,
            ["lss"] = lssPath ?? "",
            ["game"] = game ?? "",
            ["category"] = category ?? "",
            ["segments"] = segments,
        };
        string json = new JavaScriptSerializer().Serialize(root);

        // Atomic write: temp file in the same directory, then swap.
        string tmp = sidecarPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(sidecarPath)) File.Replace(tmp, sidecarPath, null);
        else File.Move(tmp, sidecarPath);
    }
}
