using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using MBS_SAP.Data;
using MBS_SAP.Services;
using Microsoft.Extensions.FileProviders;
using System.IO;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Persist Data Protection Keys to prevent cookie invalidation when app restarts or is re-published
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys");
if (!Directory.Exists(keysFolder))
{
    Directory.CreateDirectory(keysFolder);
}
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("MBS_SAP");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<MBS_SAP.Services.CompanyHierarchyService>();
builder.Services.AddScoped<MBS_SAP.Services.ImageUploadService>();
builder.Services.Configure<PostgresReplicationOptions>(builder.Configuration.GetSection("PostgresReplication"));
builder.Services.AddScoped<PostgresReplicationService>();
builder.Services.AddHostedService<PostgresReplicationScheduler>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// Forwarded headers for reverse proxy / IIS domain hosting
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
});

// Register DbContext with SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add Cookie Authentication with Real-time Session Invalidation
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".MBS_SAP.Auth";
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                try
                {
                    var nrp = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var passwordClaim = context.Principal?.FindFirst("PasswordHash")?.Value;
                    
                    if (!string.IsNullOrEmpty(nrp))
                    {
                        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                        var cacheKey = $"UserAuthActive_{nrp}";
                        
                        if (!cache.TryGetValue(cacheKey, out string? currentPassword))
                        {
                            var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                            
                            // Check if employee or user exists and active
                            var karyawan = await dbContext.Karyawans.FirstOrDefaultAsync(k => k.NoNik == nrp && k.StatusAktif);
                            var pengguna = await dbContext.Penggunas.FirstOrDefaultAsync(p => p.Username == nrp && p.IsAktif);

                            if (karyawan == null && pengguna == null)
                            {
                                context.RejectPrincipal();
                                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                                return;
                            }
                            
                            // Check if password has changed since cookie was issued
                            var overridePwd = await dbContext.PasswordOverrides.FirstOrDefaultAsync(p => p.Nrp == nrp);
                            currentPassword = overridePwd?.KataSandi;
                            if (string.IsNullOrEmpty(currentPassword))
                            {
                                currentPassword = pengguna?.KataSandi ?? "123456";
                            }
                            
                            cache.Set(cacheKey, currentPassword, TimeSpan.FromMinutes(5));
                        }
                        
                        if (!string.IsNullOrEmpty(passwordClaim) && !string.IsNullOrEmpty(currentPassword) && passwordClaim != currentPassword)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Catch DB hiccups so logged in users are not forcefully signed out on domain
                    Console.WriteLine($"[Auth] OnValidatePrincipal warning: {ex.Message}");
                }
            }
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseForwardedHeaders();
app.UseStaticFiles();

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MBS_SAP.Data.AppDbContext>();
    try {
        dbContext.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[tbl_t_attendance_record]') AND name = N'latitude')
            BEGIN
                ALTER TABLE tbl_t_attendance_record ADD Latitude float NULL;
            END
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[tbl_t_attendance_record]') AND name = N'longitude')
            BEGIN
                ALTER TABLE tbl_t_attendance_record ADD Longitude float NULL;
            END
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[tbl_t_incident_news]') AND name = N'perusahaan_id')
            BEGIN
                ALTER TABLE tbl_t_incident_news ADD perusahaan_id int NULL;
            END

            IF OBJECT_ID(N'[dbo].[tbl_t_coaching]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[tbl_t_coaching] (
                    [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [foto] NVARCHAR(500) NULL,
                    [tanggal] DATETIME NOT NULL,
                    [waktu] TIME NOT NULL,
                    [nama] NVARCHAR(150) NOT NULL,
                    [nik] NVARCHAR(50) NOT NULL,
                    [departemen] NVARCHAR(150) NULL,
                    [area] NVARCHAR(150) NULL,
                    [lokasi] NVARCHAR(150) NULL,
                    [detil_lokasi] NVARCHAR(250) NULL,
                    [tema] NVARCHAR(100) NULL,
                    [feedback] NVARCHAR(MAX) NULL,
                    [komitmen] NVARCHAR(MAX) NULL,
                    [perusahaan_id] INT NULL,
                    [is_deleted] BIT NOT NULL DEFAULT 0,
                    [created_at] DATETIME NOT NULL DEFAULT GETDATE()
                );
            END

            IF OBJECT_ID(N'[dbo].[tbl_t_coaching_participant]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[tbl_t_coaching_participant] (
                    [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [coaching_id] INT NOT NULL,
                    [nik] NVARCHAR(50) NOT NULL,
                    [nama] NVARCHAR(150) NOT NULL,
                    CONSTRAINT [FK_tbl_t_coaching_participant_tbl_t_coaching] FOREIGN KEY ([coaching_id]) REFERENCES [dbo].[tbl_t_coaching] ([id]) ON DELETE CASCADE
                );
            END

            IF OBJECT_ID(N'[dbo].[tbl_m_roster]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[tbl_m_roster] (
                    [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [nik] NVARCHAR(50) NOT NULL,
                    [awal_dinas] DATE NOT NULL,
                    [akhir_dinas] DATE NOT NULL,
                    [awal_cuti] DATE NOT NULL,
                    [akhir_cuti] DATE NOT NULL,
                    [created_at] DATETIME NOT NULL DEFAULT GETDATE(),
                    [updated_at] DATETIME NULL
                );
            END

            IF OBJECT_ID(N'[dbo].[tbl_m_penilaian_kualitas_sap]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[tbl_m_penilaian_kualitas_sap] (
                    [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [program_type] VARCHAR(50) NOT NULL,
                    [program_id] INT NOT NULL,
                    [rating] INT NOT NULL,
                    [notes] NVARCHAR(1000) NULL,
                    [created_by] NVARCHAR(150) NOT NULL,
                    [created_at] DATETIME NOT NULL DEFAULT GETDATE()
                );
                CREATE NONCLUSTERED INDEX [IX_tbl_m_penilaian_kualitas_sap_program] 
                ON [dbo].[tbl_m_penilaian_kualitas_sap] ([program_type], [program_id]);
            END

            -- Add non-clustered composite indexes to prevent execution timeouts on safety calculations
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_hazard_report_nik_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_hazard_report'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_hazard_report_nik_is_deleted_created_at
                ON tbl_t_hazard_report (nik, is_deleted, created_at DESC)
                INCLUDE (tanggal, status_temuan, lokasi, area, temuan, nama);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_hazard_report_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_hazard_report'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_hazard_report_is_deleted_created_at
                ON tbl_t_hazard_report (is_deleted, created_at DESC);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_hazard_report_is_deleted_tanggal' AND object_id = OBJECT_ID('tbl_t_hazard_report'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_hazard_report_is_deleted_tanggal
                ON tbl_t_hazard_report (is_deleted, tanggal)
                INCLUDE (nik, temuan, lokasi, perusahaan_id, status_temuan, created_at);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_hazard_report_perusahaan_deleted_created' AND object_id = OBJECT_ID('tbl_t_hazard_report'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_hazard_report_perusahaan_deleted_created
                ON tbl_t_hazard_report (perusahaan_id, is_deleted, created_at DESC)
                INCLUDE (nik);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_hazard_report_nik_pja_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_hazard_report'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_hazard_report_nik_pja_is_deleted_created_at
                ON tbl_t_hazard_report (nik_pja, is_deleted, created_at DESC)
                INCLUDE (tanggal, status_temuan, lokasi, area, temuan, nama);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_hazard_report_is_deleted_area' AND object_id = OBJECT_ID('tbl_t_hazard_report'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_hazard_report_is_deleted_area
                ON tbl_t_hazard_report (is_deleted, area);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_inspection_nik_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_inspection'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_inspection_nik_is_deleted_created_at
                ON tbl_t_inspection (nik, is_deleted, created_at DESC)
                INCLUDE (tanggal, jenis_inspeksi, area, nama);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_inspection_nik_pja_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_inspection'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_inspection_nik_pja_is_deleted_created_at
                ON tbl_t_inspection (nik_pja, is_deleted, created_at DESC)
                INCLUDE (tanggal, jenis_inspeksi, area, nama);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_inspection_is_deleted_tanggal' AND object_id = OBJECT_ID('tbl_t_inspection'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_inspection_is_deleted_tanggal
                ON tbl_t_inspection (is_deleted, tanggal)
                INCLUDE (nik, jenis_inspeksi, lokasi, perusahaan_id, pja, nik_pja, departemen_pja, catatan, created_at);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_safety_talk_nik_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_safety_talk'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_safety_talk_nik_is_deleted_created_at
                ON tbl_t_safety_talk (nik, is_deleted, created_at);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_p5m_nik_is_deleted_created_at' AND object_id = OBJECT_ID('tbl_t_p5m'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_p5m_nik_is_deleted_created_at
                ON tbl_t_p5m (nik, is_deleted, created_at);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_m_roster_nik' AND object_id = OBJECT_ID('tbl_m_roster'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_m_roster_nik
                ON tbl_m_roster (nik);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_observation_nik' AND object_id = OBJECT_ID('tbl_t_observation'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_observation_nik ON tbl_t_observation (nik) INCLUDE (is_deleted, created_at);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_coaching_nik' AND object_id = OBJECT_ID('tbl_t_coaching'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_coaching_nik ON tbl_t_coaching (nik) INCLUDE (is_deleted, created_at);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_coaching_participant_nik' AND object_id = OBJECT_ID('tbl_t_coaching_participant'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_coaching_participant_nik ON tbl_t_coaching_participant (nik);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_action_plan_nik' AND object_id = OBJECT_ID('tbl_t_action_plan'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_action_plan_nik ON tbl_t_action_plan (nik) INCLUDE (is_deleted);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_action_plan_nik_pja' AND object_id = OBJECT_ID('tbl_t_action_plan'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_action_plan_nik_pja ON tbl_t_action_plan (nik_pja) INCLUDE (is_deleted);
            END

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_tbl_t_action_plan_nik_pic' AND object_id = OBJECT_ID('tbl_t_action_plan'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_tbl_t_action_plan_nik_pic ON tbl_t_action_plan (nik_pic) INCLUDE (is_deleted);
            END
        ");
     } catch {
        // Ignored, columns might already exist
    }
}

app.Run();
