namespace YSMInstaller
{
    public static class ModTypes
    {
        public const string Ysm = "ysm";
        public const string YsmWif = "ysm_wif";

        public static string ToDisplayName(string modType)
        {
            return modType == YsmWif ? "YSM x WiF" : "YSM";
        }
    }
}
