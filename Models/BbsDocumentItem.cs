using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_bbs_document_item")]
    public class BbsDocumentItem
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("document_id")]
        public int DocumentId { get; set; }

        [Column("section_name")]
        [Required]
        [MaxLength(200)]
        public string SectionName { get; set; } = string.Empty;

        [Column("item_no")]
        [MaxLength(50)]
        public string? ItemNo { get; set; }

        [Column("item_text")]
        [Required]
        [MaxLength(1000)]
        public string ItemText { get; set; } = string.Empty;

        [Column("item_type")]
        [MaxLength(50)]
        public string ItemType { get; set; } = "Perilaku"; // "Perilaku", "Kondisi", "Prasyarat", "Kepatuhan"

        [Column("classification")]
        [MaxLength(50)]
        public string? Classification { get; set; } = "At-Risk"; // "Safe", "At-Risk"

        [Column("risk_level")]
        [MaxLength(50)]
        public string? RiskLevel { get; set; } = "Sedang"; // "Rendah", "Sedang", "Tinggi", "Sangat Tinggi"

        [Column("sort_order")]
        public int SortOrder { get; set; } = 0;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey("DocumentId")]
        public virtual BbsDocument? Document { get; set; }
    }
}
