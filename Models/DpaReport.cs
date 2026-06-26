using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class DpaReport
    {
        [Key]
        public int Id { get; set; }

        // Assessor (person filling the form)
        [Required]
        [MaxLength(50)]
        public string AssessorNik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string AssessorNama { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? AssessorDepartemen { get; set; }

        // Driver being assessed
        [Required]
        [MaxLength(50)]
        public string DriverNik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string DriverNama { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? DriverDepartemen { get; set; }

        // Assessment context
        [Required]
        public DateTime TanggalPenilaian { get; set; } = DateTime.Today;

        [Required]
        [MaxLength(100)]
        public string JenisPerjalanan { get; set; } = string.Empty; // Dinas / Cuti Berangkat / Cuti Pulang

        [MaxLength(200)]
        public string? Rute { get; set; } // e.g. "Site - Banjarmasin"

        [MaxLength(100)]
        public string? NoLambung { get; set; } // Vehicle unit number

        // Assessment scores (JSON serialized arrays)
        public string? SafetyDrivingJson { get; set; }   // 5 items
        public string? DrivingSkillJson { get; set; }     // 3 items
        public string? BehaviorJson { get; set; }         // 3 items
        public string? ServiceQualityJson { get; set; }   // 4 items

        // Calculated scores
        public double ScorePenumpang { get; set; }  // User/Passenger score (0-100)
        public double ScoreGps { get; set; }         // GPS Tracking score (0-100), default 0 until integrated
        public double ScoreLenzguard { get; set; }   // LenzGuard AI score (0-100), default 0 until integrated
        public double ScoreFinal { get; set; }       // Weighted final score (0-100)

        [MaxLength(50)]
        public string? Kategori { get; set; } // Excellent / Good / Satisfactory / Need Improvement / Corrective Action

        [MaxLength(1000)]
        public string? Keterangan { get; set; } // Additional comments

        public int? PerusahaanId { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
