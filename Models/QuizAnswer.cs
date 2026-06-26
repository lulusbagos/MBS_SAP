using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class QuizAnswer
    {
        [Key]
        public int Id { get; set; }

        public int QuizId { get; set; }

        [ForeignKey("QuizId")]
        public Quiz Quiz { get; set; } = null!;

        public int ItemId { get; set; }

        public string Question { get; set; } = string.Empty;

        [MaxLength(10)]
        public string? CorrectKey { get; set; }

        public string? CorrectAnswerText { get; set; }

        [MaxLength(10)]
        public string? SelectedAnswer { get; set; }

        public string? SelectedAnswerText { get; set; }

        public int PointsEarned { get; set; }
    }
}
