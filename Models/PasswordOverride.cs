using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class PasswordOverride
    {
        [Key]
        [Required]
        [MaxLength(50)]
        public string Nrp { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string KataSandi { get; set; } = string.Empty;

        public DateTime DiubahPada { get; set; } = DateTime.Now;

        [MaxLength(1000)]
        public string? ProfilePicture { get; set; }

        public bool HasAgreedToTerms { get; set; } = false;
    }
}
