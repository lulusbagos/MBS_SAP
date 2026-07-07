namespace MBS_SAP.Controllers
{
    /// <summary>
    /// Perusahaan yang dikecualikan dari semua perhitungan SAP dan tidak boleh login.
    /// PT SANTAN BORNEO ABADI (276) dan PT GANDA ALAM MAKMUR (272).
    /// </summary>
    public static class ExcludedCompanies
    {
        public static readonly HashSet<int> Ids = new HashSet<int> { 272, 276 };

        public static bool IsExcluded(int companyId) => Ids.Contains(companyId);
    }
}
