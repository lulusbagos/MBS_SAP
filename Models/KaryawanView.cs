using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class KaryawanView
    {
        [Key]
        public int IdKaryawan { get; set; }

        public int IdPersonal { get; set; }

        [Required]
        [MaxLength(50)]
        public string NoNik { get; set; } = string.Empty;

        public DateTime? TanggalMasuk { get; set; }

        public int IdPerusahaan { get; set; }

        public int? IdDepartemen { get; set; }

        public int? IdSeksi { get; set; }

        public int? IdJabatan { get; set; }

        public bool StatusAktif { get; set; }

        public int? PerusahaanNodeId { get; set; }
    }
}
