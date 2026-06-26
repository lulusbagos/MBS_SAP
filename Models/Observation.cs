using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class Observation
    {
        [Key]
        public int Id { get; set; }

        public DateTime Date { get; set; } = DateTime.Now;

        [MaxLength(150)]
        public string Nama { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Departemen { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Area { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Lokasi { get; set; } = string.Empty;

        public string? DetilLokasi { get; set; }

        public string? KegiatanYangDiamati { get; set; }

        [MaxLength(100)]
        public string? DepartemenYangDiamati { get; set; }

        [MaxLength(100)]
        public string? DokumenPendukung { get; set; } // IK, Prosedur, JSA

        [MaxLength(100)]
        public string? ResikoKritis { get; set; } // Pengoperasian Peralatan, LOTO, Traffic

        [MaxLength(50)]
        public string? TingkatResiko { get; set; } // Rendah, Sedang, Tinggi, Ekstrim

        [MaxLength(150)]
        public string? PerihalYangDiamati { get; set; } // Peralatan, Prosedur Kerja

        [MaxLength(50)]
        public string? HasilObservasi { get; set; } // Positive, Negative, Improvement, Violation

        [MaxLength(2000)]
        public string? Keterangan { get; set; }

        [MaxLength(500)]
        public string? FotoUrl { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
