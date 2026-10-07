import pyodbc

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

# 1. Categories to add
extra_categories = [
    ("Peledakan & Handak", "Both", "bi-fire", "#ef4444", "Operasional blasting, penanganan bahan peledak, dan gudang handak", 11),
    ("Operasional Port & Kelautan", "Both", "bi-water", "#0ea5e9", "Sandar tongkang, pemuatan batubara jetty, dan keselamatan perairan", 12),
    ("Pencegahan Kebakaran & Darurat", "Both", "bi-shield-exclamation", "#f97316", "Kesiapan APAR, jalur evakuasi, dan batubara terbakar spontan", 13),
    ("Pengelolaan Lingkungan Tambang", "Both", "bi-tree-fill", "#22c55e", "Pengendalian debu, pengelolaan air sump, dan fasilitas settling pond", 14),
]

for name, ctype, icon, color, desc, sort in extra_categories:
    cursor.execute("SELECT id FROM tbl_m_bbs_category WHERE name = ?", (name,))
    exists = cursor.fetchone()
    if not exists:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_category (name, category_type, icon, color, description, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, ?, 1, GETDATE())
        """, (name, ctype, icon, color, desc, sort))
        print(f"Added Category: {name}")

conn.commit()

# Re-fetch category map
cursor.execute("SELECT id, name FROM tbl_m_bbs_category")
cat_map = {row[1]: row[0] for row in cursor.fetchall()}

# 2. Topics for new categories
extra_topics = [
    # Peledakan & Handak
    ("Peledakan & Handak", "Radius Aman Peledakan & Evakuasi", "bi-shield-shaded", "Evakuasi personil dan unit di luar batas clearance radius blasting", 1),
    ("Peledakan & Handak", "Penanganan Handak & Gudang", "bi-box-seam", "Transportasi amonium nitrat, detonator, dan pengamanan gudang handak", 2),
    ("Peledakan & Handak", "Prosedur Sirine & Radio Blasting", "bi-broadcast-pin", "Kepatuhan terhadap kode bunyi sirine 1-2-3 dan radio silence", 3),

    # Operasional Port & Kelautan
    ("Operasional Port & Kelautan", "Sandar Tongkang & Tali Mooring", "bi-anchor", "Manuver tugboat, penarikan tali tambat, dan pengikatan tongkang", 1),
    ("Operasional Port & Kelautan", "Keselamatan di Atas Air & Pelampung", "bi-life-preserver", "Pemakaian life jacket di bibir dermaga, trestle, dan tongkang", 2),
    ("Operasional Port & Kelautan", "Pengoperasian Conveyor & Ship Loader", "bi-symmetry-horizontal", "Operasional belt conveyor, chute, dan boom ship loader", 3),

    # Pencegahan Kebakaran & Darurat
    ("Pencegahan Kebakaran & Darurat", "Kesiapan APAR & Fire Suppression", "bi-fire", "Pemeriksaan segel, jarum tekanan hijau, dan nozzle pemadam api", 1),
    ("Pencegahan Kebakaran & Darurat", "Jalur Evakuasi & Muster Point", "bi-signpost-split", "Akses bebas hambatan ke titik kumpul dan pintu darurat", 2),
    ("Pencegahan Kebakaran & Darurat", "Penanganan Batubara Terbakar (Self Combustion)", "bi-cloud-fog2", "Deteksi asap, pemadaman hotspot stockpile, dan pembongkaran timbunan", 3),

    # Pengelolaan Lingkungan Tambang
    ("Pengelolaan Lingkungan Tambang", "Pengendalian Debu & Water Truck", "bi-cloud-rain", "Jadwal penyiraman jalan hauling dan pengaturan semprotan air", 1),
    ("Pengelolaan Lingkungan Tambang", "Pengelolaan Sump & Pompa Tambang", "bi-droplet-half", "Pengawasan dinding sump, pelampung pompa, dan pipa discharge air", 2),
    ("Pengelolaan Lingkungan Tambang", "Settling Pond & Pengendalian Sedimentasi", "bi-layers", "Pembersihan kompartemen sediment trap dan pemantauan pH/TSS air", 3),
]

for cat_name, t_name, t_icon, t_desc, t_sort in extra_topics:
    c_id = cat_map[cat_name]
    cursor.execute("SELECT id FROM tbl_m_bbs_topic WHERE category_id = ? AND name = ?", (c_id, t_name))
    exists = cursor.fetchone()
    if not exists:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, 1, GETDATE())
        """, (c_id, t_name, t_icon, t_desc, t_sort))
        print(f"Added Topic: [{cat_name}] {t_name}")

conn.commit()

# Re-fetch topic map
cursor.execute("SELECT t.id, t.name, c.name FROM tbl_m_bbs_topic t JOIN tbl_m_bbs_category c ON t.category_id = c.id")
topic_map = {(row[2], row[1]): row[0] for row in cursor.fetchall()}

# 3. Behaviors for new categories
new_behaviors = [
    # Peledakan & Handak
    ("Peledakan & Handak", "Radius Aman Peledakan & Evakuasi", "Berada di luar batas radius aman peledakan (500m untuk manusia, 300m untuk alat)", "Safe", "Rendah", "Mematuhi perimeter clearance zona blasting yang ditentukan Juru Ledak", 1),
    ("Peledakan & Handak", "Radius Aman Peledakan & Evakuasi", "Memastikan pos pemblokir jalan (blocker) terpasang pita barikade dan rambu dilarang melintas", "Safe", "Rendah", "Tidak ada celah masuk bagi kendaraan tanpa izin", 2),
    ("Peledakan & Handak", "Radius Aman Peledakan & Evakuasi", "Masuk ke area blok peledakan saat bendera merah masih terpasang", "At-Risk", "Sangat Tinggi", "Pelanggaran fatal berisiko terkena lontaran batu (flyrock) peledakan", 3),
    ("Peledakan & Handak", "Radius Aman Peledakan & Evakuasi", "Menerobos pos blocker peledakan sebelum pengumuman 'All Clear' disiarkan", "At-Risk", "Sangat Tinggi", "Potensi paparan gas beracun (fumes) atau peledakan susulan", 4),

    ("Peledakan & Handak", "Penanganan Handak & Gudang", "Memastikan muatan bahan peledak diikat kokoh dan kendaraan berbendera merah menyala", "Safe", "Rendah", "Standar konvoi angkutan handak dengan APAR siap pakai", 1),
    ("Peledakan & Handak", "Penanganan Handak & Gudang", "Membawa korek api, rokok, atau telepon genggam aktif ke dalam area gudang handak", "At-Risk", "Sangat Tinggi", "Sumber nyala dilarang keras di area bahan berdaya ledak tinggi", 2),
    ("Peledakan & Handak", "Penanganan Handak & Gudang", "Mengisi lubang ledak (charging) saat terdeteksi petir / kilat di area tambang", "At-Risk", "Sangat Tinggi", "Bahaya arus listrik statis petir memicu peledakan dini", 3),

    ("Peledakan & Handak", "Prosedur Sirine & Radio Blasting", "Mematuhi radio silence (dilarang menggunakan radio komunikasi kecuali Juru Ledak) saat firing", "Safe", "Rendah", "Saluran radio steril untuk koordinasi tembak", 1),
    ("Peledakan & Handak", "Prosedur Sirine & Radio Blasting", "Mengabaikan bunyi sirine peringatan peledakan dan tetap beraktivitas di sekitar lokasi", "At-Risk", "Sangat Tinggi", "Wajib segera evakuasi saat sirine peringatan berbunyi", 2),

    # Operasional Port & Kelautan
    ("Operasional Port & Kelautan", "Sandar Tongkang & Tali Mooring", "Menjaga jarak aman dari tali tambat (mooring line) yang sedang ditarik bertegangan tinggi", "Safe", "Rendah", "Menghindari zona pantulan (snap-back zone) jika tali putus", 1),
    ("Operasional Port & Kelautan", "Sandar Tongkang & Tali Mooring", "Melangkah atau melompati tali tambat yang sedang tegang", "At-Risk", "Sangat Tinggi", "Risiko putus mendadak berakibat cedera hantaman fatal", 2),
    ("Operasional Port & Kelautan", "Sandar Tongkang & Tali Mooring", "Melompat dari dermaga ke tongkang tanpa jembatan penyeberangan (gangway) berpagar", "At-Risk", "Sangat Tinggi", "Bahaya terpeleset jatuh ke laut atau terjepit bibir dermaga", 3),

    ("Operasional Port & Kelautan", "Keselamatan di Atas Air & Pelampung", "Mengenakan Life Jacket / Work Vest dengan benar saat berada di tepi dermaga atau tongkang", "Safe", "Rendah", "Perlindungan tenggelam wajib untuk seluruh aktivitas di atas air", 1),
    ("Operasional Port & Kelautan", "Keselamatan di Atas Air & Pelampung", "Memastikan pelampung penyelamat (lifebuoy) dengan tali lempar tersedia di sepanjang dermaga", "Safe", "Rendah", "Kesiapan alat tanggap darurat orang jatuh ke laut (man overboard)", 2),
    ("Operasional Port & Kelautan", "Keselamatan di Atas Air & Pelampung", "Bekerja di bibir tongkang atau tepian dermaga tanpa memakai life jacket / pelampung", "At-Risk", "Sangat Tinggi", "Bahaya tenggelam seketika jika terjatuh ke perairan deras", 3),

    ("Operasional Port & Kelautan", "Pengoperasian Conveyor & Ship Loader", "Memastikan tali darurat emergency pull cord di sepanjang conveyor berfungsi normal", "Safe", "Rendah", "Dapat dihentikan seketika saat terjadi kondisi darurat", 1),
    ("Operasional Port & Kelautan", "Pengoperasian Conveyor & Ship Loader", "Membersihkan batubara di dekat puli drum conveyor yang sedang berputar", "At-Risk", "Sangat Tinggi", "Bahaya titik jepit (nip point) berputar menarik anggota tubuh", 2),
    ("Operasional Port & Kelautan", "Pengoperasian Conveyor & Ship Loader", "Menaiki atau melompati belt conveyor yang sedang aktif beroperasi", "At-Risk", "Sangat Tinggi", "Pelanggaran serius berisiko jatuh terbawa aliran batubara", 3),

    # Pencegahan Kebakaran & Darurat
    ("Pencegahan Kebakaran & Darurat", "Kesiapan APAR & Fire Suppression", "Memeriksa jarum tekanan APAR berada pada zona hijau dan pin segel utuh terpasang", "Safe", "Rendah", "Memastikan kesiapan pakai pemadam api saat terjadi insiden kebakaran", 1),
    ("Pencegahan Kebakaran & Darurat", "Kesiapan APAR & Fire Suppression", "Memeriksa sistem otomatis Fire Suppression pada alat berat aktif dan tidak terisolir", "Safe", "Rendah", "Proteksi kebakaran otomatis ruang mesin unit", 2),
    ("Pencegahan Kebakaran & Darurat", "Kesiapan APAR & Fire Suppression", "Menumpuk barang atau parkir kendaraan menghalangi akses tabung APAR / hidran", "At-Risk", "Tinggi", "Menghambat respon cepat pemadaman saat awal titik api muncul", 3),
    ("Pencegahan Kebakaran & Darurat", "Kesiapan APAR & Fire Suppression", "Membiarkan tabung APAR dengan tekanan kosong (jarum di zona merah) tanpa diganti", "At-Risk", "Tinggi", "Alat proteksi tidak akan berfungsi saat terjadi kebakaran", 4),

    ("Pencegahan Kebakaran & Darurat", "Jalur Evakuasi & Muster Point", "Memastikan jalur evakuasi darurat terang, bersih, dan pintu darurat tidak terkunci", "Safe", "Rendah", "Jalur penyelamatan bebas hambatan", 1),
    ("Pencegahan Kebakaran & Darurat", "Jalur Evakuasi & Muster Point", "Mengetahui lokasi titik kumpul (Muster Point) terdekat dan rute evakuasinya", "Safe", "Rendah", "Kesiapsiagaan personil saat sirine darurat berbunyi", 2),
    ("Pencegahan Kebakaran & Darurat", "Jalur Evakuasi & Muster Point", "Menaruh barang-barang material menghalangi tangga darurat gedung atau workshop", "At-Risk", "Sedang", "Bisa memicu kepanikan dan tersandung saat evakuasi darurat", 3),

    ("Pencegahan Kebakaran & Darurat", "Penanganan Batubara Terbakar (Self Combustion)", "Melaporkan segera titik asap (hotspot) atau bau belerang pada timbunan batubara stockpile", "Safe", "Rendah", "Tindakan dini mencegah penyebaran api batubara", 1),
    ("Pencegahan Kebakaran & Darurat", "Penanganan Batubara Terbakar (Self Combustion)", "Melakukan pemadaman hotspot dengan metode spreading (pembongkaran) dan pendinginan air", "Safe", "Rendah", "Teknik pemadaman batubara terbakar yang benar dan efektif", 2),
    ("Pencegahan Kebakaran & Darurat", "Penanganan Batubara Terbakar (Self Combustion)", "Membiarkan hotspot batubara membesar tanpa koordinasi pemadaman dengan tim stockpile", "At-Risk", "Tinggi", "Bahaya meluasnya kebakaran batubara dan polusi asap beracun", 3),

    # Pengelolaan Lingkungan Tambang
    ("Pengelolaan Lingkungan Tambang", "Pengendalian Debu & Water Truck", "Mengatur jadwal penyiraman jalan hauling secara berkala dan proporsional (tidak terlalu basah/licin)", "Safe", "Rendah", "Menjaga visibilitas berkendara tanpa membuat jalan licin berbahaya", 1),
    ("Pengelolaan Lingkungan Tambang", "Pengendalian Debu & Water Truck", "Mengurangi kecepatan dan menyalakan lampu saat berpapasan dengan unit water truck menyiram", "Safe", "Rendah", "Waspada kondisi jalan basah sesaat setelah penyiraman", 2),
    ("Pengelolaan Lingkungan Tambang", "Pengendalian Debu & Water Truck", "Menyiram jalan berlebihan hingga membentuk lumpur tebal yang memicu skid/tergelincir", "At-Risk", "Tinggi", "Jalan licin membahayakan dump truck bermuatan", 3),

    ("Pengelolaan Lingkungan Tambang", "Pengelolaan Sump & Pompa Tambang", "Memeriksa tanggul bibir sump penampungan air tambang dalam kondisi aman dan tidak retak", "Safe", "Rendah", "Mencegah runtuhan tebing sump ke arah instalasi pompa", 1),
    ("Pengelolaan Lingkungan Tambang", "Pengelolaan Sump & Pompa Tambang", "Mendekati tepian sump air berlumpur tanpa pelampung dan tanpa tali pengaman", "At-Risk", "Sangat Tinggi", "Bahaya terperosok ke dalam lumpur hisap atau air tambang dalam", 2),

    ("Pengelolaan Lingkungan Tambang", "Settling Pond & Pengendalian Sedimentasi", "Memastikan saluran inlet, kompartemen pengendapan, dan outlet settling pond mengalir normal", "Safe", "Rendah", "Kepatuhan baku mutu air keluaran tambang (TSS dan pH netral)", 1),
    ("Pengelolaan Lingkungan Tambang", "Settling Pond & Pengendalian Sedimentasi", "Membuang limbah oli atau bahan kimia langsung ke saluran air tambang terbuka", "At-Risk", "Sangat Tinggi", "Pencemaran lingkungan serius melanggar regulasi perizinan lingkungan", 2),
]

added_b = 0
for cat_name, t_name, b_text, def_class, def_risk, g_note, sort in new_behaviors:
    c_id = cat_map[cat_name]
    t_id = topic_map[(cat_name, t_name)]
    cursor.execute("SELECT id FROM tbl_m_bbs_behavior WHERE category_id = ? AND behavior_text = ?", (c_id, b_text))
    exists = cursor.fetchone()
    if not exists:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_behavior (category_id, topic_id, behavior_text, default_classification, default_risk_level, guidance_note, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, 1, GETDATE())
        """, (c_id, t_id, b_text, def_class, def_risk, g_note, sort))
        added_b += 1

conn.commit()
print(f"Successfully added {added_b} new behaviors.")

cursor.execute('''
    SELECT c.id, c.name, COUNT(b.id) as total_behaviors
    FROM tbl_m_bbs_category c
    LEFT JOIN tbl_m_bbs_behavior b ON c.id = b.category_id
    GROUP BY c.id, c.name
    ORDER BY c.id
''')
print("\nFinal Master Categories & Behaviors:")
total_all = 0
for row in cursor.fetchall():
    print(f"[{row[0]}] {row[1]}: {row[2]} butir perilaku")
    total_all += row[2]
print(f"\nTotal Overall Behaviors in DB: {total_all}")

cursor.close()
conn.close()
