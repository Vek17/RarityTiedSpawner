using Harmony;
using IRBTModUtils.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace RarityTiedSpawner {
    public class RTS {
        internal static DeferringLogger modLog;
        internal static string modDir;
        internal static Settings settings;

        public static void Init(string modDirectory, string settingsJSON) {
            modDir = modDirectory;

            try {
                using (StreamReader reader = new StreamReader($"{modDir}/settings.json")) {
                    string jdata = reader.ReadToEnd();
                    settings = JsonConvert.DeserializeObject<Settings>(jdata);
                }
                modLog = new DeferringLogger(modDirectory, "RarityTiedSpawner", "RTS", settings.Debug, settings.Trace);
                modLog.Debug?.Write($"Loaded settings from {modDir}/settings.json. Version {typeof(Settings).Assembly.GetName().Version}");
                modLog.Debug?.Write($"ExcludeTag: {settings.ExcludeTag}");
                modLog.Debug?.Write($"DynamicTag: {settings.DynamicTag}");
            } catch (Exception e) {
                settings = new Settings();
                modLog = new DeferringLogger(modDir, "RarityTiedSpawner", "RTS", true, true);
                modLog.Error?.Write(e);
            }

            var harmony = HarmonyInstance.Create("blue.winds.RarityTiedSpawner");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
