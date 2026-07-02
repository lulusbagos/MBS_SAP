using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using MBS_SAP.Data;
using MBS_SAP.Services;
using Microsoft.Extensions.FileProviders;
using System.IO;var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<MBS_SAP.Services.ExcelService>();
builder.Services.AddScoped<MBS_SAP.Services.CompanyHierarchyService>();
builder.Services.AddScoped<MBS_SAP.Services.ImageUploadService>();
builder.Services.Configure<PostgresReplicationOptions>(builder.Configuration.GetSection("PostgresReplication"));
builder.Services.AddScoped<PostgresReplicationService>();
builder.Services.AddHostedService<PostgresReplicationScheduler>();
builder.Services.AddHttpClient();

// Register DbContext with SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

var externalFilesPath = @"C:\MinePermitFiles\MBS";
if (!Directory.Exists(externalFilesPath))
{
    Directory.CreateDirectory(externalFilesPath);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(externalFilesPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();
