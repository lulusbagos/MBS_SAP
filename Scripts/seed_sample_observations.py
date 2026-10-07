import pyodbc
import json
from datetime import datetime, date

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

# Get a couple of real workers for realistic observed data
cursor.execute("SELECT TOP 5 nik, nama, 'Operator / Karyawan' as jabatan, departemen, perusahaan, id_perusahaan FROM tbl_t_app_user WHERE nik != '24011950928'")
workers = cursor.fetchall()
print(f"Loaded {len(workers)} reference workers.")

# Observations to insert
observations = [
    {
        "no": "BBS-202610-0001",
        "type": "Rutin",
        "tanggal": "2026-10-06",
        "waktu": "09:15",
        "observer_nik": "24011950928",
        "observer_nama": "MUHAMMAD ALFIAN YUSTIANDA",
        "observer_dept": "SYSTEM INTEGRATIONS",
        "observer_perusahaan_id": 1,
        "observer_perusahaan": "PT INDEXIM COALINDO",
        "site": "-0.923412, 117.892341",
        "area": "Road Coal Hauling (Jl. Nusantara)",
        "detil_lokasi": "KM 18 JALUR HAULING UTAMA",
        "observed_nik": workers[0][0] if len(workers) > 0 else "2201019901",
        "observed_nama": workers[0][1] if len(workers) > 0 else "AHMAD FAUZI",
        "observed_jabatan": workers[0][2] if len(workers) > 0 else "Driver Dump Truck",
        "observed_dept": workers[0][3] if len(workers) > 0 else "HAULING",
        "observed_perusahaan_id": workers[0][5] if len(workers) > 0 else 1,
        "observed_perusahaan": workers[0][4] if len(workers) > 0 else "PT INDEXIM COALINDO",
        "category_id": 1,
        "category_name": "Mengemudi & Hauling",
        "topic_id": None,
        "topic_name": None,
        "selected_behaviors_json": json.dumps([
            {"id": 1, "text": "Mengemudi sesuai batas kecepatan yang ditentukan", "status": "Aman"},
            {"id": 2, "text": "Menjaga jarak aman minimal sesuai prosedur", "status": "Aman"},
            {"id": 3, "text": "Fokus penuh ke jalan dan spion", "status": "Aman"}
        ]),
        "custom_behavior": None,
        "klasifikasi": "Aman",
        "kondisi_jalan": "Kering, sedikit berdebu",
        "kondisi_kerja": "Lalu lintas hauling normal lancar",
        "faktor_pemicu": "Kesadaran K3 dan kepatuhan rambu kecepatan",
        "tingkat_risiko": "Rendah",
        "deskripsi": "Driver mengemudikan unit Dump Truck dengan kecepatan konstan 45 km/jam di jalur hauling KM 18, menjaga jarak aman lebih dari 50 meter dengan unit di depannya dan disiplin lajur kiri.",
        "respons_pekerja": "Positif",
        "tindakan_dilakukan": "Apresiasi Langsung, Reinforcement Positif",
        "catatan_coaching": "Memberikan apresiasi dan terima kasih atas kedisiplinan menjaga kecepatan dan jarak aman di jalur hauling.",
        "foto_url": None
    },
    {
        "no": "BBS-202610-0002",
        "type": "SpecialCase",
        "tanggal": "2026-10-06",
        "waktu": "14:30",
        "observer_nik": "24011950928",
        "observer_nama": "MUHAMMAD ALFIAN YUSTIANDA",
        "observer_dept": "SYSTEM INTEGRATIONS",
        "observer_perusahaan_id": 1,
        "observer_perusahaan": "PT INDEXIM COALINDO",
        "site": "-0.931245, 117.884123",
        "area": "Power Plant & Workshop - Indexim",
        "detil_lokasi": "BAY 4 WORKSHOP UTAMA",
        "observed_nik": workers[1][0] if len(workers) > 1 else "2304018822",
        "observed_nama": workers[1][1] if len(workers) > 1 else "BAMBANG IRAWAN",
        "observed_jabatan": workers[1][2] if len(workers) > 1 else "Mekanik Alat Berat",
        "observed_dept": workers[1][3] if len(workers) > 1 else "PLANT",
        "observed_perusahaan_id": workers[1][5] if len(workers) > 1 else 1,
        "observed_perusahaan": workers[1][4] if len(workers) > 1 else "PT INDEXIM COALINDO",
        "category_id": 5,
        "category_name": "Pekerjaan Berisiko Khusus",
        "topic_id": 12,
        "topic_name": "LOTO (Lock Out Tag Out)",
        "selected_behaviors_json": json.dumps([
            {"id": 10, "text": "Melakukan servis/perbaikan tanpa memasang padlock LOTO", "status": "Berisiko"},
            {"id": 11, "text": "Memasang wheel chock (ganjal ban) pada roda sebelum servis dilakukan", "status": "Aman"}
        ]),
        "custom_behavior": None,
        "klasifikasi": "Berisiko",
        "kondisi_jalan": "Lantai workshop beton",
        "kondisi_kerja": "Pekerjaan servis kelistrikan alternator",
        "faktor_pemicu": "Terburu-buru ingin cepat selesai",
        "tingkat_risiko": "Tinggi",
        "deskripsi": "Mekanik sedang membuka cover alternator unit Dozer tanpa memasang gembok LOTO pribadi dan saklar isolator baterai belum diputus.",
        "respons_pekerja": "Positif",
        "tindakan_dilakukan": "Stop Work Langsung, Coaching LOTO, Pemasangan Padlock",
        "catatan_coaching": "Mekanik menghentikan pekerjaan, mengambil gembok LOTO pribadi, memasangnya pada battery isolator, dan melakukan uji zero-energy sebelum melanjutkan pekerjaan.",
        "foto_url": None
    },
    {
        "no": "BBS-202610-0003",
        "type": "Rutin",
        "tanggal": "2026-10-07",
        "waktu": "10:20",
        "observer_nik": "24011950928",
        "observer_nama": "MUHAMMAD ALFIAN YUSTIANDA",
        "observer_dept": "SYSTEM INTEGRATIONS",
        "observer_perusahaan_id": 1,
        "observer_perusahaan": "PT INDEXIM COALINDO",
        "site": "-0.918734, 117.901245",
        "area": "Port/Jetty - Indexim",
        "detil_lokasi": "DERMAGA 2 WEST BERTH",
        "observed_nik": workers[2][0] if len(workers) > 2 else "2105017733",
        "observed_nama": workers[2][1] if len(workers) > 2 else "DEDI SURYADI",
        "observed_jabatan": workers[2][2] if len(workers) > 2 else "Operator Ship Loader",
        "observed_dept": workers[2][3] if len(workers) > 2 else "PORT",
        "observed_perusahaan_id": workers[2][5] if len(workers) > 2 else 1,
        "observed_perusahaan": workers[2][4] if len(workers) > 2 else "PT INDEXIM COALINDO",
        "category_id": 9,
        "category_name": "Penggunaan APD",
        "topic_id": None,
        "topic_name": None,
        "selected_behaviors_json": json.dumps([
            {"id": 20, "text": "Menggunakan APD lengkap sesuai matriks bahaya area", "status": "Aman"},
            {"id": 21, "text": "Menggunakan rompi reflektif (high-vis) yang bersih dan berpendar terang", "status": "Aman"}
        ]),
        "custom_behavior": None,
        "klasifikasi": "Aman",
        "kondisi_jalan": "Area trestle conveyor beton",
        "kondisi_kerja": "Operasional loading batubara ke tongkang",
        "faktor_pemicu": "Disiplin pemakaian APD di area perairan dan dermaga",
        "tingkat_risiko": "Rendah",
        "deskripsi": "Operator ship loader dan kru dermaga terpantau mengenakan life vest, helm safety berchin strap, rompi high-vis, dan safety boots saat bertugas di tepi dermaga.",
        "respons_pekerja": "Positif",
        "tindakan_dilakukan": "Apresiasi Langsung, Reinforcement Budaya K3",
        "catatan_coaching": "Kru sangat memahami bahaya jatuh ke air dan secara konsisten memakai perlengkapan keselamatan perairan dengan baik.",
        "foto_url": None
    }
]

for obs in observations:
    cursor.execute("SELECT id FROM tbl_t_bbs_observation WHERE observation_no = ?", (obs["no"],))
    exists = cursor.fetchone()
    if not exists:
        cursor.execute("""
            INSERT INTO tbl_t_bbs_observation (
                observation_no, observation_type, tanggal, waktu,
                observer_nik, observer_nama, observer_dept, observer_perusahaan_id, observer_perusahaan,
                site, area, detil_lokasi,
                observed_nik, observed_nama, observed_jabatan, observed_dept, observed_perusahaan_id, observed_perusahaan,
                category_id, category_name, topic_id, topic_name,
                selected_behaviors_json, custom_behavior,
                klasifikasi, kondisi_jalan, kondisi_kerja, faktor_pemicu, tingkat_risiko,
                deskripsi, respons_pekerja, tindakan_dilakukan, catatan_coaching, foto_url,
                is_deleted, created_at
            ) VALUES (
                ?, ?, ?, ?,
                ?, ?, ?, ?, ?,
                ?, ?, ?,
                ?, ?, ?, ?, ?, ?,
                ?, ?, ?, ?,
                ?, ?,
                ?, ?, ?, ?, ?,
                ?, ?, ?, ?, ?,
                0, GETDATE()
            )
        """, (
            obs["no"], obs["type"], obs["tanggal"], obs["waktu"],
            obs["observer_nik"], obs["observer_nama"], obs["observer_dept"], obs["observer_perusahaan_id"], obs["observer_perusahaan"],
            obs["site"], obs["area"], obs["detil_lokasi"],
            obs["observed_nik"], obs["observed_nama"], obs["observed_jabatan"], obs["observed_dept"], obs["observed_perusahaan_id"], obs["observed_perusahaan"],
            obs["category_id"], obs["category_name"], obs["topic_id"], obs["topic_name"],
            obs["selected_behaviors_json"], obs["custom_behavior"],
            obs["klasifikasi"], obs["kondisi_jalan"], obs["kondisi_kerja"], obs["faktor_pemicu"], obs["tingkat_risiko"],
            obs["deskripsi"], obs["respons_pekerja"], obs["tindakan_dilakukan"], obs["catatan_coaching"], obs["foto_url"]
        ))
        print(f"Inserted observation: {obs['no']} ({obs['klasifikasi']})")

conn.commit()

cursor.execute("SELECT COUNT(*), SUM(CASE WHEN klasifikasi='Aman' THEN 1 ELSE 0 END), SUM(CASE WHEN klasifikasi='Berisiko' THEN 1 ELSE 0 END) FROM tbl_t_bbs_observation WHERE is_deleted = 0")
row = cursor.fetchone()
print(f"\nTotal Active BBS Observations: {row[0]} (Aman: {row[1]}, Berisiko: {row[2]})")

cursor.close()
conn.close()
