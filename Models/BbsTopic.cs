using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_bbs_topic")]
    public class BbsTopic
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("category_id")]
        public int CategoryId { get; set; }

        [Column("name")]
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Column("icon")]
        [MaxLength(50)]
        public string? Icon { get; set; } = "bi-tag";

        [Column("description")]
        [MaxLength(255)]
        public string? Description { get; set; }

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

        public virtual ICollection<BbsBehavior> Behaviors { get; set; } = new List<BbsBehavior>();
    }
}
