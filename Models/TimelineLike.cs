using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class TimelineLike
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ItemType { get; set; } = string.Empty; // P5m, Hazard, dsb

        [Required]
        public int ItemId { get; set; }

        [MaxLength(50)]
        public string? Nik { get; set; } // Kosong jika Guest

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
