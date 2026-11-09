using System;
using System.Collections.Generic;
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

        [Column("is_all_karyawan")]
        public bool IsAllKaryawan { get; set; } = true;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [MaxLength(50)]
        [Column("updated_by_nik")]
        public string? UpdatedByNik { get; set; }

        [MaxLength(150)]
        [Column("updated_by_name")]
        public string? UpdatedByName { get; set; }

        public virtual List<AreaPjaRelation> PjaList { get; set; } = new();
    }
}
