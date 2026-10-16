using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_user_scan_permission")]
    public class UserScanPermission
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(80)]
        [Column("nik")]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(180)]
        [Column("nama")]
        public string? Nama { get; set; }

        [MaxLength(180)]
        [Column("departemen")]
        public string? Departemen { get; set; }

        [MaxLength(180)]
        [Column("perusahaan")]
        public string? Perusahaan { get; set; }

        [Column("can_scan_acara")]
        public bool CanScanAcara { get; set; } = true;

        [Column("can_scan_minepermit")]
        public bool CanScanMinePermit { get; set; } = false;

        [Column("can_scan_permit_pelanggaran")]
        public bool CanScanPermitPelanggaran { get; set; } = false;

        [Column("can_scan_permit_keluar_masuk")]
        public bool CanScanPermitKeluarMasuk { get; set; } = false;

        [Column("can_scan_simper")]
        public bool CanScanSimper { get; set; } = false;

        [Column("can_scan_commissioning")]
        public bool CanScanCommissioning { get; set; } = false;

        [Column("can_scan_special_permit")]
        public bool CanScanSpecialPermit { get; set; } = false;

        [Column("can_scan_other")]
        public bool CanScanOther { get; set; } = false;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [MaxLength(100)]
        [Column("updated_by")]
        public string? UpdatedBy { get; set; }
    }
}
