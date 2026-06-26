using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using MBS_SAP.Data;
using MBS_SAP.Models;

namespace MBS_SAP.Controllers
{
    [ApiController]
    [Route("Api/Quiz")]
    public class QuizSyncController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<QuizSyncController> _logger;

        public QuizSyncController(AppDbContext context, IHttpClientFactory httpClientFactory, ILogger<QuizSyncController> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet("Sync")]
        public async Task<IActionResult> SyncQuizzes([FromQuery] string date, [FromQuery] int limit = 10)
        {
            if (string.IsNullOrEmpty(date))
            {
                date = DateTime.Today.ToString("yyyy-MM-dd");
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync($"https://savera_admin.ungguldinamika.com/api/p5m/results?date={date}&limit={limit}");

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, "Failed to fetch from external API");
                }

                var content = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<SaveraApiResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (apiResponse?.Data != null && apiResponse.Data.Count > 0)
                {
                    foreach (var item in apiResponse.Data)
                    {
                        DateTime parsedDate = DateTime.Now;
                        if (!string.IsNullOrEmpty(item.CreatedAt) && DateTime.TryParse(item.CreatedAt, out var dt))
                        {
                            parsedDate = dt;
                        }

                        // Check if exists
                        var exists = await _context.Quizzes
                            .AnyAsync(q => q.Nik == item.UserNik && q.CreatedAt == parsedDate);

                        if (!exists)
                        {
                            var quiz = new Quiz
                            {
                                Nik = item.UserNik ?? string.Empty,
                                Nama = item.UserName ?? string.Empty,
                                Score = item.Score,
                                Platform = item.Platform,
                                CreatedAt = parsedDate
                            };

                            if (item.Answers != null)
                            {
                                foreach (var ans in item.Answers)
                                {
                                    quiz.Answers.Add(new QuizAnswer
                                    {
                                        ItemId = ans.ItemId,
                                        Question = ans.Question ?? string.Empty,
                                        CorrectKey = ans.CorrectKey,
                                        CorrectAnswerText = ans.CorrectAnswerText,
                                        SelectedAnswer = ans.SelectedAnswer,
                                        SelectedAnswerText = ans.SelectedAnswerText,
                                        PointsEarned = ans.PointsEarned
                                    });
                                }
                            }

                            _context.Quizzes.Add(quiz);
                        }
                    }

                    await _context.SaveChangesAsync();
                }

                // Return exactly what we received so frontend rendering doesn't need to change
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing quizzes");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SaveraApiResponse
    {
        [JsonPropertyName("data")]
        public List<SaveraApiQuizData>? Data { get; set; }
    }

    public class SaveraApiQuizData
    {
        [JsonPropertyName("user_nik")]
        public string? UserNik { get; set; }

        [JsonPropertyName("user_name")]
        public string? UserName { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("answers")]
        public List<SaveraApiQuizAnswer>? Answers { get; set; }
    }

    public class SaveraApiQuizAnswer
    {
        [JsonPropertyName("item_id")]
        public int ItemId { get; set; }

        [JsonPropertyName("question")]
        public string? Question { get; set; }

        [JsonPropertyName("correct_key")]
        public string? CorrectKey { get; set; }

        [JsonPropertyName("correct_answer_text")]
        public string? CorrectAnswerText { get; set; }

        [JsonPropertyName("selected_answer")]
        public string? SelectedAnswer { get; set; }

        [JsonPropertyName("selected_answer_text")]
        public string? SelectedAnswerText { get; set; }

        [JsonPropertyName("points_earned")]
        public int PointsEarned { get; set; }
    }
}
