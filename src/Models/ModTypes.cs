namespace YSMInstaller {
    public static class ModTypes {
        public const string Ysm = "ysm";
        public const string YsmWif = "ysm_wif";
        public const string Wto = "wto";

        public static string ToDisplayName(string modType) {
            if (modType == YsmWif) {
                return "YSM x WiF";
            }

            if (modType == Wto) {
                return "WTO";
            }

            return "YSM";
        }
    }
}
