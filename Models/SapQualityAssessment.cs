using System;

namespace MBS_SAP.Models
{
    public class SapQualityAssessment
    {
        public int Id { get; set; }
        public string ProgramType { get; set; } = null!; // 'Hazard', 'Inspection', 'SafetyTalk', 'Observation', 'Coaching'
        public int ProgramId { get; set; }
        public int Rating { get; set; } // 1-5 stars
        public string? Notes { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }
}
