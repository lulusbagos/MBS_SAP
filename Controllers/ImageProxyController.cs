using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class ImageProxyController : Controller
    {
        private static readonly string CacheDirectory = @"C:\MinePermitFiles\MBS\ImageProxyCache";

        public ImageProxyController()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                try
                {
                    Directory.CreateDirectory(CacheDirectory);
                }
                catch { }
            }
        }

        private string GetCacheFilePath(string url)
        {
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
                var hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                return Path.Combine(CacheDirectory, hashStr + ".dat");
            }
        }

        [HttpGet]
        [ResponseCache(Duration = 604800, Location = ResponseCacheLocation.Any, NoStore = false)]
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
                var cachePath = GetCacheFilePath(url);
                if (System.IO.File.Exists(cachePath))
                {
                    return PhysicalFile(cachePath, "image/jpeg");
                }

                // Bypass SSL certificate check in case internal server certificate is self-signed/invalid
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(15);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return NotFound();
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                
                try
                {
                    await System.IO.File.WriteAllBytesAsync(cachePath, imageBytes);
                }
                catch
                {
                    // Ignore cache write errors
                }
                
                return File(imageBytes, contentType);
            }
            catch (Exception)
            {
                return NotFound();
            }
        }
    }
}
