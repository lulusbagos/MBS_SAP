using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_area_utama")]
    public class MasterArea
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        [Column("nama_area")]
        public string NamaArea { get; set; } = string.Empty;

        [Required]
        [Column("perusahaan_id")]
        public int PerusahaanId { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("created_by_nik")]
        public string CreatedByNik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        [Column("created_by_name")]
        public string CreatedByName { get; set; } = string.Empty;

        [Required]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
