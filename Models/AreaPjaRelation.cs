using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_r_area_pja")]
    public class AreaPjaRelation
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("area_id")]
        public int AreaId { get; set; }

        [Required]
        [MaxLength(150)]
        [Column("nama_area")]
        public string NamaArea { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        [Column("nik_pja")]
        public string NikPja { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        [Column("nama_pja")]
        public string NamaPja { get; set; } = string.Empty;

        [MaxLength(150)]
        [Column("jabatan_pja")]
        public string? JabatanPja { get; set; }

        [MaxLength(150)]
        [Column("departemen_pja")]
        public string? DepartemenPja { get; set; }

        [MaxLength(150)]
        [Column("perusahaan_pja")]
        public string? PerusahaanPja { get; set; }

        [Column("is_pic_utama")]
        public bool IsPicUtama { get; set; } = false;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [MaxLength(50)]
        [Column("created_by")]
        public string? CreatedBy { get; set; }

        [ForeignKey("AreaId")]
        public virtual MasterArea? Area { get; set; }
    }
}

