using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class JabatanView
    {
        [Key]
        public int JabatanId { get; set; }

        [MaxLength(10)]
        public string? KodeJabatan { get; set; }

        [MaxLength(50)]
        public string? NamaJabatan { get; set; }

        [MaxLength(10)]
        public string? StatusAktif { get; set; }

        public int? IdPerusahaan { get; set; }

        public int? IdSeksi { get; set; }
    }
}
