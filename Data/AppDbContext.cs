using Microsoft.EntityFrameworkCore;
using MBS_SAP.Models;

namespace MBS_SAP.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<PasswordOverride> PasswordOverrides { get; set; } = null!;
        public DbSet<HazardReport> HazardReports { get; set; } = null!;
        public DbSet<Inspection> Inspections { get; set; } = null!;
        public DbSet<ActionPlan> ActionPlans { get; set; } = null!;
        public DbSet<SafetyTalk> SafetyTalks { get; set; } = null!;
        public DbSet<P5m> P5ms { get; set; } = null!;
        public DbSet<TimelineLike> TimelineLikes { get; set; } = null!;
        public DbSet<TimelineComment> TimelineComments { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<RunningText> RunningTexts { get; set; } = null!;
        public DbSet<AppUser> AppUsers { get; set; } = null!;
        public DbSet<MasterArea> MasterAreas { get; set; } = null!;
        public DbSet<Quiz> Quizzes { get; set; } = null!;
        public DbSet<QuizAnswer> QuizAnswers { get; set; } = null!;
        public DbSet<Observation> Observations { get; set; } = null!;
        public DbSet<P2hVehicle> P2hVehicles { get; set; } = null!;
        public DbSet<P2hReport> P2hReports { get; set; } = null!;
        public DbSet<DpaReport> DpaReports { get; set; } = null!;
        public DbSet<DpaDriver> DpaDrivers { get; set; } = null!;
        public DbSet<IncidentNews> IncidentNewsList { get; set; } = null!;
        public DbSet<AttendanceEvent> AttendanceEvents { get; set; } = null!;
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; } = null!;

        // View entities
        public DbSet<KaryawanView> Karyawans { get; set; } = null!;
        public DbSet<PersonalView> Personals { get; set; } = null!;
        public DbSet<PenggunaView> Penggunas { get; set; } = null!;
        public DbSet<PerusahaanView> Perusahaans { get; set; } = null!;
        public DbSet<PerusahaanHierarchyRelationView> PerusahaanHierarchyRelations { get; set; } = null!;
        public DbSet<DepartemenView> Departemens { get; set; } = null!;
        public DbSet<JabatanView> Jabatans { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Table mappings
            modelBuilder.Entity<PasswordOverride>()
                .ToTable("tbl_m_pengguna_sandi")
                .HasKey(p => p.Nrp);

            modelBuilder.Entity<HazardReport>()
                .ToTable("tbl_t_hazard_report");

            modelBuilder.Entity<Inspection>()
                .ToTable("tbl_t_inspection");

            modelBuilder.Entity<ActionPlan>()
                .ToTable("tbl_t_action_plan");

            modelBuilder.Entity<SafetyTalk>()
                .ToTable("tbl_t_safety_talk");

            modelBuilder.Entity<P5m>()
                .ToTable("tbl_t_p5m");

            modelBuilder.Entity<Quiz>()
                .ToTable("table_m_quis");

            modelBuilder.Entity<QuizAnswer>()
                .ToTable("table_m_quis_detail");

            modelBuilder.Entity<Observation>()
                .ToTable("tbl_t_observation");

            modelBuilder.Entity<P2hVehicle>()
                .ToTable("tbl_m_p2h_vehicle");

            modelBuilder.Entity<P2hReport>()
                .ToTable("tbl_t_p2h_report");

            modelBuilder.Entity<DpaReport>()
                .ToTable("tbl_t_dpa_report");

            modelBuilder.Entity<DpaDriver>()
                .ToTable("tbl_m_dpa_driver");

            modelBuilder.Entity<DpaDriver>()
                .HasIndex(d => d.DriverNamaNormalized)
                .IsUnique();

            modelBuilder.Entity<TimelineLike>()
                .ToTable("tbl_t_likes");

            modelBuilder.Entity<TimelineComment>()
                .ToTable("tbl_t_comments");

            modelBuilder.Entity<RunningText>()
                .ToTable("tbl_m_running_text");

            modelBuilder.Entity<AppUser>()
                .ToTable("tbl_t_app_user");

            modelBuilder.Entity<MasterArea>()
                .ToTable("tbl_m_area_utama");

            modelBuilder.Entity<IncidentNews>()
                .ToTable("tbl_t_incident_news");

            modelBuilder.Entity<AttendanceEvent>()
                .ToTable("tbl_t_attendance_event");

            modelBuilder.Entity<AttendanceRecord>()
                .ToTable("tbl_t_attendance_record");

            modelBuilder.Entity<AttendanceEvent>()
                .HasIndex(e => e.QrToken)
                .IsUnique();

            modelBuilder.Entity<AttendanceRecord>()
                .HasIndex(r => new { r.AttendanceEventId, r.Nik })
                .IsUnique();

            modelBuilder.Entity<AttendanceRecord>()
                .HasOne(r => r.AttendanceEvent)
                .WithMany(e => e.AttendanceRecords)
                .HasForeignKey(r => r.AttendanceEventId)
                .OnDelete(DeleteBehavior.Cascade);

            // View mappings
            modelBuilder.Entity<KaryawanView>()
                .ToView("vw_karyawan")
                .HasKey(k => k.IdKaryawan);

            modelBuilder.Entity<PersonalView>()
                .ToView("vw_personal")
                .HasKey(p => p.IdPersonal);

            modelBuilder.Entity<PenggunaView>()
                .ToView("vw_pengguna")
                .HasKey(p => p.PenggunaId);

            modelBuilder.Entity<PerusahaanView>()
                .ToView("vw_perusahaan")
                .HasKey(p => p.PerusahaanId);

            modelBuilder.Entity<PerusahaanHierarchyRelationView>()
                .ToView("vw_m_hirarki_perusahaan")
                .HasNoKey();

            modelBuilder.Entity<DepartemenView>()
                .ToView("vw_departemen")
                .HasKey(d => d.DepartemenId);

            modelBuilder.Entity<JabatanView>()
                .ToView("vw_jabatan")
                .HasKey(j => j.JabatanId);

            // Apply snake_case column names mapping
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(ConvertToSnakeCase(property.Name));
                }
            }
        }

        private static string ConvertToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Special mappings to ensure 100% database schema compliance
            if (input == "NoNik") return "no_nik";
            if (input == "NoKtp") return "no_ktp";
            if (input == "Hp1") return "hp_1";

            var startUnderscore = System.Text.RegularExpressions.Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2");
            return startUnderscore.ToLower();
        }
    }
}

