using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_t_gci_report")]
    public class GciReport
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
        public string? Pit { get; set; }

        [MaxLength(100)]
        public string? NamaLereng { get; set; }

        // Area Type based on Master Specification: Highwall, Lowwall, Sidewall, Bench/Jenjang, Disposal Slope, In-Pit Dump, Stockpile Embankment, Sump Slope
        [MaxLength(50)]
        public string? AreaType { get; set; } = "Highwall";

        [MaxLength(20)]
        public string? Shift { get; set; }

        [MaxLength(50)]
        public string? Latitude { get; set; }

        [MaxLength(50)]
        public string? Longitude { get; set; }

        // Parameter Penilaian Geotechnical Condition Index (GCI) - Bobot Total 100%
        // 1. Geometri & Kemiringan Lereng vs Desain (15%) - GEO-01 & GEO-02
        public int SkorGeometriLereng { get; set; } = 100;

        // 2. Kondisi Crest, Toe & Catch Berm (15%) - GEO-03 & GEO-04
        public int SkorKondisiCrestToe { get; set; } = 100;

        // 3. Bebas Retakan Tarik & Amblesan (15%) - GEO-05 & GEO-06
        public int SkorTensionCrack { get; set; } = 100;

        // 4. Kestabilan Batuan, Bulging & Rockfall (15%) - GEO-07 & GEO-08
        public int SkorKestabilanBatuan { get; set; } = 100;

        // 5. Kontrol Rembesan Air & Drainase (10%) - HYD-01 & HYD-02
        public int SkorRembesanDrainase { get; set; } = 100;

        // 6. Pemantauan Sensor & Tren Deformasi (10%) - MON-01 & MON-03
        public int SkorPemantauanSensor { get; set; } = 100;

        // 7. Demarkasi Exclusion Zone & Tanggap Darurat (10%) - CTL-01 & CTL-02
        public int SkorDemarkasiZona { get; set; } = 100;

        // 8. Kepatuhan Rekomendasi Geoteknik (10%) - CTL-03
        public int SkorKepatuhanRekomendasi { get; set; } = 100;

        public double TotalScore { get; set; } = 100;

        [MaxLength(50)]
        public string KategoriIndex { get; set; } = "Baik";

        // TARP Level: GREEN, YELLOW, ORANGE, RED
        [MaxLength(50)]
        public string TarpLevel { get; set; } = "GREEN";

        [MaxLength(2000)]
        public string? Catatan { get; set; }

        [MaxLength(2000)]
        public string? TindakanPerbaikan { get; set; }

        [MaxLength(100)]
        public string? Pic { get; set; }

        [MaxLength(50)]
        public string? PicNik { get; set; }

        [MaxLength(250)]
        public string? JudulTemuan { get; set; }

        public DateTime? DueDate { get; set; }

        [MaxLength(500)]
        public string? FotoUrl { get; set; }

        [MaxLength(500)]
        public string? FotoTemuanUrl { get; set; }

        [MaxLength(500)]
        public string? FotoRekomendasiUrl { get; set; }

        [MaxLength(500)]
        public string? PetaHazardUrl { get; set; }

        // Data Tambahan Spesifikasi Mockup Geoteknik (Image 1, 2, 3, 4, 5)
        [MaxLength(50)]
        public string? JenisInspeksi { get; set; } = "Inspeksi Rutin";

        [MaxLength(100)]
        public string? PjaNama { get; set; }

        [MaxLength(50)]
        public string? PjaNik { get; set; }

        public double? ElevasiRl { get; set; } = 245.0;
        public double? TinggiJenjang { get; set; } = 15.5;
        public double? LebarBerm { get; set; } = 8.0;
        public double? ActualFaceAngle { get; set; } = 68.0;
        public double? DesignFaceAngle { get; set; } = 70.0;
        public double? OverallSlopeAngle { get; set; } = 42.0;

        [MaxLength(50)]
        public string? KondisiDesain { get; set; } = "Sesuai";

        [MaxLength(50)]
        public string? KondisiRetakan { get; set; } = "Tidak Ada";

        [MaxLength(50)]
        public string? KondisiRockfall { get; set; } = "Tidak Ada";

        [MaxLength(50)]
        public string? KondisiBulging { get; set; } = "Tidak Ada";

        [MaxLength(50)]
        public string? KondisiRembesan { get; set; } = "Tidak Ada";

        public double? DebitRembesan { get; set; }

        [MaxLength(50)]
        public string? KondisiErosi { get; set; } = "Tidak Ada";

        [MaxLength(50)]
        public string? KondisiSettlement { get; set; } = "Tidak Ada";

        public double? LebarRetakanMm { get; set; }
        public double? PanjangRetakanM { get; set; }
        public double? KedalamanRetakanM { get; set; }

        [MaxLength(50)]
        public string? ArahRetakan { get; set; }

        [MaxLength(50)]
        public string? PertumbuhanRetakan { get; set; }

        [MaxLength(50)]
        public string? KecepatanRetakan { get; set; }

        [MaxLength(500)]
        public string? PetaMonitoringUrl { get; set; }

        [MaxLength(500)]
        public string? PetaDesainUrl { get; set; }

        [MaxLength(50)]
        public string? StatusWorkflow { get; set; } = "Menunggu Review PJA";

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}
