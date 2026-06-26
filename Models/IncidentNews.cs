using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class IncidentNews
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(300)]
        public string Judul { get; set; } = string.Empty;

        [Required]
        public string Konten { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? GambarUrl { get; set; }

        [MaxLength(150)]
        public string? Lokasi { get; set; }

        public DateTime? TanggalKejadian { get; set; }

        [MaxLength(100)]
        public string? Kategori { get; set; } // Ringan, Sedang, Berat, Fatal

        [Required]
        [MaxLength(150)]
        public string DibuatOleh { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string NikPembuat { get; set; } = string.Empty;

        public bool IsPublished { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}
