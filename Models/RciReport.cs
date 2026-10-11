using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_t_rci_report")]
    public class RciReport
    {
        [Key]
        public int Id { get; set; }

        public DateTime Tanggal { get; set; } = DateTime.Today;

        public TimeSpan Waktu { get; set; } = DateTime.Now.TimeOfDay;

        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Nama { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Departemen { get; set; }

        [MaxLength(200)]
        public string? Perusahaan { get; set; }

        public int? PerusahaanId { get; set; }

        [MaxLength(100)]
        public string Area { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Lokasi { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? DetilLokasi { get; set; }

        [MaxLength(100)]
        public string? NamaJalan { get; set; }

        [MaxLength(100)]
        public string? SegmentJalan { get; set; }

        [MaxLength(20)]
        public string? Shift { get; set; }

        [MaxLength(50)]
        public string? Latitude { get; set; }

        [MaxLength(50)]
        public string? Longitude { get; set; }

        // Standard Parameter RCI Baru (Skala 1 - 4 & Segmen Jalan)
        public double? PanjangSegment { get; set; } = 100;
        public double? DefisitSurfacing { get; set; } = 0;
        public double? DefisitUndulation { get; set; } = 0;
        public double? DefisitSpoil { get; set; } = 0;
        public double? DefisitSafetyBerm { get; set; } = 0;
        public double? DefisitCrossfall { get; set; } = 0;
        public double? DefisitDust { get; set; } = 0;
        public double? DefisitDrainage { get; set; } = 0;
        public double? DefisitRoadAttachment { get; set; } = 0;

        public double SkorSurfacing { get; set; } = 4.0;
        public double SkorUndulation { get; set; } = 4.0;
        public double SkorSpoil { get; set; } = 4.0;
        public double SkorSafetyBerm { get; set; } = 4.0;
        public double SkorCrossfall { get; set; } = 4.0;
        public double SkorDust { get; set; } = 4.0;
        public double SkorDrainage { get; set; } = 4.0;
        public double SkorRoadAttachment { get; set; } = 4.0;

        // Group Scores (Permukaan 60%, Safety Berm 20%, Drainage 20%)
        public double SkorPermukaanGroup { get; set; } = 4.0;
        public double SkorSafetyBermGroup { get; set; } = 4.0;
        public double SkorDrainageGroup { get; set; } = 4.0;

        public double ActualRoadScore { get; set; } = 4.0;
        public double TargetScore { get; set; } = 4.0;
        public double Achievement { get; set; } = 100.0;

        // Legacy / Backward-compatibility fields
        public int SkorLebarJalan { get; set; } = 100;
        public int SkorGradeJalan { get; set; } = 100;
        public int SkorPermukaanJalan { get; set; } = 100;
        public int SkorDrainaseParit { get; set; } = 100;
        public int SkorSuperelevasiTikungan { get; set; } = 100;
        public int SkorBebasSpillage { get; set; } = 100;
        public int SkorRambuDebu { get; set; } = 100;

        public double TotalScore { get; set; } = 100;

        [MaxLength(50)]
        public string KategoriIndex { get; set; } = "Baik";

        [MaxLength(2000)]
        public string? Catatan { get; set; }

        [MaxLength(2000)]
        public string? TindakanPerbaikan { get; set; }

        [MaxLength(100)]
        public string? Pic { get; set; }

        [MaxLength(500)]
        public string? FotoUrl { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}
