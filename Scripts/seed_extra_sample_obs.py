import pyodbc
import json

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

cursor.execute("SELECT TOP 5 nik, nama, departemen, perusahaan, id_perusahaan FROM tbl_t_app_user WHERE nik != '24011950928'")
workers = cursor.fetchall()

extra_obs = [
    {
        "no": "BBS-202610-0004",
        "type": "SpecialCase",
        "tanggal": "2026-10-07",
        "waktu": "11:45",
        "observer_nik": "24011950928",
        "observer_nama": "MUHAMMAD ALFIAN YUSTIANDA",
        "observer_dept": "SYSTEM INTEGRATIONS",
        "observer_perusahaan_id": 1,
        "observer_perusahaan": "PT INDEXIM COALINDO",
        "site": "-0.941234, 117.876543",
        "area": "PIT - Unggul",
        "detil_lokasi": "FRONT PIT TIMUR BLOK C",
        "observed_nik": workers[0][0] if len(workers) > 0 else "2201019901",
        "observed_nama": workers[0][1] if len(workers) > 0 else "HENDRA WIJAYA",
        "observed_jabatan": "Juru Ledak / Blasting Crew",
        "observed_dept": workers[0][2] if len(workers) > 0 else "OPERATION",
        "observed_perusahaan_id": workers[0][4] if len(workers) > 0 else 1,
        "observed_perusahaan": workers[0][3] if len(workers) > 0 else "PT INDEXIM COALINDO",
        "category_id": 16,
        "category_name": "Peledakan & Handak",
        "topic_id": 16,
        "topic_name": "Radius Aman Peledakan & Evakuasi",
        "selected_behaviors_json": json.dumps([
            {"id": 120, "text": "Berada di luar batas radius aman peledakan (500m untuk manusia, 300m untuk alat)", "status": "Aman"},
            {"id": 121, "text": "Memastikan pos pemblokir jalan (blocker) terpasang pita barikade dan rambu dilarang melintas", "status": "Aman"}
        ]),
        "custom_behavior": None,
        "klasifikasi": "Aman",
        "kondisi_jalan": "Jalan akses tambang steril",
        "kondisi_kerja": "Persiapan peledakan overburden pit timur",
        "faktor_pemicu": "Kepatuhan prosedur SOP Blasting Safety",
        "tingkat_risiko": "Rendah",
        "deskripsi": "Kru peledakan dan pos blocker telah mensterilkan radius 500 meter dari titik tembak, pita barikade terpasang kokoh, dan radio silence dipatuhi sempurna sebelum aba-aba tembak.",
        "respons_pekerja": "Positif",
        "tindakan_dilakukan": "Apresiasi Langsung, Konfirmasi Radio Clearance",
        "catatan_coaching": "Koordinasi clearance sangat baik dan rapi. Seluruh unit dan personil berada di pos aman.",
        "foto_url": None
    },
    {
        "no": "BBS-202610-0005",
        "type": "Rutin",
        "tanggal": "2026-10-07",
        "waktu": "13:10",
        "observer_nik": "24011950928",
        "observer_nama": "MUHAMMAD ALFIAN YUSTIANDA",
        "observer_dept": "SYSTEM INTEGRATIONS",
        "observer_perusahaan_id": 1,
        "observer_perusahaan": "PT INDEXIM COALINDO",
        "site": "-0.925612, 117.898741",
        "area": "CPP-33 & ROM - Indexim",
        "detil_lokasi": "ROM STOCKPILE AREA D",
        "observed_nik": workers[1][0] if len(workers) > 1 else "2304018822",
        "observed_nama": workers[1][1] if len(workers) > 1 else "AGUS PRASETYO",
        "observed_jabatan": "Operator Dozer ROM",
        "observed_dept": workers[1][2] if len(workers) > 1 else "PRODUCTION",
        "observed_perusahaan_id": workers[1][4] if len(workers) > 1 else 1,
        "observed_perusahaan": workers[1][3] if len(workers) > 1 else "PT INDEXIM COALINDO",
        "category_id": 18,
        "category_name": "Pencegahan Kebakaran & Darurat",
        "topic_id": None,
        "topic_name": None,
        "selected_behaviors_json": json.dumps([
            {"id": 140, "text": "Melaporkan segera titik asap (hotspot) atau bau belerang pada timbunan batubara stockpile", "status": "Aman"},
            {"id": 141, "text": "Melakukan pemadaman hotspot dengan metode spreading (pembongkaran) dan pendinginan air", "status": "Aman"}
        ]),
        "custom_behavior": None,
        "klasifikasi": "Aman",
        "kondisi_jalan": "Lantai ROM stockpile padat",
        "kondisi_kerja": "Pengelolaan stockpile batubara kalori tinggi",
        "faktor_pemicu": "Kewaspadaan terhadap bahaya kebakaran spontan batubara",
        "tingkat_risiko": "Rendah",
        "deskripsi": "Operator dozer mendeteksi kepulan asap tipis di timbunan batubara ROM blok D dan langsung melakukan spreading serta memanggil unit water truck untuk pendinginan sebelum timbul lidah api.",
        "respons_pekerja": "Positif",
        "tindakan_dilakukan": "Apresiasi Respon Cepat, Spreading & Cooling Hotspot",
        "catatan_coaching": "Tindakan cepat operator dozer berhasil mencegah timbulnya nyala api dan polusi asap di area ROM.",
        "foto_url": None
    }
]

for obs in extra_obs:
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
        print(f"Inserted extra observation: {obs['no']}")

conn.commit()
cursor.close()
conn.close()
