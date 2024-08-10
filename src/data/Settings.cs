using System.Collections.Generic;

namespace RarityTiedSpawner {
    public class Settings {
        public bool Debug = false;
        public bool Trace = false;
        public string ExcludeTag = "";
        public string DynamicTag = "";
        public Dictionary<string, int> MoreCommonTags = new Dictionary<string, int>();
    }
}
