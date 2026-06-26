using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class TimelineComment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ItemType { get; set; } = string.Empty;

        [Required]
        public int ItemId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string CommentText { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Nik { get; set; } 

        [MaxLength(150)]
        public string? NamaPengguna { get; set; } // Supaya tau siapa yang komen (Guest/Name)

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
