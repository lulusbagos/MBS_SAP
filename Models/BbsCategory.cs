using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_bbs_category")]
    public class BbsCategory
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Column("category_type")]
        [MaxLength(50)]
        public string CategoryType { get; set; } = "Both"; // "Rutin", "SpecialCase", "Both"

        [Column("icon")]
        [MaxLength(50)]
        public string? Icon { get; set; } = "bi-clipboard2-check";

        [Column("color")]
        [MaxLength(50)]
        public string? Color { get; set; } = "#0284c7";

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

        // Navigation properties
        public virtual ICollection<BbsTopic> Topics { get; set; } = new List<BbsTopic>();
        public virtual ICollection<BbsBehavior> Behaviors { get; set; } = new List<BbsBehavior>();
    }
}
