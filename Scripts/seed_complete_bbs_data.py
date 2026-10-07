import pyodbc

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

# 1. Fetch Categories
cursor.execute("SELECT id, name FROM tbl_m_bbs_category")
cat_map = {row[1]: row[0] for row in cursor.fetchall()}
print(f"Loaded {len(cat_map)} categories: {list(cat_map.keys())}")

# 2. Define All Topics to ensure every category has topics
new_topics = [
    # Area Loading & Dumping
    ("Area Loading & Dumping", "Operasional Front Loading", "bi-cone-striped", "Manuver antrean, posisi tunggu, dan interaksi di front galian", 1),
    ("Area Loading & Dumping", "Manuver & Dumping Disposal", "bi-exclamation-triangle", "Prosedur mundur dumping, tanggul pengaman, dan aba-aba spotter", 2),
    
    # Perawatan & Maintenance
    ("Perawatan & Maintenance", "Pengendalian Energi & Ganjal Ban", "bi-lock", "Pemasangan wheel chock, pemutusan arus baterai, dan isolasi", 1),
    ("Perawatan & Maintenance", "Bekerja di Bawah Unit & Attachment", "bi-arrow-down-square", "Safety prop bak dump, penurunan blade/ripper, penopang hidrolik", 2),
    ("Perawatan & Maintenance", "Penggunaan Peralatan Kerja (Tools)", "bi-tools", "Kelayakan hand tools, power tools, gerinda, dan alat las", 3),
    
    # Faktor Manusia
    ("Faktor Manusia", "Kesiapan Kerja & Fit to Work", "bi-heart-pulse", "Istirahat cukup, bebas obat penenang/alkohol, dan kondisi kesehatan", 1),
    ("Faktor Manusia", "Fokus, Rushing & Complacency", "bi-speedometer", "Menghindari tergesa-gesa, meremehkan bahaya, dan lelucon bahaya", 2),
    
    # Kepatuhan Prosedur
    ("Kepatuhan Prosedur", "Pemeriksaan Pra-Operasi (P2H)", "bi-clipboard2-check", "Pemeriksaan fungsi kritis rem, kemudi, klakson, lampu, dan wiper", 1),
    ("Kepatuhan Prosedur", "Izin Kerja Khusus & SIMPER/KIMPER", "bi-person-badge", "Kepemilikan izin kerja aktif, lisensi operasional, dan JSEA", 2),
    
    # Kepemimpinan & Intervensi
    ("Kepemimpinan & Intervensi", "Stop Work Authority & Intervensi", "bi-hand-index-thumb", "Menghentikan kerja berbahaya dan mengingatkan rekan kerja", 1),
    ("Kepemimpinan & Intervensi", "Komunikasi & Briefing Keselamatan", "bi-megaphone", "Partisipasi aktif P5M, safety talk, dan keterbukaan bahaya", 2),
    
    # Penggunaan APD
    ("Penggunaan APD", "Kepatuhan APD Wajib & Khusus", "bi-shield-check", "Pemakaian helm, kacamata, rompi, sepatu, sarung tangan, earplug", 1),
    
    # Housekeeping & Lingkungan
    ("Housekeeping & Lingkungan", "Kerapihan Area & Pengelolaan Limbah", "bi-recycle", "Penyimpanan alat, kebersihan lantai, dan pembuangan limbah B3", 1),
]

for cat_name, t_name, t_icon, t_desc, t_sort in new_topics:
    if cat_name in cat_map:
        c_id = cat_map[cat_name]
        cursor.execute("SELECT id FROM tbl_m_bbs_topic WHERE category_id = ? AND name = ?", (c_id, t_name))
        existing = cursor.fetchone()
        if not existing:
            cursor.execute("""
                INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
                VALUES (?, ?, ?, ?, ?, 1, GETDATE())
            """, (c_id, t_name, t_icon, t_desc, t_sort))
            print(f"Added topic: [{cat_name}] {t_name}")

conn.commit()

# Re-fetch Topics
cursor.execute("SELECT t.id, t.name, c.name FROM tbl_m_bbs_topic t JOIN tbl_m_bbs_category c ON t.category_id = c.id")
topic_map = {}
for row in cursor.fetchall():
    topic_map[(row[2], row[1])] = row[0]
print(f"Total topics now: {len(topic_map)}")

# 3. Define Behaviors for ALL categories & topics
# Tuple: (CategoryName, TopicName, BehaviorText, Classification, RiskLevel, GuidanceNote, SortOrder)
all_behaviors = [
    # --- MENGEMUDI & HAULING ---
    ("Mengemudi & Hauling", "Kecepatan Kendaraan (Speeding)", "Mengurangi kecepatan saat mendekati tikungan tajam, persimpangan, atau turunan", "Safe", "Rendah", "Melakukan perlambatan proaktif sebelum area rawan", 4),
    ("Mengemudi & Hauling", "Kecepatan Kendaraan (Speeding)", "Tidak mengurangi kecepatan saat visibilitas berkurang akibat debu atau kabut", "At-Risk", "Tinggi", "Kecepatan melebihi jarak pandang aman", 5),
    
    ("Mengemudi & Hauling", "Jarak Aman (Following Distance)", "Menambah jarak aman saat kondisi jalan berdebu, hujan, atau malam hari", "Safe", "Rendah", "Memberikan margin pengereman ekstra dalam kondisi cuaca buruk", 3),
    ("Mengemudi & Hauling", "Jarak Aman (Following Distance)", "Tidak menambah jarak saat konvoi di turunan curam", "At-Risk", "Tinggi", "Bahaya tabrak belakang jika unit depan rem mendadak", 4),
    
    ("Mengemudi & Hauling", "Kelelahan (Fatigue)", "Mengakui kondisi mengantuk/lelah dan melapor ke pengawas atau singgah di rest area", "Safe", "Rendah", "Inisiatif menghentikan unit saat kelelahan demi keselamatan", 1),
    ("Mengemudi & Hauling", "Kelelahan (Fatigue)", "Melakukan stretching fisik dan cuci muka di pos istirahat saat mulai lelah", "Safe", "Rendah", "Memulihkan kesegaran tubuh secara berkala", 2),
    ("Mengemudi & Hauling", "Kelelahan (Fatigue)", "Memaksakan mengemudi dalam kondisi mengantuk berat (microsleep)", "At-Risk", "Sangat Tinggi", "Risiko fatal keluar jalur, terguling, atau menabrak unit lain", 3),
    ("Mengemudi & Hauling", "Kelelahan (Fatigue)", "Mengabaikan alarm peringatan DSS (Driver Safety System) atau kamera fatigue", "At-Risk", "Sangat Tinggi", "Alarm bunyi berulang namun pengemudi tidak merespons istirahat", 4),
    
    ("Mengemudi & Hauling", "Persimpangan (Intersection)", "Berhenti sempurna (Stop), tengok kiri-kanan, dan bunyikan klakson sebelum melintas", "Safe", "Rendah", "Disiplin rambu Stop di setiap simpang tambang", 1),
    ("Mengemudi & Hauling", "Persimpangan (Intersection)", "Mendahulukan kendaraan yang memiliki hak jalan (right of way)", "Safe", "Rendah", "Menghormati prioritas unit bermuatan atau alat berat", 2),
    ("Mengemudi & Hauling", "Persimpangan (Intersection)", "Menerobos persimpangan tanpa berhenti dan tanpa konfirmasi radio", "At-Risk", "Sangat Tinggi", "Potensi tabrakan frontal atau samping di persimpangan", 3),
    ("Mengemudi & Hauling", "Persimpangan (Intersection)", "Ragu-ragu saat mengambil keputusan melintas di persimpangan jalan tambang", "At-Risk", "Sedang", "Dapat memicu kebingungan bagi pengemudi lain", 4),
    
    ("Mengemudi & Hauling", "Menyalip (Overtaking)", "Melakukan konfirmasi radio 2 arah dan menunggu jawaban 'aman/silakan' sebelum menyalip", "Safe", "Rendah", "Komunikasi tuntas memastikan kedua belah pihak siap", 1),
    ("Mengemudi & Hauling", "Menyalip (Overtaking)", "Memastikan jarak pandang bebas dan tidak menyalip di tikungan atau tanjakan", "Safe", "Rendah", "Visibilitas penuh ke arah depan", 2),
    ("Mengemudi & Hauling", "Menyalip (Overtaking)", "Menyalip tanpa izin komunikasi radio dari operator unit di depan", "At-Risk", "Sangat Tinggi", "Blind overtaking sangat berbahaya di jalur tambang", 3),
    ("Mengemudi & Hauling", "Menyalip (Overtaking)", "Menyalip di area terlarang (jembatan, tikungan buta, persimpangan, area sempit)", "At-Risk", "Sangat Tinggi", "Melanggar rambu dilarang menyalip", 4),
    
    ("Mengemudi & Hauling", "Disiplin Jalur (Lane Discipline)", "Konsisten berada di lajur kiri jalan dan menjaga posisi roda di tengah jalur", "Safe", "Rendah", "Mematuhi lajur berkendara standar", 1),
    ("Mengemudi & Hauling", "Disiplin Jalur (Lane Discipline)", "Menjaga jarak aman terhadap tanggul pengaman (safety berm / bund wall)", "Safe", "Rendah", "Tidak menggesek tanggul jalan saat melaju", 2),
    ("Mengemudi & Hauling", "Disiplin Jalur (Lane Discipline)", "Mengemudi terlalu ke tengah atau memotong marka pembatas jalan", "At-Risk", "Tinggi", "Membahayakan kendaraan dari arah berlawanan", 3),
    ("Mengemudi & Hauling", "Disiplin Jalur (Lane Discipline)", "Berjalan terlalu mepet ke bibir lereng atau tanggul jalan", "At-Risk", "Sangat Tinggi", "Potensi amblas atau terjun ke jurang", 4),
    
    ("Mengemudi & Hauling", "Pengereman & Manuver", "Menggunakan retarder / engine brake secara efektif sebelum foot brake di turunan", "Safe", "Rendah", "Mencegah rem panas (brake fading)", 1),
    ("Mengemudi & Hauling", "Pengereman & Manuver", "Berhenti secara bertahap tanpa pengereman mendadak", "Safe", "Rendah", "Mengendalikan stabilitas muatan dan unit", 2),
    ("Mengemudi & Hauling", "Pengereman & Manuver", "Menetralkan transmisi (gigi netral) saat menuruni jalan terjal", "At-Risk", "Sangat Tinggi", "Kehilangan kendali mesin dan rem", 3),
    ("Mengemudi & Hauling", "Pengereman & Manuver", "Mengerem mendadak pada jalan licin berlumpur yang memicu jackknife", "At-Risk", "Tinggi", "Roda mengunci dan unit tergelincir", 4),

    # --- INTERAKSI ALAT & MANUSIA ---
    ("Interaksi Alat & Manusia", "Jarak Aman Manusia & Alat", "Berada di luar radius bahaya swing (swing radius) alat gali/muat", "Safe", "Rendah", "Memelihara jarak bebas minimal 15-20 meter dari alat aktif", 3),
    ("Interaksi Alat & Manusia", "Jarak Aman Manusia & Alat", "Melintas atau berdiri di belakang unit alat berat yang sedang mundur", "At-Risk", "Sangat Tinggi", "Blindspot mundur sangat berbahaya bagi pejalan kaki", 4),
    
    ("Interaksi Alat & Manusia", "Komunikasi Kontak Mata & Radio", "Memastikan kontak mata langsung dan menerima sinyal aman dari operator sebelum mendekat", "Safe", "Rendah", "Operator menurunkan alat dan memberi isyarat aman", 1),
    ("Interaksi Alat & Manusia", "Komunikasi Kontak Mata & Radio", "Melakukan panggilan radio saluran kerja yang tepat sebelum memasuki area alat berat", "Safe", "Rendah", "Memberi tahu posisi dan maksud kedatangan", 2),
    ("Interaksi Alat & Manusia", "Komunikasi Kontak Mata & Radio", "Mendekati alat berat yang menyala tanpa konfirmasi positif dari operator", "At-Risk", "Sangat Tinggi", "Operator tidak menyadari keberadaan orang di sekitar unit", 3),
    ("Interaksi Alat & Manusia", "Komunikasi Kontak Mata & Radio", "Menggunakan isyarat tangan non-standar yang membingungkan operator", "At-Risk", "Sedang", "Gunakan aba-aba tangan baku pemandu tambang", 4),
    
    ("Interaksi Alat & Manusia", "Area Blind Spot Alat", "Menghindari titik buta alat berat dan selalu berada di zona terlihat spion/kamera", "Safe", "Rendah", "Jika Anda tidak melihat operator di spion, operator tidak melihat Anda", 1),
    ("Interaksi Alat & Manusia", "Area Blind Spot Alat", "Menggunakan rompi reflektif (high-vis) yang bersih dan berpendar terang", "Safe", "Rendah", "Meningkatkan visibilitas pekerja di siang dan malam hari", 2),
    ("Interaksi Alat & Manusia", "Area Blind Spot Alat", "Berada di titik buta sisi kanan atau belakang dump truck besar", "At-Risk", "Sangat Tinggi", "Area tanpa jangkauan pandang langsung sopir", 3),
    ("Interaksi Alat & Manusia", "Area Blind Spot Alat", "Memarkir kendaraan sarana (LV) di area blind spot manuver alat berat", "At-Risk", "Sangat Tinggi", "LV berisiko terlindas alat berat", 4),

    # --- AREA LOADING & DUMPING ---
    ("Area Loading & Dumping", "Operasional Front Loading", "Menunggu sinyal klakson/lampu operator excavator sebelum mundur posisi loading", "Safe", "Rendah", "Mundur setelah dipanggil dengan aman", 1),
    ("Area Loading & Dumping", "Operasional Front Loading", "Memasang rem parkir dan transmisi netral saat menerima muatan", "Safe", "Rendah", "Menjaga kestabilan unit selama proses loading", 2),
    ("Area Loading & Dumping", "Operasional Front Loading", "Manuver memotong antrean di front loading tanpa izin pengawas", "At-Risk", "Tinggi", "Menyebabkan kekacauan jalur lalu lintas front", 3),
    ("Area Loading & Dumping", "Operasional Front Loading", "Berhenti atau menunggu di bawah tebing galian yang rawan runtuh/longsor", "At-Risk", "Sangat Tinggi", "Bahaya tertimbun runtuhan batu/tanah tebing", 4),
    
    ("Area Loading & Dumping", "Manuver & Dumping Disposal", "Memastikan kondisi tanggul disposal kokoh dan tinggi minimal 1/2 roda terbesar", "Safe", "Rendah", "Tanggul pengaman mampu menahan dorongan ban", 1),
    ("Area Loading & Dumping", "Manuver & Dumping Disposal", "Mengikuti aba-aba dari pemandu dumping (spotter) dengan seksama", "Safe", "Rendah", "Mundur lurus tegak lurus tanggul sesuai panduan", 2),
    ("Area Loading & Dumping", "Manuver & Dumping Disposal", "Mundur dumping dengan kecepatan tinggi menabrak/melompati tanggul penahan", "At-Risk", "Sangat Tinggi", "Risiko unit terjungkal ke lereng disposal", 3),
    ("Area Loading & Dumping", "Manuver & Dumping Disposal", "Melakukan dumping di area retakan tanah atau bibir disposal yang labil tanpa spotter", "At-Risk", "Sangat Tinggi", "Risiko longsor disposal membawa unit jatuh", 4),

    # --- PERAWATAN & MAINTENANCE ---
    ("Perawatan & Maintenance", "Pengendalian Energi & Ganjal Ban", "Memasang wheel chock (ganjal ban) pada roda sebelum servis dilakukan", "Safe", "Rendah", "Mencegah pergerakan unit yang tidak diinginkan", 1),
    ("Perawatan & Maintenance", "Pengendalian Energi & Ganjal Ban", "Memutus saklar isolator baterai (battery isolator) saat pekerjaan mekanik besar", "Safe", "Rendah", "Mematikan aliran listrik utama sistem unit", 2),
    ("Perawatan & Maintenance", "Pengendalian Energi & Ganjal Ban", "Membongkar sistem bertekanan tinggi tanpa merilis tekanan sisa", "At-Risk", "Sangat Tinggi", "Semburan fluida hidrolik panas bertekanan tinggi", 3),
    ("Perawatan & Maintenance", "Pengendalian Energi & Ganjal Ban", "Tidak memasang ganjal roda pada unit yang sedang diperbaiki", "At-Risk", "Tinggi", "Unit berpotensi menggelinding saat rem dilepas", 4),
    
    ("Perawatan & Maintenance", "Bekerja di Bawah Unit & Attachment", "Menurunkan blade/vessel/bucket ke tanah atau memasang mechanical safety prop", "Safe", "Rendah", "Mengunci komponen hidrolik agar tidak turun sendiri", 1),
    ("Perawatan & Maintenance", "Bekerja di Bawah Unit & Attachment", "Bekerja di bawah bak dump hidrolik yang terangkat tanpa safety lock pin", "At-Risk", "Sangat Tinggi", "Bahaya terjepit fatal jika silinder hidrolik bocor/turun", 2),
    ("Perawatan & Maintenance", "Bekerja di Bawah Unit & Attachment", "Berdiri atau melintas di bawah bucket excavator yang terangkat", "At-Risk", "Sangat Tinggi", "Bahaya hydraulic drop seketika", 3),
    
    ("Perawatan & Maintenance", "Penggunaan Peralatan Kerja (Tools)", "Menggunakan perkakas tangan (tools) standar dan layak pakai (tidak retak/modifikasi)", "Safe", "Rendah", "Memilih ukuran kunci yang tepat tanpa selip", 1),
    ("Perawatan & Maintenance", "Penggunaan Peralatan Kerja (Tools)", "Menggunakan pelindung mata (safety glasses/face shield) saat menggerinda atau memukul", "Safe", "Rendah", "Mencegah serpihan gram besi masuk ke mata", 2),
    ("Perawatan & Maintenance", "Penggunaan Peralatan Kerja (Tools)", "Menggunakan kunci pas aus atau menyambung kunci dengan pipa tuas ilegal", "At-Risk", "Sedang", "Kunci dapat patah/terlepas mencederai mekanik", 3),
    ("Perawatan & Maintenance", "Penggunaan Peralatan Kerja (Tools)", "Melakukan pengelasan di dekat bahan mudah terbakar tanpa tabung APAR di dekatnya", "At-Risk", "Tinggi", "Bahaya percikan las memicu kebakaran bengkel", 4),

    # --- PEKERJAAN BERISIKO KHUSUS ---
    ("Pekerjaan Berisiko Khusus", "LOTO (Lock Out Tag Out)", "Melakukan uji coba saklar ON setelah LOTO terpasang (Zero Energy Verification)", "Safe", "Rendah", "Memastikan mesin benar-benar mati dan tak ada energi sisa", 3),
    ("Pekerjaan Berisiko Khusus", "LOTO (Lock Out Tag Out)", "Melepas gembok atau danger tag milik orang lain tanpa otorisasi formal", "At-Risk", "Sangat Tinggi", "Pelanggaran serius standar isolasi energi", 4),
    
    ("Pekerjaan Berisiko Khusus", "Bekerja di Ketinggian", "Memakai Full Body Harness double lanyard dan mengaitkan 100% tie-off ke anchor point kokoh", "Safe", "Rendah", "Perlindungan jatuh aktif dan teruji kapasitas 22 kN", 1),
    ("Pekerjaan Berisiko Khusus", "Bekerja di Ketinggian", "Memeriksa scaffolding memiliki tagging hijau (inspeksi aman) sebelum dinaiki", "Safe", "Rendah", "Scaffold kokoh dengan handrail lengkap", 2),
    ("Pekerjaan Berisiko Khusus", "Bekerja di Ketinggian", "Mengamankan perkakas kerja dengan tali pengikat (tool lanyard) agar tak jatuh", "Safe", "Rendah", "Mencegah bahaya benda jatuh (dropped objects)", 3),
    ("Pekerjaan Berisiko Khusus", "Bekerja di Ketinggian", "Bekerja di ketinggian > 1.8 meter tanpa perlindungan jatuh (fall protection)", "At-Risk", "Sangat Tinggi", "Risiko jatuh berakibat fatal atau cacat tetap", 4),
    ("Pekerjaan Berisiko Khusus", "Bekerja di Ketinggian", "Mengaitkan hook lanyard pada kabel listrik, pipa kecil, atau objek rapuh", "At-Risk", "Sangat Tinggi", "Titik anchor tidak mampu menahan beban kejut jatuh", 5),
    
    ("Pekerjaan Berisiko Khusus", "Lifting & Rigging", "Memeriksa sling dan shackle memiliki tag SWL (kapasitas aman) dan tidak cacat/putus", "Safe", "Rendah", "Peralatan rigger tersertifikasi dan terawat", 1),
    ("Pekerjaan Berisiko Khusus", "Lifting & Rigging", "Menggunakan tali pengendali (tag line) untuk mengarahkan muatan gantung dari jarak aman", "Safe", "Rendah", "Tangan tidak bersentuhan langsung dengan beban melayang", 2),
    ("Pekerjaan Berisiko Khusus", "Lifting & Rigging", "Berada atau melintas di bawah muatan yang sedang tergantung (suspended load)", "At-Risk", "Sangat Tinggi", "Risiko tertimpa langsung jika sling putus", 3),
    ("Pekerjaan Berisiko Khusus", "Lifting & Rigging", "Mengangkat beban melebihi kapasitas tabel angkat (load chart) crane", "At-Risk", "Sangat Tinggi", "Crane berisiko terguling atau boom patah", 4),
    
    ("Pekerjaan Berisiko Khusus", "Ruang Terbatas (Confined Space)", "Melakukan pengujian multi-gas atmosfer (O2, H2S, CO, LEL) sebelum masuk dan berkala", "Safe", "Rendah", "Memastikan kadar oksigen aman (19.5% - 23.5%)", 1),
    ("Pekerjaan Berisiko Khusus", "Ruang Terbatas (Confined Space)", "Menempatkan petugas standby (watchman) di luar pintu masuk selama pekerjaan berlangsung", "Safe", "Rendah", "Komunikasi aktif dan siap prosedur evakuasi", 2),
    ("Pekerjaan Berisiko Khusus", "Ruang Terbatas (Confined Space)", "Masuk ke ruang terbatas/tangki tanpa Izin Masuk (Confined Space Entry Permit)", "At-Risk", "Sangat Tinggi", "Bahaya asfiksia, keracunan gas, atau ledakan atmosfer", 3),
    ("Pekerjaan Berisiko Khusus", "Ruang Terbatas (Confined Space)", "Petugas standby meninggalkan pos saat pekerja masih berada di dalam tangki", "At-Risk", "Sangat Tinggi", "Kehilangan jalur penyelamatan darurat", 4),

    # --- FAKTOR MANUSIA ---
    ("Faktor Manusia", "Kesiapan Kerja & Fit to Work", "Melakukan self-check fit to work dan beristirahat cukup (minimal 6-8 jam) sebelum shift", "Safe", "Rendah", "Kondisi fisik prima dan siap konsentrasi penuh", 1),
    ("Faktor Manusia", "Kesiapan Kerja & Fit to Work", "Melapor jujur kepada atasan jika merasa sakit atau mengonsumsi obat yang bikin kantuk", "Safe", "Rendah", "Pengawas dapat mengatur penugasan non-kritis", 2),
    ("Faktor Manusia", "Kesiapan Kerja & Fit to Work", "Bekerja di area operasional dalam kondisi sakit kepala berat, demam, atau mabuk obat", "At-Risk", "Sangat Tinggi", "Konsentrasi terganggu berakibat insiden fatal", 3),
    
    ("Faktor Manusia", "Fokus, Rushing & Complacency", "Menjaga ritme kerja stabil dan tidak tergesa-gesa (rushing) saat menjalankan pekerjaan kritis", "Safe", "Rendah", "Kualitas keselamatan diutamakan di atas ketergesaan", 1),
    ("Faktor Manusia", "Fokus, Rushing & Complacency", "Rushing (tergesa-gesa) memotong langkah prosedur standar demi mempercepat waktu", "At-Risk", "Tinggi", "Langkah pemeriksaan terlewati", 2),
    ("Faktor Manusia", "Fokus, Rushing & Complacency", "Complacency (merasa terlalu ahli sehingga mengabaikan potensi bahaya yang sudah lazim)", "At-Risk", "Sedang", "Kurangnya kewaspadaan terhadap bahaya rutin", 3),
    ("Faktor Manusia", "Fokus, Rushing & Complacency", "Bercanda kasar / bergurau membahayakan (horseplay) di area operasional atau workshop", "At-Risk", "Sedang", "Mengalihkan fokus dan dapat mencelakakan rekan", 4),

    # --- KEPATUHAN PROSEDUR ---
    ("Kepatuhan Prosedur", "Pemeriksaan Pra-Operasi (P2H)", "Memeriksa fungsi kritis kemudi, rem utama, rem parkir, dan klakson secara fisik", "Safe", "Rendah", "P2H riil bukan sekadar centang kertas", 1),
    ("Kepatuhan Prosedur", "Pemeriksaan Pra-Operasi (P2H)", "Mengisi lembar ceklist P2H asal-asalan tanpa melakukan inspeksi keliling unit", "At-Risk", "Tinggi", "Kerusakan tersembunyi tidak terdeteksi", 2),
    ("Kepatuhan Prosedur", "Pemeriksaan Pra-Operasi (P2H)", "Tetap mengoperasikan unit yang memiliki temuan rem tidak pakem / lampu padam", "At-Risk", "Sangat Tinggi", "Unit kategori Danger To Operate tetap dipaksakan jalan", 3),
    
    ("Kepatuhan Prosedur", "Izin Kerja Khusus & SIMPER/KIMPER", "Memiliki SIMPER / KIMPER yang masih aktif sesuai kelas unit yang dioperasikan", "Safe", "Rendah", "Kompetensi pengemudi/operator terverifikasi K3", 1),
    ("Kepatuhan Prosedur", "Izin Kerja Khusus & SIMPER/KIMPER", "Mengoperasikan unit alat berat tanpa memiliki izin SIMPER/KIMPER yang sah", "At-Risk", "Sangat Tinggi", "Operasi tanpa wewenang dan kompetensi", 2),
    ("Kepatuhan Prosedur", "Izin Kerja Khusus & SIMPER/KIMPER", "Bekerja pada pekerjaan panas (hot work) tanpa lembar Permit yang ditandatangani pengawas", "At-Risk", "Tinggi", "Prosedur mitigasi kebakaran tidak divalidasi", 3),

    # --- KEPEMIMPINAN & INTERVENSI ---
    ("Kepemimpinan & Intervensi", "Stop Work Authority & Intervensi", "Menggunakan Hak Menghentikan Pekerjaan (Stop Work) saat melihat bahaya langsung", "Safe", "Rendah", "Tindakan berani menyelamatkan nyawa rekan kerja", 1),
    ("Kepemimpinan & Intervensi", "Stop Work Authority & Intervensi", "Memberikan intervensi / koreksi positif dengan sopan kepada rekan yang berisiko", "Safe", "Rendah", "Mengedukasi sesama rekan dengan komunikasi bersahabat", 2),
    ("Kepemimpinan & Intervensi", "Stop Work Authority & Intervensi", "Memberikan apresiasi langsung kepada pekerja yang tertib mematuhi keselamatan", "Safe", "Rendah", "Mendorong budaya keselamatan saling peduli", 3),
    ("Kepemimpinan & Intervensi", "Stop Work Authority & Intervensi", "Membiarkan tindakan tidak aman terjadi di depan mata tanpa menegur atau intervensi", "At-Risk", "Tinggi", "Sikap permisif terhadap bahaya merusak budaya K3", 4),
    
    ("Kepemimpinan & Intervensi", "Komunikasi & Briefing Keselamatan", "Memimpin safety talk / P5M harian dengan materi relevan dan diskusi 2 arah", "Safe", "Rendah", "Memastikan seluruh kru paham mitigasi bahaya hari itu", 1),
    ("Kepemimpinan & Intervensi", "Komunikasi & Briefing Keselamatan", "Memulai pekerjaan tanpa safety briefing atau tidak menyampaikan bahaya khusus area", "At-Risk", "Sedang", "Pekerja tidak waspada terhadap kondisi lingkungan kerja", 2),

    # --- PENGGUNAAN APD ---
    ("Penggunaan APD", "Kepatuhan APD Wajib & Khusus", "Menggunakan sarung tangan kerja yang sesuai bahaya (mekanikal/kimia/las)", "Safe", "Rendah", "Melindungi jari dan tangan dari cedera gores/jepit", 4),
    ("Penggunaan APD", "Kepatuhan APD Wajib & Khusus", "Menggunakan ear plug / ear muff saat bekerja di area bising melebihi 85 dBA", "Safe", "Rendah", "Melindungi pendengaran dari paparan kebisingan mesin", 5),
    ("Penggunaan APD", "Kepatuhan APD Wajib & Khusus", "Menggunakan APD yang sudah rusak, getas, atau kedaluwarsa (helm retak/rompi pudar)", "At-Risk", "Sedang", "Fungsi proteksi APD menurun drastis", 6),
    ("Penggunaan APD", "Kepatuhan APD Wajib & Khusus", "Melepas helm keselamatan di bawah area kerja bertingkat / crane beroperasi", "At-Risk", "Tinggi", "Bahaya fatal benturan benda jatuh dari atas", 7),

    # --- HOUSEKEEPING & LINGKUNGAN ---
    ("Housekeeping & Lingkungan", "Kerapihan Area & Pengelolaan Limbah", "Membuang limbah B3 (filter bekas, majun oli, aki) ke drum khusus berlabel B3", "Safe", "Rendah", "Mencegah kontaminasi tanah dan sumber air", 4),
    ("Housekeeping & Lingkungan", "Kerapihan Area & Pengelolaan Limbah", "Menutup rapat wadah bahan kimia dan menempatkannya di atas palet penampung (secondary containment)", "Safe", "Rendah", "Mencegah tumpahan bahan kimia beracun", 5),
    ("Housekeeping & Lingkungan", "Kerapihan Area & Pengelolaan Limbah", "Membuang sampah makanan / botol plastik sembarangan di jalur tambang", "At-Risk", "Sedang", "Mencemari lingkungan dan menarik satwa liar", 6),
    ("Housekeeping & Lingkungan", "Kerapihan Area & Pengelolaan Limbah", "Menyimpan tabung gas bertekanan (oksigen/asetilen) berdiri tanpa rantai pengaman", "At-Risk", "Tinggi", "Tabung gas dapat roboh dan katup meledak", 7),
]

inserted_count = 0
for cat_name, t_name, b_text, def_class, def_risk, g_note, sort in all_behaviors:
    c_id = cat_map.get(cat_name)
    t_id = topic_map.get((cat_name, t_name))
    
    if not c_id:
        print(f"Skipping: Category not found: {cat_name}")
        continue
        
    cursor.execute("""
        SELECT id FROM tbl_m_bbs_behavior 
        WHERE category_id = ? AND behavior_text = ?
    """, (c_id, b_text))
    existing = cursor.fetchone()
    
    if not existing:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_behavior (category_id, topic_id, behavior_text, default_classification, default_risk_level, guidance_note, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, 1, GETDATE())
        """, (c_id, t_id, b_text, def_class, def_risk, g_note, sort))
        inserted_count += 1

conn.commit()
print(f"Successfully inserted {inserted_count} new behaviors.")

# Check final summary
cursor.execute('''
    SELECT c.name, COUNT(b.id) as total_behaviors
    FROM tbl_m_bbs_category c
    LEFT JOIN tbl_m_bbs_behavior b ON c.id = b.category_id
    GROUP BY c.id, c.name
    ORDER BY c.id
''')
print("\nFinal Behavior Count per Category:")
for row in cursor.fetchall():
    print(f"- {row[0]}: {row[1]} butir perilaku")

cursor.close()
conn.close()
