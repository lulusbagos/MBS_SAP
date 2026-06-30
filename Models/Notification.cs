using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_t_notifications")]
    public class Notification
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("recipient_nik")]
        public string RecipientNik { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [MaxLength(500)]
        [Column("url")]
        public string? Url { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; } = false;

        /// <summary>Jenis notifikasi: hazard_new, hazard_reassign, inspection_new, inspection_reassign,
        /// actionplan_new, actionplan_reassign, timeline_like, timeline_comment, general</summary>
        [MaxLength(50)]
        [Column("notif_type")]
        public string? NotifType { get; set; } = "general";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
