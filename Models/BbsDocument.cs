using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_bbs_document")]
    public class BbsDocument
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("doc_code")]
        [Required]
        [MaxLength(100)]
        public string DocCode { get; set; } = string.Empty;

        [Column("title")]
        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [Column("category_id")]
        public int? CategoryId { get; set; }

        [Column("category_name")]
        [MaxLength(150)]
        public string? CategoryName { get; set; }

        [Column("topic_name")]
        [MaxLength(150)]
        public string? TopicName { get; set; }

        [Column("revision")]
        [MaxLength(50)]
        public string? Revision { get; set; } = "00";

        [Column("effective_date")]
        [MaxLength(100)]
        public string? EffectiveDate { get; set; }

        [Column("department")]
        [MaxLength(150)]
        public string? Department { get; set; }

        [Column("scope")]
        [MaxLength(255)]
        public string? Scope { get; set; }

        [Column("description")]
        [MaxLength(1000)]
        public string? Description { get; set; }

        [Column("icon")]
        [MaxLength(50)]
        public string? Icon { get; set; } = "bi-file-earmark-text-fill";

        [Column("color")]
        [MaxLength(50)]
        public string? Color { get; set; } = "#0284c7";

        [Column("file_name")]
        [MaxLength(255)]
        public string? FileName { get; set; }

        [Column("file_url")]
        [MaxLength(500)]
        public string? FileUrl { get; set; }

        [Column("total_items")]
        public int TotalItems { get; set; } = 0;

        [Column("behavior_count")]
        public int BehaviorCount { get; set; } = 0;

        [Column("condition_count")]
        public int ConditionCount { get; set; } = 0;

        [Column("sort_order")]
        public int SortOrder { get; set; } = 0;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        [ForeignKey("CategoryId")]
        public virtual BbsCategory? Category { get; set; }

        public virtual ICollection<BbsDocumentItem> Items { get; set; } = new List<BbsDocumentItem>();
    }
}
