using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class AttendanceEvent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(160)]
        public string EventName { get; set; } = string.Empty;

        [MaxLength(220)]
        public string? EventLocation { get; set; }

        [MaxLength(1200)]
        public string? EventDescription { get; set; }

        public DateTime StartAt { get; set; }

        public DateTime EndAt { get; set; }

        [Required]
        [MaxLength(80)]
        public string QrToken { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    }
}
