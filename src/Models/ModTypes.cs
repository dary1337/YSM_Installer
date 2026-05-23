using System;

namespace YSMInstaller {
    public static class ModTypes {
        public const string Ysm = "ysm";
        public const string YsmWif = "ysm_wif";
        public const string Wto = "wto";

        public static string ToDisplayName(string modType) {
            if (string.Equals(modType, YsmWif, StringComparison.Ordinal)) {
                return "YSM x WiF";
            }

            if (string.Equals(modType, Wto, StringComparison.Ordinal)) {
                return "WTO";
            }

            return "YSM";
        }
    }
}
