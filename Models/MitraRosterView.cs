using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class MitraRosterView
    {
        [Key]
        public int KaryawanId { get; set; }

        [Required]
        [MaxLength(50)]
        public string NoNik { get; set; } = string.Empty;

        public int? HariOnsite { get; set; }

        public int? HariOffsite { get; set; }
    }
}
