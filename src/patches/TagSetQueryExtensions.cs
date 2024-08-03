using BattleTech.Data;
using BattleTech.Framework;
using Harmony;
using HBS.Collections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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
                    Units[unitDef.FriendlyName].Add(unitDef.UnitDefID, TagSetQueryExtensions_GetMatchingUnitDefs.numberToAdd(unitDef) + 1);
                } else {
                    Units[unitDef.FriendlyName][unitDef.UnitDefID] = TagSetQueryExtensions_GetMatchingUnitDefs.numberToAdd(unitDef) + 1;
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
                builder.Append("----------------------------------------------------------------------");
                builder.AppendLine();
                builder.AppendFormat("Old Length: {0} | New Length: {1}", OldLength, TotalUnits);
                builder.AppendLine();
                builder.AppendFormat("Required Tags: {0}", string.Join(", ", RequiredTags.items));
                builder.AppendLine();
                builder.AppendFormat("Excluded Tags: {0}", string.Join(", ", ExcludedTags.items));
                builder.AppendLine();
                builder.Append("----------------------------------------------------------------------");
                builder.AppendLine();

                sortedUnits.AddRange(Units.AsEnumerable());
                var sortedList = sortedUnits.OrderByDescending(unit => GetUnitCount(unit.Key)).ToList();
                for (int i = 0; i < sortedList.Count; i++) {
                    builder.AppendFormat("{0,6} | {1,2} - {2}", ((double)GetUnitCount(sortedList[i].Key) / (double)TotalUnits).ToString("p2", numberInfo), GetUnitCount(sortedList[i].Key), sortedList[i].Key);

                    List<KeyValuePair<string, int>> sortedVariants = new();
                    sortedVariants.AddRange(sortedList[i].Value.AsEnumerable());
                    foreach (var variant in sortedVariants.OrderByDescending(unit => unit.Value)) {
                        builder.AppendLine();
                        builder.AppendFormat("\t{0,6} | {1,2} - {2}",((double)variant.Value / (double)TotalUnits).ToString("p2", numberInfo), variant.Value, variant.Key);
                    }
                    builder.AppendLine();
                }
                builder.Append("----------------------------------------------------------------------");
                builder.AppendLine();
                builder.AppendFormat("Selected: {0} - ({1}/{2}) | ({3}/{4})", 
                    Selected.UnitDefID,
                    ((double)GetUnitCount(Selected.FriendlyName) / (double)TotalUnits).ToString("p2", numberInfo),
                    ((double)Units[Selected.FriendlyName][Selected.UnitDefID] / (double)TotalUnits).ToString("p2", numberInfo),
                    GetUnitCount(Selected.FriendlyName),
                    Units[Selected.FriendlyName][Selected.UnitDefID]);
                builder.AppendLine();
                builder.Append("----------------------------------------------------------------------");
                builder.AppendLine();
                return builder.ToString();
            }
        }

        [HarmonyPatch(typeof(TagSetQueryExtensions), "GetMatchingUnitDefs")]
        public static class TagSetQueryExtensions_GetMatchingUnitDefs {
            private static Dictionary<string, int> numberToAddCache = new Dictionary<string, int>();
            public  static int numberToAdd(UnitDef_MDD unitDef) {
                if (numberToAddCache.ContainsKey(unitDef.UnitDefID)) {
                    return numberToAddCache[unitDef.UnitDefID];
                }

                Settings s = RTS.settings;
                int toAdd = 0;

                foreach (Tag_MDD tag in unitDef.TagSetEntry.Tags) {
                    if (s.moreCommonTags.ContainsKey(tag.Name)) {
                        toAdd += s.moreCommonTags[tag.Name];
                    }
                }
                numberToAddCache[unitDef.UnitDefID] = toAdd;
                return toAdd;
            }

            private static void Postfix(ref List<UnitDef_MDD> __result, TagSet requiredTags, TagSet excludedTags, TagSet companyTags) {
                try {
                    TagBreakdown.Instance = new TagBreakdown(__result.Count, requiredTags, excludedTags, companyTags);
                    foreach (UnitDef_MDD unitDef in __result.ToArray()) {
                        int toAdd = numberToAdd(unitDef);
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

                } catch (Exception e) {
                    RTS.modLog.Error?.Write(e);
                }
            }
        }
    }
    
}
