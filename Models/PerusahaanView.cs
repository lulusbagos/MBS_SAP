using System.ComponentModel.DataAnnotations;

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

        public int? TipePerusahaanId { get; set; }

        public int? PerusahaanIndukId { get; set; }

        public bool StatusAktif { get; set; }
    }
}
