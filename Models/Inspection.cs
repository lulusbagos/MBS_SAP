using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class Inspection
    {
        [Key]
        public int Id { get; set; }

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
        [MaxLength(150)]
        public string JenisInspeksi { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? Pja { get; set; }

        [MaxLength(50)]
        public string? NikPja { get; set; }

        [MaxLength(150)]
        public string? DepartemenPja { get; set; }

        public int? PerusahaanId { get; set; }

        public int Q1_1 { get; set; } = 0;
        public int Q1_2 { get; set; } = 0;
        public int Q1_3 { get; set; } = 0;
        
        public int Q2_1 { get; set; } = 0;
        public int Q2_2 { get; set; } = 0;
        public int Q2_3 { get; set; } = 0;
        
        public int Q3_1 { get; set; } = 0;
        public int Q3_2 { get; set; } = 0;
        public int Q3_3 { get; set; } = 0;
        
        public int Q4_1 { get; set; } = 0;
        public int Q4_2 { get; set; } = 0;
        public int Q4_3 { get; set; } = 0;
        
        public int Q5_1 { get; set; } = 0;
        public int Q5_2 { get; set; } = 0;
        public int Q5_3 { get; set; } = 0;

        [MaxLength(2000)]
        public string? Catatan { get; set; }

        public string? LampiranJson { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
