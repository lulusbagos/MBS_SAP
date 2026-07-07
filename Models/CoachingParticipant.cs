using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class CoachingParticipant
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CoachingId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Nama { get; set; } = string.Empty;

        [ForeignKey("CoachingId")]
        public Coaching? Coaching { get; set; }
    }
}
