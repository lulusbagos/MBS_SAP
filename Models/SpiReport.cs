using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_t_spi_report")]
    public class SpiReport
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
        public string? NamaKolam { get; set; }

        [MaxLength(100)]
        public string? TitikPenaatan { get; set; }

        [MaxLength(20)]
        public string? Shift { get; set; }

        [MaxLength(50)]
        public string? Latitude { get; set; }

        [MaxLength(50)]
        public string? Longitude { get; set; }

        // Parameter Penilaian Sediment Pond Index (Skor 0 - 100)
        // 1. Kapasitas Tampung & Endapan Lumpur (15%)
        public int SkorKapasitasEndapan { get; set; } = 100;
        // 2. Tanggul & Kestabilan Dike Embankment (15%)
        public int SkorKondisiTanggul { get; set; } = 100;
        // 3. Sekat Baffle & Kompartemen Retensi (15%)
        public int SkorSekatBaffle { get; set; } = 100;
        // 4. Saluran Pelimpah & Spillway Structure (15%)
        public int SkorPelimpahSpillway { get; set; } = 100;
        // 5. Fasilitas Dosing & Koagulan / Flokulan (10%)
        public int SkorFasilitasDosing { get; set; } = 100;
        // 6. Kualitas Air Fisik & TSS Visual (10%)
        public int SkorKualitasAirFisik { get; set; } = 100;
        // 7. Titik Pantau & Alat Ukur Debit (10%)
        public int SkorTitikPenaatanDebit { get; set; } = 100;
        // 8. Rambu Keselamatan & Pagar / Pelampung (10%)
        public int SkorRambuPengaman { get; set; } = 100;

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
