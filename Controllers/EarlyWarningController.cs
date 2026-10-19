using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [AllowAnonymous]
    [Route("EarlyWarning")]
    public class EarlyWarningController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private const string CacheKey = "IMORA_FORECAST_CACHE_V1";
        private static string? _lastKnownPayload = null;
        private static DateTime? _lastFetchTime = null;

        public EarlyWarningController(IHttpClientFactory httpClientFactory, IMemoryCache cache)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
        }

        [HttpGet("")]
        [HttpGet("/Display4")]
        [HttpGet("Display4")]
        [HttpGet("/Imora")]
        [HttpGet("Imora")]
        public IActionResult Index()
        {
            ViewData["HideHeader"] = true;
            ViewData["HideNav"] = true;
            return View();
        }

        [HttpGet("GetForecast")]
        public async Task<IActionResult> GetForecast()
        {
            if (_cache.TryGetValue(CacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                return Content(cachedJson, "application/json");
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "MBS-SAP-Display4/1.0");

                var response = await client.GetAsync("https://imora.indexim.id/api/v1/forecast");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    
                    // Cache for 5 minutes per IMORA integration guide specification
                    _cache.Set(CacheKey, json, TimeSpan.FromMinutes(5));
                    _lastKnownPayload = json;
                    _lastFetchTime = DateTime.UtcNow;

                    return Content(json, "application/json");
                }
            }
            catch
            {
                // Fallback to last known payload if external network temporarily hiccups
                if (!string.IsNullOrEmpty(_lastKnownPayload))
                {
                    return Content(_lastKnownPayload, "application/json");
                }
            }

            // If completely unavailable and no cache, return error structure
            return StatusCode(503, new
            {
                provider = "IMORA",
                error = "Forecast data is temporarily unavailable. Retrying...",
                last_updated = _lastFetchTime
            });
        }
    }
}
