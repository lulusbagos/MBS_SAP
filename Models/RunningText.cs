using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_running_text")]
    public class RunningText
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("pesan")]
        [Required]
        public string Pesan { get; set; } = string.Empty;

        [Column("is_aktif")]
        public bool IsAktif { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
