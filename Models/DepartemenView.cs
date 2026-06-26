using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class DepartemenView
    {
        [Key]
        public int DepartemenId { get; set; }

        [MaxLength(15)]
        public string? KodeDepartemen { get; set; }

        [MaxLength(50)]
        public string? NamaDepartemen { get; set; }

        [MaxLength(10)]
        public string? StatusAktif { get; set; }

        public int? IdPerusahaan { get; set; }
    }
}
