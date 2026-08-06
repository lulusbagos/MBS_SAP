using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class PerusahaanView
    {
        [Key]
        public int PerusahaanId { get; set; }

        [MaxLength(15)]
        public string? KodePerusahaan { get; set; }

        [MaxLength(200)]
        public string? NamaPerusahaan { get; set; }

        [MaxLength(200)]
        [Column("nama_pjo")]
        public string? NamaPjo { get; set; }

        public int? TipePerusahaanId { get; set; }

        public int? PerusahaanIndukId { get; set; }

        public bool StatusAktif { get; set; }
    }
}
