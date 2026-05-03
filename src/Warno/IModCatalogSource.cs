using System.Collections.Generic;
using System.Threading.Tasks;

namespace YSMInstaller {
    internal interface IModCatalogSource {
        ModCatalogSourceKind Kind { get; }
        string Name { get; }
        Task<List<ModMetadata>> DownloadAsync();
    }
}
