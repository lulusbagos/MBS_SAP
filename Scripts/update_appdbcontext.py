with open('Data/AppDbContext.cs', 'r', encoding='utf-8') as f:
    content = f.read()

target1 = "public DbSet<SapQualityAssessment> SapQualityAssessments { get; set; } = null!;"
replacement1 = """public DbSet<SapQualityAssessment> SapQualityAssessments { get; set; } = null!;

        // BBS (Behavior Based Safety)
        public DbSet<BbsCategory> BbsCategories { get; set; } = null!;
        public DbSet<BbsTopic> BbsTopics { get; set; } = null!;
        public DbSet<BbsBehavior> BbsBehaviors { get; set; } = null!;
        public DbSet<BbsObservation> BbsObservations { get; set; } = null!;"""

if target1 in content:
    content = content.replace(target1, replacement1, 1)
    print("DbSets added.")
else:
    print("target1 not found")

target2 = '.ToTable("tbl_m_penilaian_kualitas_sap");'
replacement2 = """.ToTable("tbl_m_penilaian_kualitas_sap");

            // BBS mappings
            modelBuilder.Entity<BbsCategory>()
                .ToTable("tbl_m_bbs_category");

            modelBuilder.Entity<BbsTopic>()
                .ToTable("tbl_m_bbs_topic");

            modelBuilder.Entity<BbsBehavior>()
                .ToTable("tbl_m_bbs_behavior");

            modelBuilder.Entity<BbsObservation>()
                .ToTable("tbl_t_bbs_observation")
                .HasIndex(b => b.ObservationNo)
                .IsUnique();"""

if target2 in content:
    content = content.replace(target2, replacement2, 1)
    print("Mappings added.")
else:
    print("target2 not found")

with open('Data/AppDbContext.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print("Finished!")
