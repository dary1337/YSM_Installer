using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace YSMInstaller {
    public class WarnoEntry {
        public string ExePath { get; set; }
        public string GamePath => Path.GetDirectoryName(ExePath);
        public string ModsPath => Path.Combine(GamePath, "Mods");
        public string VersionPath => Path.Combine(GamePath, "Data", "PC");
        public int Version { get; set; }
        public ModMetadata VersionMetadata { get; set; }
    }
}
