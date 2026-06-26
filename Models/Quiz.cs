using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class Quiz
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Nama { get; set; } = string.Empty;

        public int Score { get; set; }

        [MaxLength(50)]
        public string? Platform { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<QuizAnswer> Answers { get; set; } = new List<QuizAnswer>();
    }
}
