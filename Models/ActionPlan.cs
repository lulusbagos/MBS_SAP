using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class ActionPlan
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(500)]
        public string? FotoTemuan { get; set; }

        [MaxLength(500)]
        public string? FotoPerbaikan { get; set; }

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

        [MaxLength(100)]
        public string? ItemSap { get; set; } // hazard / inspection

        [MaxLength(150)]
        public string? KategoriTemuan { get; set; }

        public string? DetilTemuan { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Open"; // Open / Closed

        [MaxLength(150)]
        public string? Pja { get; set; }

        [MaxLength(50)]
        public string? NikPja { get; set; }

        [MaxLength(150)]
        public string? DepartemenPja { get; set; }

        [MaxLength(150)]
        public string? Pic { get; set; }

        [MaxLength(50)]
        public string? NikPic { get; set; }

        [MaxLength(150)]
        public string? DepartemenPic { get; set; }

        public string? RencanaPerbaikan { get; set; }

        public DateTime? TanggalRencanaPerbaikan { get; set; }

        public string? Perbaikan { get; set; }

        public DateTime? TanggalPerbaikan { get; set; }

        [MaxLength(50)]
        public string? Overdue { get; set; }

        public string? AlasanOverdue { get; set; }

        /// <summary>Nama PJA sebelum dialihkan (untuk riwayat)</summary>
        [MaxLength(300)]
        public string? ReassignedFrom { get; set; }

        /// <summary>Nama PJA tujuan pengalihan (untuk tampilan)</summary>
        [MaxLength(300)]
        public string? ReassignedTo { get; set; }

        public DateTime? ReassignedAt { get; set; }

        public int? PerusahaanId { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
