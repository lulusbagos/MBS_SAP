using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_bbs_behavior")]
    public class BbsBehavior
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("category_id")]
        public int CategoryId { get; set; }

        [Column("topic_id")]
        public int? TopicId { get; set; }

        [Column("behavior_text")]
        [Required]
        [MaxLength(300)]
        public string BehaviorText { get; set; } = string.Empty;

        [Column("default_classification")]
        [MaxLength(50)]
        public string DefaultClassification { get; set; } = "At-Risk"; // "Safe", "At-Risk", "Both"

        [Column("default_risk_level")]
        [MaxLength(50)]
        public string? DefaultRiskLevel { get; set; } = "Sedang"; // "Rendah", "Sedang", "Tinggi", "Sangat Tinggi"

        [Column("guidance_note")]
        [MaxLength(500)]
        public string? GuidanceNote { get; set; }

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

        [ForeignKey("TopicId")]
        public virtual BbsTopic? Topic { get; set; }
    }
}
