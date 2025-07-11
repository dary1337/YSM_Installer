using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller {
    public class WarnoSupportedVersions {

        private static List<ModMetadata> list = new List<ModMetadata>();

        public static async Task<List<ModMetadata>> Update() {
            list = await ModListFetcher.DownloadModListAsync();

            return list;
        }

        public static List<ModMetadata> Get() {
            return list;
        }
    }
}
