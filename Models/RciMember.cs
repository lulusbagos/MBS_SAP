using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_t_rci_member")]
    public class RciMember
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("rci_report_id")]
        public int RciReportId { get; set; }

        [Required]
        [MaxLength(30)]
        [Column("role")] // "Creator", "Approver"
        public string Role { get; set; } = "Creator";

        [Required]
        [MaxLength(50)]
        [Column("nik")]
        public string Nik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        [Column("nama")]
        public string Nama { get; set; } = string.Empty;

        [MaxLength(150)]
        [Column("jabatan")]
        public string? Jabatan { get; set; }

        [MaxLength(150)]
        [Column("perusahaan")]
        public string? Perusahaan { get; set; }

        [Column("perusahaan_id")]
        public int? PerusahaanId { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("status")] // "Pending", "Approved", "Confirmed"
        public string Status { get; set; } = "Pending";

        [MaxLength(500)]
        [Column("catatan")]
        public string? Catatan { get; set; }

        [Column("action_at")]
        public DateTime? ActionAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("RciReportId")]
        public virtual RciReport? RciReport { get; set; }
    }
}
