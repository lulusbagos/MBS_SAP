namespace MBS_SAP.Controllers
{
    /// <summary>
    /// Perusahaan yang dikecualikan dari semua perhitungan SAP dan tidak boleh login.
    /// PT SANTAN BORNEO ABADI (276) dan PT GANDA ALAM MAKMUR (272).
    /// </summary>
    public static class ExcludedCompanies
    {
        public static readonly HashSet<int> Ids = new HashSet<int> { 272, 276, 146, 147, 166, 181, 193, 210, 232, 246, 250, 255, 273, 274, 277, 286, 298, 315, 322, 344, 363 };

        public static bool IsExcluded(int companyId) => Ids.Contains(companyId);
    }
}
