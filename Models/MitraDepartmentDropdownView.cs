using System;

namespace MBS_SAP.Models
{
    public class MitraDepartmentDropdownView
    {
        public int IdPerusahaan { get; set; }
        public string? KodePerusahaan { get; set; }
        public string? NamaPerusahaan { get; set; }
        public int? IdDepartemen { get; set; }
        public string? KodeDepartemen { get; set; }
        public string? NamaDepartemen { get; set; }
        public string? LabelDropdown { get; set; }
        public bool? PerusahaanStatusAktif { get; set; }
        public string? DepartemenStatusAktif { get; set; }
        public DateTime? PerusahaanDeletedAt { get; set; }
        public string? GroupLabel { get; set; }
        public string? OptionLabel { get; set; }
        public int? SortOrder { get; set; }
    }
}
