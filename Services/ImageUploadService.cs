using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MBS_SAP.Services
{
    public class ImageUploadService
    {
        private readonly string _basePath = @"C:\MinePermitFiles\MBS";

        public async Task<string?> UploadAndCompressImageAsync(IFormFile? file, string category, string prefix = "")
        {
            if (file == null || file.Length == 0) return null;

            var monthFolder = DateTime.Now.ToString("yyyy-MM");
            var uploadsFolder = Path.Combine(_basePath, category, monthFolder);
            
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var extension = Path.GetExtension(file.FileName).ToLower();
            bool isImage = extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".webp" || extension == ".bmp";

            var uniqueFileName = (string.IsNullOrEmpty(prefix) ? "" : prefix + "_") + Guid.NewGuid().ToString().Substring(0, 8);
            
            if (isImage)
            {
                uniqueFileName += ".jpg"; // Convert images to compressed jpg
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                try
                {
                    using (var stream = file.OpenReadStream())
                    {
                        using (var image = await Image.LoadAsync(stream))
                        {
                            // Resize if width > 1280 to save space
                            if (image.Width > 1280)
                            {
                                var ratio = 1280.0 / image.Width;
                                var newHeight = (int)(image.Height * ratio);
                                image.Mutate(x => x.Resize(1280, newHeight));
                            }

                            var encoder = new JpegEncoder { Quality = 75 };
                            await image.SaveAsync(filePath, encoder);
                        }
                    }
                }
                catch (Exception)
                {
                    // Fallback if ImageSharp fails to read
                    uniqueFileName = uniqueFileName.Replace(".jpg", extension);
                    var fallbackPath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(fallbackPath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }
                }
            }
            else
            {
                uniqueFileName += extension;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }
            }

            return $"/uploads/{category}/{monthFolder}/{uniqueFileName}";
        }
    }
}
