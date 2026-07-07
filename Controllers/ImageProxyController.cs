using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class ImageProxyController : Controller
    {
        [HttpGet]
        public async Task<IActionResult> Get(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return BadRequest("URL is required");
            }

            // Security check: only allow proxying from the specific image server
            if (!url.StartsWith("https://apiis.idcapps.net/", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://apiis.idcapps.net/", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://172.16.1.96/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Invalid image source URL");
            }

            try
            {
                // Bypass SSL certificate check in case internal server certificate is self-signed/invalid
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return NotFound();
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var stream = await response.Content.ReadAsStreamAsync();
                
                return File(stream, contentType);
            }
            catch (Exception)
            {
                return NotFound();
            }
        }
    }
}
