import pyodbc
import json

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

print("=" * 60)
print("       TEST VALIDASI CRUD BBS OBSERVATION & MASTER DATA")
print("=" * 60)

# 1. Verifikasi Master Data Exisiting
print("\n[1] VERIFIKASI DATA MASTER TERSEDIA:")
cursor.execute("SELECT COUNT(*) FROM tbl_m_bbs_category")
cat_count = cursor.fetchone()[0]
cursor.execute("SELECT COUNT(*) FROM tbl_m_bbs_topic")
topic_count = cursor.fetchone()[0]
cursor.execute("SELECT COUNT(*) FROM tbl_m_bbs_behavior")
bhv_count = cursor.fetchone()[0]
print(f"    - Kategori BBS       : {cat_count} data")
print(f"    - Topik Special Case : {topic_count} data")
print(f"    - Butir Perilaku     : {bhv_count} data")
assert cat_count > 0, "Kategori tidak boleh kosong"
assert topic_count > 0, "Topik tidak boleh kosong"
assert bhv_count > 0, "Butir perilaku tidak boleh kosong"

# 2. CRUD Transaksi Observasi BBS
print("\n[2] TEST CRUD TRANSAKSI OBSERVASI BBS (tbl_t_bbs_observation):")
test_no = "BBS-TEST-VERIFY-001"
behaviors_json = json.dumps([
    {"id": 1, "text": "Menggunakan helm & tali dagu terkancing", "status": "Aman"},
    {"id": 2, "text": "Menggunakan kacamata safety saat bekerja di area berdebu", "status": "Berisiko"}
])

# CREATE
cursor.execute("""
    INSERT INTO tbl_t_bbs_observation (
        observation_no, observation_type, tanggal, waktu, site, area, detil_lokasi,
        observer_nik, observer_nama, observer_dept, observer_perusahaan,
        observed_nik, observed_nama, observed_jabatan, observed_dept, observed_perusahaan,
        category_id, category_name, topic_id, topic_name, selected_behaviors_json,
        klasifikasi, tingkat_risiko, deskripsi, respons_pekerja, tindakan_dilakukan, catatan_coaching,
        is_deleted, created_at
    ) VALUES (
        ?, 'Rutin', CAST(GETDATE() AS DATE), '14:30', 'BBS Site', 'Port Area', 'Jetty 1 Dermaga Barat',
        'NIK-001', 'Safety Inspector', 'HSE', 'PT Indexim Coalindo',
        'NIK-TEST-99', 'Budi Santoso', 'Operator Rigger', 'Logistik', 'PT Indexim Coalindo',
        1, 'APD (Alat Pelindung Diri)', NULL, NULL, ?,
        'Berisiko', 'Sedang', 'Pekerja terlihat tidak mengancingkan helm saat di area dermaga',
        'Positif', 'Coaching Langsung, Koreksi Posisi/Metode', 'Pekerja memahami pentingnya tali dagu dan langsung memasangnya',
        0, GETDATE()
    )
""", (test_no, behaviors_json))
conn.commit()
print("    [CREATE] Berhasil insert transaksi baru!")

# READ
cursor.execute("""
    SELECT id, observation_no, observed_nama, klasifikasi, tingkat_risiko, is_deleted, selected_behaviors_json
    FROM tbl_t_bbs_observation
    WHERE observation_no = ?
""", (test_no,))
row = cursor.fetchone()
test_id = row[0]
parsed_json = json.loads(row[6])
print(f"    [READ]   Ditemukan ID={test_id}, No={row[1]}, Pekerja={row[2]}, Klasifikasi={row[3]}, Risiko={row[4]}, BehaviorsCount={len(parsed_json)}")
assert row[1] == test_no
assert row[3] == "Berisiko"

# UPDATE
cursor.execute("""
    UPDATE tbl_t_bbs_observation
    SET klasifikasi = 'Aman', tingkat_risiko = 'Rendah', catatan_coaching = 'Sudah diperbaiki dan patuh penuh', updated_at = GETDATE()
    WHERE id = ?
""", (test_id,))
conn.commit()

cursor.execute("SELECT klasifikasi, tingkat_risiko, catatan_coaching, updated_at FROM tbl_t_bbs_observation WHERE id = ?", (test_id,))
up_row = cursor.fetchone()
print(f"    [UPDATE] Berhasil update -> Klasifikasi={up_row[0]}, Risiko={up_row[1]}, Coaching='{up_row[2]}'")
assert up_row[0] == "Aman"
assert up_row[1] == "Rendah"

# SOFT DELETE
cursor.execute("UPDATE tbl_t_bbs_observation SET is_deleted = 1, updated_at = GETDATE() WHERE id = ?", (test_id,))
conn.commit()
cursor.execute("SELECT is_deleted FROM tbl_t_bbs_observation WHERE id = ?", (test_id,))
del_row = cursor.fetchone()
print(f"    [DELETE] Soft-delete berhasil -> is_deleted={del_row[0]}")
assert del_row[0] == True

# Cleanup test row
cursor.execute("DELETE FROM tbl_t_bbs_observation WHERE id = ?", (test_id,))
conn.commit()
print("    [CLEAN]  Data uji coba transaksi dibersihkan.")

# 3. CRUD Master Data
print("\n[3] TEST CRUD MASTER DATA BBS:")
# Category
cursor.execute("""
    INSERT INTO tbl_m_bbs_category (name, category_type, icon, color, description, sort_order, is_active, created_at)
    VALUES ('Kategori Test Unit', 'Both', 'bi-star', '#38bdf8', 'Deskripsi test', 99, 1, GETDATE())
""")
conn.commit()
cursor.execute("SELECT @@IDENTITY")
test_cat_id = int(cursor.fetchone()[0])
print(f"    [CATEGORY CREATE] Berhasil membuat Kategori ID={test_cat_id}")

cursor.execute("UPDATE tbl_m_bbs_category SET name = 'Kategori Test Diubah', is_active = 0 WHERE id = ?", (test_cat_id,))
conn.commit()
cursor.execute("SELECT name, is_active FROM tbl_m_bbs_category WHERE id = ?", (test_cat_id,))
c_row = cursor.fetchone()
print(f"    [CATEGORY UPDATE] Berhasil update -> Name='{c_row[0]}', Active={c_row[1]}")
assert c_row[0] == "Kategori Test Diubah"

# Topic
cursor.execute("""
    INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
    VALUES (?, 'Topik Test Unit', 'bi-tag', 'Deskripsi topik', 99, 1, GETDATE())
""", (test_cat_id,))
conn.commit()
cursor.execute("SELECT @@IDENTITY")
test_top_id = int(cursor.fetchone()[0])
print(f"    [TOPIC CREATE]    Berhasil membuat Topik ID={test_top_id}")

cursor.execute("UPDATE tbl_m_bbs_topic SET name = 'Topik Test Diubah' WHERE id = ?", (test_top_id,))
conn.commit()
cursor.execute("SELECT name FROM tbl_m_bbs_topic WHERE id = ?", (test_top_id,))
t_row = cursor.fetchone()
print(f"    [TOPIC UPDATE]    Berhasil update -> Name='{t_row[0]}'")
assert t_row[0] == "Topik Test Diubah"

# Behavior
cursor.execute("""
    INSERT INTO tbl_m_bbs_behavior (category_id, topic_id, behavior_text, default_classification, default_risk_level, guidance_note, sort_order, is_active, created_at)
    VALUES (?, ?, 'Perilaku Aman Test', 'Safe', 'Rendah', 'Panduan observasi test', 99, 1, GETDATE())
""", (test_cat_id, test_top_id))
conn.commit()
cursor.execute("SELECT @@IDENTITY")
test_bhv_id = int(cursor.fetchone()[0])
print(f"    [BEHAVIOR CREATE] Berhasil membuat Behavior ID={test_bhv_id}")

cursor.execute("UPDATE tbl_m_bbs_behavior SET behavior_text = 'Perilaku Aman Diubah', default_classification = 'Both' WHERE id = ?", (test_bhv_id,))
conn.commit()
cursor.execute("SELECT behavior_text, default_classification FROM tbl_m_bbs_behavior WHERE id = ?", (test_bhv_id,))
b_row = cursor.fetchone()
print(f"    [BEHAVIOR UPDATE] Berhasil update -> Text='{b_row[0]}', Class={b_row[1]}")
assert b_row[0] == "Perilaku Aman Diubah"
assert b_row[1] == "Both"

# Clean up
cursor.execute("DELETE FROM tbl_m_bbs_behavior WHERE id = ?", (test_bhv_id,))
cursor.execute("DELETE FROM tbl_m_bbs_topic WHERE id = ?", (test_top_id,))
cursor.execute("DELETE FROM tbl_m_bbs_category WHERE id = ?", (test_cat_id,))
conn.commit()
print("    [CLEAN]           Data uji coba master dibersihkan.")

print("\n" + "=" * 60)
print("KESIMPULAN: SELURUH OPERASI CRUD BERFUNGSI SEMPURNA 100%!")
print("=" * 60)
