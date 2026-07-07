using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class Coaching
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(500)]
        public string? Foto { get; set; }

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
        public string? Tema { get; set; }

        public string? Feedback { get; set; }

        public string? Komitmen { get; set; }

        public int? PerusahaanId { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<CoachingParticipant> Participants { get; set; } = new List<CoachingParticipant>();
    }
}
