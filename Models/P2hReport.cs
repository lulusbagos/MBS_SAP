using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class P2hReport
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Nama { get; set; } = string.Empty;

        [Required]
        public DateTime Tanggal { get; set; } = DateTime.Today;

        [Required]
        public TimeSpan Waktu { get; set; } = DateTime.Now.TimeOfDay;

        [Required]
        [MaxLength(100)]
        public string JenisKendaraan { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string NoLambung { get; set; } = string.Empty;

        public double Kilometer { get; set; }

        [Required]
        [MaxLength(200)]
        public string Merek { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string SimperKimper { get; set; } = "TIDAK"; // YA / TIDAK

        [MaxLength(500)]
        public string? FotoSpeedometer { get; set; }

        public string? GolA_Json { get; set; } // Checklist results for Gol. A
        public string? GolB_Json { get; set; } // Checklist results for Gol. B
        public string? GolC_Json { get; set; } // Checklist results for Gol. C

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
