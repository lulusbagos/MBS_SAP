using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_roster")]
    public class Roster
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [Required]
        public DateTime AwalDinas { get; set; }

        [Required]
        public DateTime AkhirDinas { get; set; }

        [Required]
        public DateTime AwalCuti { get; set; }

        [Required]
        public DateTime AkhirCuti { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; } = DateTime.Now;
    }
}
