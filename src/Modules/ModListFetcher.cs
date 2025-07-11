using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YSMInstaller {
    public static class ModListFetcher {

        private const string ModListUrl = "https://raw.githubusercontent.com/dary1337/YSM_Installer/master/mods-list.json";

        public static async Task<List<ModMetadata>> DownloadModListAsync() {
            try {
                var json = await HttpService.GetStringAsync(ModListUrl);
                var mods = JsonConvert.DeserializeObject<List<ModMetadata>>(json);
                return mods ?? new List<ModMetadata>();
            }
            catch (Exception) {
                MessageBox.Show("Cant load supported versions. Check your internet connection");
                return new List<ModMetadata>();
            }
        }
    }
}
