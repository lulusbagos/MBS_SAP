import pyodbc

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

create_tables_sql = """
-- 1. Table Master Kategori BBS
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='tbl_m_bbs_category' and xtype='U')
BEGIN
    CREATE TABLE tbl_m_bbs_category (
        id INT IDENTITY(1,1) PRIMARY KEY,
        name NVARCHAR(150) NOT NULL,
        category_type NVARCHAR(50) NOT NULL DEFAULT 'Both', -- 'Rutin', 'SpecialCase', 'Both'
        icon NVARCHAR(50) NULL DEFAULT 'bi-clipboard-check',
        color NVARCHAR(50) NULL DEFAULT '#0284c7',
        description NVARCHAR(255) NULL,
        sort_order INT NOT NULL DEFAULT 0,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        updated_at DATETIME2 NULL
    );
    PRINT 'tbl_m_bbs_category created';
END;

-- 2. Table Master Sub-Kategori / Topik BBS (untuk Special Case)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='tbl_m_bbs_topic' and xtype='U')
BEGIN
    CREATE TABLE tbl_m_bbs_topic (
        id INT IDENTITY(1,1) PRIMARY KEY,
        category_id INT NOT NULL,
        name NVARCHAR(150) NOT NULL,
        icon NVARCHAR(50) NULL DEFAULT 'bi-tag',
        description NVARCHAR(255) NULL,
        sort_order INT NOT NULL DEFAULT 0,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        updated_at DATETIME2 NULL
    );
    PRINT 'tbl_m_bbs_topic created';
END;

-- 3. Table Master Perilaku yang Diamati (Behavior Items)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='tbl_m_bbs_behavior' and xtype='U')
BEGIN
    CREATE TABLE tbl_m_bbs_behavior (
        id INT IDENTITY(1,1) PRIMARY KEY,
        category_id INT NOT NULL,
        topic_id INT NULL,
        behavior_text NVARCHAR(300) NOT NULL,
        default_classification NVARCHAR(50) NOT NULL DEFAULT 'At-Risk', -- 'Safe', 'At-Risk', 'Both'
        default_risk_level NVARCHAR(50) NULL DEFAULT 'Sedang', -- 'Rendah', 'Sedang', 'Tinggi', 'Sangat Tinggi'
        guidance_note NVARCHAR(500) NULL,
        sort_order INT NOT NULL DEFAULT 0,
        is_active BIT NOT NULL DEFAULT 1,
        created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        updated_at DATETIME2 NULL
    );
    PRINT 'tbl_m_bbs_behavior created';
END;

-- 4. Table Transaksi Observasi BBS
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='tbl_t_bbs_observation' and xtype='U')
BEGIN
    CREATE TABLE tbl_t_bbs_observation (
        id INT IDENTITY(1,1) PRIMARY KEY,
        observation_no NVARCHAR(50) NOT NULL UNIQUE,
        observation_type NVARCHAR(50) NOT NULL DEFAULT 'Rutin', -- 'Rutin', 'SpecialCase'
        tanggal DATETIME2 NOT NULL,
        waktu NVARCHAR(10) NOT NULL,
        observer_nik NVARCHAR(50) NOT NULL,
        observer_nama NVARCHAR(150) NOT NULL,
        observer_dept NVARCHAR(100) NULL,
        observer_perusahaan_id INT NULL,
        observer_perusahaan NVARCHAR(200) NULL,
        site NVARCHAR(100) NOT NULL,
        area NVARCHAR(100) NOT NULL,
        detil_lokasi NVARCHAR(250) NULL,
        observed_nik NVARCHAR(50) NULL,
        observed_nama NVARCHAR(150) NULL,
        observed_jabatan NVARCHAR(100) NULL,
        observed_dept NVARCHAR(100) NULL,
        observed_perusahaan_id INT NULL,
        observed_perusahaan NVARCHAR(200) NULL,
        category_id INT NULL,
        category_name NVARCHAR(150) NULL,
        topic_id INT NULL,
        topic_name NVARCHAR(150) NULL,
        selected_behaviors_json NVARCHAR(MAX) NULL,
        custom_behavior NVARCHAR(300) NULL,
        klasifikasi NVARCHAR(50) NOT NULL, -- 'Aman', 'Berisiko'
        kondisi_jalan NVARCHAR(100) NULL,
        kondisi_kerja NVARCHAR(100) NULL,
        faktor_pemicu NVARCHAR(500) NULL,
        tingkat_risiko NVARCHAR(50) NOT NULL, -- 'Rendah', 'Sedang', 'Tinggi', 'Sangat Tinggi'
        deskripsi NVARCHAR(2000) NULL,
        respons_pekerja NVARCHAR(50) NOT NULL, -- 'Positif', 'Netral', 'Resisten'
        tindakan_dilakukan NVARCHAR(500) NULL,
        catatan_coaching NVARCHAR(2000) NULL,
        foto_url NVARCHAR(500) NULL,
        is_deleted BIT NOT NULL DEFAULT 0,
        created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        updated_at DATETIME2 NULL
    );
    PRINT 'tbl_t_bbs_observation created';
END;
"""

cursor.execute(create_tables_sql)
conn.commit()
print("Tables created successfully.")

# Seed initial categories if table empty
cursor.execute("SELECT COUNT(*) FROM tbl_m_bbs_category")
cat_count = cursor.fetchone()[0]

if cat_count == 0:
    categories = [
        ("Mengemudi & Hauling", "Both", "bi-truck", "#0284c7", "Aktivitas berkendara, operasional hauling, dan transportasi", 1),
        ("Interaksi Alat & Manusia", "Both", "bi-people", "#f59e0b", "Jarak aman orang terhadap alat berat dan kendaraan tambang", 2),
        ("Area Loading & Dumping", "Both", "bi-cone-striped", "#10b981", "Manuver di front loading, disposal, dan stockpile", 3),
        ("Perawatan & Maintenance", "Both", "bi-wrench-adjustable", "#6366f1", "Aktivitas bengkel, perbaikan unit, dan servis berkala", 4),
        ("Pekerjaan Berisiko Khusus", "SpecialCase", "bi-gear-wide-connected", "#ec4899", "LOTO, Bekerja di Ketinggian, Lifting, Ruang Terbatas", 5),
        ("Faktor Manusia", "Both", "bi-person-gear", "#8b5cf6", "Fatigue, Distraction, Complacency, dan kondisi emosional", 6),
        ("Kepatuhan Prosedur", "Both", "bi-file-earmark-ruled", "#14b8a6", "Permit to Work, SOP, IK, dan form P2H", 7),
        ("Kepemimpinan & Intervensi", "Both", "bi-award", "#06b6d4", "Stop Work Authority, intervensi positif, dan safety talk", 8),
        ("Penggunaan APD", "Rutin", "bi-shield-check", "#eab308", "Kepatuhan dan kelayakan pemakaian APD di tempat kerja", 9),
        ("Housekeeping & Lingkungan", "Rutin", "bi-recycle", "#84cc16", "Kerapihan area kerja, penataan alat, dan pengelolaan limbah", 10),
    ]

    for name, ctype, icon, color, desc, sort in categories:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_category (name, category_type, icon, color, description, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, ?, 1, GETDATE())
        """, (name, ctype, icon, color, desc, sort))
    conn.commit()
    print("Categories seeded.")

# Fetch category IDs
cursor.execute("SELECT id, name FROM tbl_m_bbs_category")
cat_map = {row[1]: row[0] for row in cursor.fetchall()}

# Seed Topics if empty
cursor.execute("SELECT COUNT(*) FROM tbl_m_bbs_topic")
topic_count = cursor.fetchone()[0]

if topic_count == 0 and "Mengemudi & Hauling" in cat_map:
    # Mengemudi topics
    c_id = cat_map["Mengemudi & Hauling"]
    topics_mengemudi = [
        ("Kecepatan Kendaraan (Speeding)", "bi-speedometer2", "Kepatuhan batas kecepatan di jalan hauling & tambang", 1),
        ("Jarak Aman (Following Distance)", "bi-arrow-left-right", "Menjaga jarak aman antar kendaraan sesuai SOP", 2),
        ("Distraksi Saat Mengemudi", "bi-phone-vibrate", "Penggunaan HP, radio tidak sesuai, makan/minum saat bergerak", 3),
        ("Kelelahan (Fatigue)", "bi-moon-stars", "Kondisi mengantuk, microsleep, kurang tidur", 4),
        ("Persimpangan (Intersection)", "bi-sign-intersection", "Prosedur berhenti, lihat kiri-kanan, dan hak jalan", 5),
        ("Menyalip (Overtaking)", "bi-arrow-repeat", "Prosedur menyalip aman dengan konfirmasi radio", 6),
        ("Disiplin Jalur (Lane Discipline)", "bi-dash-lg", "Konsistensi lajur kiri, tidak memotong marka jalan", 7),
        ("Pengereman & Manuver", "bi-exclamation-octagon", "Teknik pengereman bertahap dan manuver aman", 8),
    ]
    for name, icon, desc, sort in topics_mengemudi:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, 1, GETDATE())
        """, (c_id, name, icon, desc, sort))

    # Interaksi Alat topics
    if "Interaksi Alat & Manusia" in cat_map:
        c_id2 = cat_map["Interaksi Alat & Manusia"]
        topics_interaksi = [
            ("Jarak Aman Manusia & Alat", "bi-arrows-angle-expand", "Radius aman interaksi orang dengan alat beroperasi", 1),
            ("Komunikasi Kontak Mata & Radio", "bi-broadcast", "Konfirmasi positif sebelum memasuki area alat", 2),
            ("Area Blind Spot Alat", "bi-eye-slash", "Menghindari titik buta (blind spot) alat berat", 3),
        ]
        for name, icon, desc, sort in topics_interaksi:
            cursor.execute("""
                INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
                VALUES (?, ?, ?, ?, ?, 1, GETDATE())
            """, (c_id2, name, icon, desc, sort))

    # Pekerjaan Berisiko Khusus topics
    if "Pekerjaan Berisiko Khusus" in cat_map:
        c_id3 = cat_map["Pekerjaan Berisiko Khusus"]
        topics_khusus = [
            ("LOTO (Lock Out Tag Out)", "bi-lock", "Penguncian dan pelabelan sumber energi berbahaya", 1),
            ("Bekerja di Ketinggian", "bi-ladder", "Pemakaian full body harness dan scaffolding aman", 2),
            ("Lifting & Rigging", "bi-arrows-move", "Operasi pengangkatan crane dan perlengkapan rigger", 3),
            ("Ruang Terbatas (Confined Space)", "bi-box", "Uji gas atmosfer dan izin masuk ruang terbatas", 4),
        ]
        for name, icon, desc, sort in topics_khusus:
            cursor.execute("""
                INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
                VALUES (?, ?, ?, ?, ?, 1, GETDATE())
            """, (c_id3, name, icon, desc, sort))

    conn.commit()
    print("Topics seeded.")

# Fetch topic IDs
cursor.execute("SELECT id, name, category_id FROM tbl_m_bbs_topic")
topic_map = {row[1]: (row[0], row[2]) for row in cursor.fetchall()}

# Seed Behaviors if empty
cursor.execute("SELECT COUNT(*) FROM tbl_m_bbs_behavior")
bhv_count = cursor.fetchone()[0]

if bhv_count == 0:
    behaviors = []
    # Distraksi Saat Mengemudi
    if "Distraksi Saat Mengemudi" in topic_map:
        t_id, c_id = topic_map["Distraksi Saat Mengemudi"]
        behaviors.extend([
            (c_id, t_id, "Menggunakan HP saat mengemudi", "At-Risk", "Sangat Tinggi", "Menelepon atau membaca/membalas chat saat unit sedang berjalan", 1),
            (c_id, t_id, "Menggunakan radio komunikasi tidak sesuai prosedur", "At-Risk", "Sedang", "Berbicara terlalu lama atau radio candaan saat mengemudi", 2),
            (c_id, t_id, "Makan atau minum saat kendaraan bergerak", "At-Risk", "Sedang", "Tangan lepas dari kemudi untuk makan/minum di jalur hauling", 3),
            (c_id, t_id, "Mengatur perangkat / layar dashboard terlalu lama", "At-Risk", "Sedang", "Pandangan teralihkan ke monitor display melebihi 2 detik", 4),
            (c_id, t_id, "Mengambil barang di dalam kabin saat kendaraan bergerak", "At-Risk", "Tinggi", "Membungkuk mengambil barang jatuh saat unit berjalan", 5),
            (c_id, t_id, "Melihat ke arah lain terlalu lama", "At-Risk", "Tinggi", "Tidak fokus memantau jalan di depan", 6),
            (c_id, t_id, "Fokus penuh ke jalan dan spion", "Safe", "Rendah", "Konsentrasi penuh dan pandangan aktif ke arah lajur jalan", 7),
        ])

    # Kecepatan Kendaraan
    if "Kecepatan Kendaraan (Speeding)" in topic_map:
        t_id, c_id = topic_map["Kecepatan Kendaraan (Speeding)"]
        behaviors.extend([
            (c_id, t_id, "Melebihi batas kecepatan rambu di area tambang/hauling", "At-Risk", "Sangat Tinggi", "Over speeding di atas limit rambu", 1),
            (c_id, t_id, "Tidak mengurangi kecepatan saat jalan basah / licin", "At-Risk", "Sangat Tinggi", "Kecepatan tinggi saat kondisi hujan/berlumpur", 2),
            (c_id, t_id, "Mengemudi sesuai batas kecepatan yang ditentukan", "Safe", "Rendah", "Mematuhi rambu limit kecepatan jalan", 3),
        ])

    # Jarak Aman
    if "Jarak Aman (Following Distance)" in topic_map:
        t_id, c_id = topic_map["Jarak Aman (Following Distance)"]
        behaviors.extend([
            (c_id, t_id, "Jarak terlalu dekat dengan kendaraan di depan (tailgating)", "At-Risk", "Sangat Tinggi", "Jarak kurang dari standar 4 panjang unit", 1),
            (c_id, t_id, "Menjaga jarak aman minimal sesuai prosedur", "Safe", "Rendah", "Konsisten memelihara jarak reaksi aman", 2),
        ])

    # Interaksi Alat
    if "Jarak Aman Manusia & Alat" in topic_map:
        t_id, c_id = topic_map["Jarak Aman Manusia & Alat"]
        behaviors.extend([
            (c_id, t_id, "Berada dalam radius bahaya swing/gerak alat tanpa konfirmasi", "At-Risk", "Sangat Tinggi", "Orang berada di radius swing excavator/loader", 1),
            (c_id, t_id, "Memastikan izin kontak visual 2 arah sebelum mendekati alat", "Safe", "Rendah", "Operator menghentikan gerak dan memberi sinyal aman", 2),
        ])

    # LOTO
    if "LOTO (Lock Out Tag Out)" in topic_map:
        t_id, c_id = topic_map["LOTO (Lock Out Tag Out)"]
        behaviors.extend([
            (c_id, t_id, "Melakukan servis/perbaikan tanpa memasang padlock LOTO", "At-Risk", "Sangat Tinggi", "Bahaya energi tersimpan atau unit dihidupkan orang lain", 1),
            (c_id, t_id, "Memasang gembok LOTO pribadi dan menguji isolasi energi (zero energy)", "Safe", "Rendah", "Prosedur isolasi sempurna dan terverifikasi", 2),
        ])

    # Kategori Rutin: Penggunaan APD
    if "Penggunaan APD" in cat_map:
        c_id = cat_map["Penggunaan APD"]
        behaviors.extend([
            (c_id, None, "Menggunakan seat belt dengan benar saat berkendara/operasi", "Safe", "Rendah", "Seat belt terpasang klik dan kencang", 1),
            (c_id, None, "Tidak menggunakan seat belt saat mengemudi/mengoperasikan unit", "At-Risk", "Sangat Tinggi", "Mengabaikan sabuk pengaman kabin", 2),
            (c_id, None, "Menggunakan APD lengkap sesuai matriks bahaya area", "Safe", "Rendah", "Helm, kacamata, rompi, sepatu safety standar", 3),
            (c_id, None, "Tidak menggunakan kacamata safety di area berdebu/bengkel", "At-Risk", "Sedang", "Mata berisiko terkena partikel terbang", 4),
            (c_id, None, "Tidak menggunakan rompi reflektif (high-vis) di area operasional", "At-Risk", "Tinggi", "Visibilitas rendah bagi operator alat berat", 5),
        ])

    # Kategori Rutin: Housekeeping
    if "Housekeeping & Lingkungan" in cat_map:
        c_id = cat_map["Housekeeping & Lingkungan"]
        behaviors.extend([
            (c_id, None, "Merapikan area kerja dan menyimpan alat pada tempatnya", "Safe", "Rendah", "Area kerja bersih, rapi, dan bebas bahaya tersandung", 1),
            (c_id, None, "Membiarkan ceceran oli/bahan kimia tanpa ditangani spill kit", "At-Risk", "Sedang", "Bahaya terpeleset dan pencemaran tanah", 2),
            (c_id, None, "Menumpuk barang menghalangi akses jalan darurat atau APAR", "At-Risk", "Tinggi", "Akses evakuasi dan pemadam api terblokir", 3),
        ])

    for c_id, t_id, btext, defclass, defrisk, gnote, sort in behaviors:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_behavior (category_id, topic_id, behavior_text, default_classification, default_risk_level, guidance_note, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, 1, GETDATE())
        """, (c_id, t_id, btext, defclass, defrisk, gnote, sort))

    conn.commit()
    print(f"{len(behaviors)} behaviors seeded.")

cursor.close()
conn.close()
print("All done!")
