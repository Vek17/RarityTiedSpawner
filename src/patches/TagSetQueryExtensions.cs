using BattleTech.Data;
using BattleTech.Framework;
using Harmony;
using HBS.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RarityTiedSpawner {
    public static class RarityModifications {
        private class TagBreakdown {
            public Dictionary<string, Dictionary<string, int>> Units = new Dictionary<string, Dictionary<string, int>>();
            private int _totalUnits = 0;
            public readonly int OldLength;
            public readonly TagSet RequiredTags;
            public readonly TagSet ExcludedTags;
            public readonly TagSet CompanyTags;
            public UnitDef_MDD Selected { get; set; }

            public static TagBreakdown Instance;

            private readonly static NumberFormatInfo numberInfo = new NumberFormatInfo() {
                PercentPositivePattern = 1
            };
            public int TotalUnits {
                get {
                    if (_totalUnits != 0) {
                        return _totalUnits;
                    }
                    foreach (var unit in Units) {
                        _totalUnits += GetUnitCount(unit.Key);
                    }
                    return _totalUnits;
                }
            }

            public TagBreakdown(int oldLength, TagSet requiredTags, TagSet excludedTags, TagSet companyTags) {
                this.OldLength = oldLength;
                this.RequiredTags = requiredTags;
                this.ExcludedTags = excludedTags;
                this.CompanyTags = companyTags;
            }

            public void AddUnit(UnitDef_MDD unitDef) {
                if (!Units.ContainsKey(unitDef.FriendlyName)) {
                    Units.Add(unitDef.FriendlyName, new Dictionary<string, int>());
                }
                if (!Units[unitDef.FriendlyName].ContainsKey(unitDef.UnitDefID)) {
                    Units[unitDef.FriendlyName].Add(unitDef.UnitDefID, Math.Max(0, TagCache.Instance.GetNumberToAdd(unitDef, RequiredTags, ExcludedTags)) + 1);
                } else {
                    Units[unitDef.FriendlyName][unitDef.UnitDefID] = Math.Max(0, TagCache.Instance.GetNumberToAdd(unitDef, RequiredTags, ExcludedTags)) + 1;
                }
            }

            public int GetUnitCount(string FriendlyName) {
                var result = 0;
                if (!Units.ContainsKey(FriendlyName)) {
                    return result;
                }
                foreach (var unit in Units[FriendlyName]) {
                    result += unit.Value;
                }
                return result;
            }

            public string CreateOutputTable() {
                StringBuilder builder = new StringBuilder();
                List<KeyValuePair<string, Dictionary<string, int>>> sortedUnits = new();
                builder.AppendLine();
                builder.Append("----------------------------------------------------------------------------------------------------");
                builder.AppendLine();
                builder.AppendFormat("Old Length: {0} | New Length: {1}", OldLength, TotalUnits);
                builder.AppendLine();
                builder.AppendFormat("Required Tags: {0}", string.Join(", ", RequiredTags.items));
                builder.AppendLine();
                builder.AppendFormat("Excluded Tags: {0}", string.Join(", ", ExcludedTags.items));
                builder.AppendLine();
                builder.Append("----------------------------------------------------------------------------------------------------");
                builder.AppendLine();

                sortedUnits.AddRange(Units.AsEnumerable());
                var sortedList = sortedUnits.OrderByDescending(unit => GetUnitCount(unit.Key)).ToList();
                for (int i = 0; i < sortedList.Count; i++) {
                    builder.AppendFormat("{0,6} | {1,2} - {2}", ((double)GetUnitCount(sortedList[i].Key) / (double)TotalUnits).ToString("p2", numberInfo), GetUnitCount(sortedList[i].Key), sortedList[i].Key);

                    List<KeyValuePair<string, int>> sortedVariants = new();
                    sortedVariants.AddRange(sortedList[i].Value.AsEnumerable());
                    foreach (var variant in sortedVariants.OrderByDescending(unit => unit.Value)) {
                        builder.AppendLine();
                        builder.AppendFormat("\t{0,6} | {1,2} - {2}", ((double)variant.Value / (double)TotalUnits).ToString("p2", numberInfo), variant.Value, variant.Key);
                    }
                    builder.AppendLine();
                }
                builder.Append("----------------------------------------------------------------------------------------------------");
                builder.AppendLine();
                builder.AppendFormat("Selected: {0} - {1} | ({2}/{3}) | ({4}/{5})",
                    Selected.FriendlyName,
                    Selected.UnitDefID,
                    ((double)GetUnitCount(Selected.FriendlyName) / (double)TotalUnits).ToString("p2", numberInfo),
                    ((double)Units[Selected.FriendlyName][Selected.UnitDefID] / (double)TotalUnits).ToString("p2", numberInfo),
                    GetUnitCount(Selected.FriendlyName),
                    Units[Selected.FriendlyName][Selected.UnitDefID]);
                builder.AppendLine();
                builder.Append("----------------------------------------------------------------------------------------------------");
                builder.AppendLine();
                return builder.ToString();
            }
        }

        private class TagCache {
            private HashSet<string> NonTagCachhe;
            private Dictionary<string, int> MoreCommonTags;
            private Dictionary<string, Regex> GenericTags;
            private Dictionary<string, int> NumberStrings;
            private Dictionary<string, int> NegativeTags;
            private Dictionary<string, int> PositiveTags;
            private Dictionary<string, List<Tag_MDD>> MechTagCache;

            private static Regex EndNumberPattern = new Regex(@"-?\d+$", RegexOptions.Compiled);
            private static Regex NegativePattern = new Regex($"^{RTS.settings.ExcludeTag}_.+{RTS.settings.DynamicTag}_-?\\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static Regex PositiveTagPattern = new Regex($"^.+_{RTS.settings.DynamicTag}_-?\\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static Regex TagPattern = new Regex($"^{RTS.settings.DynamicTag}_-?\\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static Regex DynamicTagPattern = new Regex($"{RTS.settings.DynamicTag}_-?\\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            public long timeUsed = 0;

            private static TagCache _instance;
            public static TagCache Instance {
                get {
                    if (_instance == null) {
                        _instance = new TagCache();
                    }
                    return _instance;
                }
            }

            private TagCache() {
                NonTagCachhe = new HashSet<string>();
                MoreCommonTags = RTS.settings.MoreCommonTags;
                GenericTags = new Dictionary<string, Regex>();
                NumberStrings = new Dictionary<string, int> {
                    { "0", 0 },
                    { "1", 1 },
                    { "2", 2 },
                    { "3", 3 },
                    { "4", 4 },
                    { "5", 5 },
                    { "6", 6 },
                    { "7", 7 },
                    { "8", 8 },
                    { "9", 9 },
                    { "10", 10 },
                    { "11", 11 },
                    { "12", 12 },
                    { "13", 13 },
                    { "14", 14 },
                    { "15", 15 },
                    { "16", 16 },
                    { "17", 17 },
                    { "18", 18 },
                    { "19", 19 },
                    { "20", 20 },
                };
                PositiveTags = new Dictionary<string,int>();
                NegativeTags = new Dictionary<string, int>();
                MechTagCache = new Dictionary<string, List<Tag_MDD>>();
            }

            public int GetNumberToAdd(UnitDef_MDD unitDef, TagSet requiredTags, TagSet excludedTags, bool logTime = false) {
                var stopwatch = new Stopwatch();
                if (logTime) {
                    stopwatch.Start();
                }
                int toAdd = 0;

                if (!MechTagCache.ContainsKey(unitDef.UnitDefID)) {
                    MechTagCache.Add(unitDef.UnitDefID, unitDef.TagSetEntry.Tags);
                }
                foreach (Tag_MDD tag in MechTagCache[unitDef.UnitDefID]) {
                    if (MoreCommonTags.ContainsKey(tag.Name)) {
                        toAdd += MoreCommonTags[tag.Name];
                        continue;
                    }
                    if (TagPattern.IsMatch(tag.Name)) {
                        var numString = EndNumberPattern.Match(tag.Name).Value;
                        var num = 0;
                        if (NumberStrings.ContainsKey(numString)) {
                            num = NumberStrings[numString];
                        } else {
                            num = int.Parse(numString);
                            NumberStrings.Add(numString, num);
                        }
                        MoreCommonTags.Add(tag.Name, num);
                        toAdd += MoreCommonTags[tag.Name];
                        continue;
                    }
                    if (!DynamicTagPattern.IsMatch(tag.Name)) {
                        MoreCommonTags.Add(tag.Name, 0);
                        continue;
                    }
                    var negativeAdd = 0;
                    foreach (var negativeTag in excludedTags) {
                        if (!NegativePattern.IsMatch(tag.Name)) {
                            break;
                        }
                        if (!GenericTags.ContainsKey(negativeTag)) {
                            GenericTags.Add(negativeTag, new Regex(Regex.Escape(negativeTag), RegexOptions.Compiled | RegexOptions.IgnoreCase));
                        }
                        if (!GenericTags[negativeTag].IsMatch(tag.Name)) {
                            continue;
                        }
                        if (NegativeTags.ContainsKey(tag.Name)) {
                            negativeAdd = NegativeTags[tag.Name];
                            break;
                        } else {
                            var numString = EndNumberPattern.Match(tag.Name).Value;
                            var num = 0;
                            if (NumberStrings.ContainsKey(numString)) {
                                num = NumberStrings[numString];
                            } else {
                                num = int.Parse(numString);
                            }
                            NegativeTags.Add(tag.Name, num);
                            negativeAdd = NegativeTags[tag.Name];
                            break;
                        }
                    }
                    if (negativeAdd > 0) {
                        toAdd += negativeAdd;
                        continue;
                    }
                    var positiveAdd = 0;
                    foreach (var positiveTag in requiredTags) {
                        if (!PositiveTagPattern.IsMatch(tag.Name)) {
                            break;
                        }
                        if (!GenericTags.ContainsKey(positiveTag)) {
                            GenericTags.Add(positiveTag, new Regex(Regex.Escape(positiveTag), RegexOptions.Compiled | RegexOptions.IgnoreCase));
                        }
                        if (!GenericTags[positiveTag].IsMatch(tag.Name)) {
                            continue;
                        }
                        if (PositiveTags.ContainsKey(tag.Name)) {
                            positiveAdd = PositiveTags[tag.Name];
                            break;
                        } else {
                            var numString = EndNumberPattern.Match(tag.Name).Value;
                            var num = 0;
                            if (NumberStrings.ContainsKey(numString)) {
                                num = NumberStrings[numString];
                            } else {
                                num = int.Parse(numString);
                            }
                            PositiveTags.Add(tag.Name, num);
                            positiveAdd = PositiveTags[tag.Name];
                            break;
                        }
                    }
                    if (positiveAdd > 0) {
                        toAdd += positiveAdd;
                        continue;
                    }
                }
                if (logTime) { 
                    stopwatch.Stop();
                    timeUsed += stopwatch.ElapsedMilliseconds;
                }
                return toAdd;
            }
        }

        [HarmonyPatch(typeof(TagSetQueryExtensions), "GetMatchingUnitDefs")]
        public static class TagSetQueryExtensions_GetMatchingUnitDefs {
            private static Dictionary<string, int> numberToAddCache = new Dictionary<string, int>();
            public static int numberTsoAdd(UnitDef_MDD unitDef) {
                if (numberToAddCache.ContainsKey(unitDef.UnitDefID)) {
                    return numberToAddCache[unitDef.UnitDefID];
                }

                Settings s = RTS.settings;
                int toAdd = 0;

                foreach (Tag_MDD tag in unitDef.TagSetEntry.Tags) {
                    if (s.MoreCommonTags.ContainsKey(tag.Name)) {
                        toAdd += s.MoreCommonTags[tag.Name];
                    }
                }
                numberToAddCache[unitDef.UnitDefID] = toAdd;
                return toAdd;
            }

            private static void Postfix(ref List<UnitDef_MDD> __result, TagSet requiredTags, TagSet excludedTags, TagSet companyTags) {
                try {
                    TagBreakdown.Instance = new TagBreakdown(__result.Count, requiredTags, excludedTags, companyTags);
                    foreach (UnitDef_MDD unitDef in __result.ToArray()) {
                        //int toAdd = numberToAdd(unitDef);
                        int toAdd = TagCache.Instance.GetNumberToAdd(unitDef, requiredTags, excludedTags, true);
                        TagBreakdown.Instance.AddUnit(unitDef);
                        if (toAdd > 0) {
                            for (int i = 0; i < toAdd; i++) {
                                __result.Add(unitDef);
                            }
                        }
                    }

                } catch (Exception e) {
                    RTS.modLog.Error?.Write(e);
                }
            }
        }

        [HarmonyPatch(typeof(UnitSpawnPointOverride), nameof(UnitSpawnPointOverride.SelectTaggedUnitDef))]
        public static class TagSetQueryExtensions_SelectTaggedUnitDef {
            private static void Postfix(ref UnitDef_MDD __result) {
                try {
                    if (TagBreakdown.Instance == null) {
                        RTS.modLog.Info?.Write("Failed to find TagBreakdown");
                        return;
                    }
                    TagBreakdown.Instance.Selected = __result;
                    RTS.modLog.Info?.Write($"Generating new Spawn Table\n{TagBreakdown.Instance.CreateOutputTable()}");
                    RTS.modLog.Info?.Write($"Tag Processing Time Total: {TagCache.Instance.timeUsed}ms");

                } catch (Exception e) {
                    RTS.modLog.Error?.Write(e);
                }
            }
        }
    }
    
}
