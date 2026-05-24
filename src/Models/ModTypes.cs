using System;

namespace YSMInstaller {
    public static class ModTypes {
        public const string Ysm = "ysm";
        public const string YsmWif = "ysm_wif";
        public const string YsmWifWto = "ysm_wif_wto";
        public const string Wto = "wto";
        public const string Manual = "manual";

        public static string ToDisplayName(string modType) {
            if (string.Equals(modType, YsmWif, StringComparison.Ordinal)) {
                return "YSM x WiF";
            }

            if (string.Equals(modType, YsmWifWto, StringComparison.Ordinal)) {
                return "YSM x WiF x WTO";
            }

            if (string.Equals(modType, Wto, StringComparison.Ordinal)) {
                return "WTO";
            }

            if (string.Equals(modType, Manual, StringComparison.Ordinal)) {
                return "Manual install";
            }

            return "YSM";
        }
    }
}
