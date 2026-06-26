using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class HazardReport
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(500)]
        public string? FotoTemuan { get; set; }

        [Required]
        public DateTime Tanggal { get; set; } = DateTime.Today;

        [Required]
        public TimeSpan Waktu { get; set; } = DateTime.Now.TimeOfDay;

        [Required]
        [MaxLength(150)]
        public string Nama { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? Departemen { get; set; }

        [MaxLength(150)]
        public string? Area { get; set; }

        [MaxLength(150)]
        public string? Lokasi { get; set; }

        [MaxLength(250)]
        public string? DetilLokasi { get; set; }

        [Required]
        public string Temuan { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? KategoriBahaya { get; set; } // Kondisi Tidak Aman / Tindakan Tidak Aman

        [MaxLength(100)]
        public string? JenisBahaya { get; set; }

        [MaxLength(150)]
        public string? JenisKetidaksesuaian { get; set; }

        [MaxLength(50)]
        public string? TingkatResiko { get; set; } // Low / Medium / High

        public string? Perbaikan { get; set; }

        public string? TindakanPerbaikan { get; set; }

        [MaxLength(150)]
        public string? Pja { get; set; } // Penanggung Jawab Area

        [MaxLength(50)]
        public string? NikPja { get; set; }

        [MaxLength(150)]
        public string? DepartemenPja { get; set; }

        [Required]
        [MaxLength(50)]
        public string StatusTemuan { get; set; } = "Open"; // Open / Closed

        public int? PerusahaanId { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
