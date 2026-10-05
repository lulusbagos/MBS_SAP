import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    try:
        conn = pyodbc.connect(conn_str, timeout=10)
        cursor = conn.cursor()
        
        nik = "23021900738"
        print(f"--- CHECKING NIK {nik} ---")
        
        # 1. Karyawan info
        sql_karyawan = """
        SELECT k.id_karyawan, k.no_nik, p.nama_lengkap, d.nama_departemen, j.nama_jabatan, 
               comp.nama_perusahaan, k.id_perusahaan, k.id_departemen, k.id_jabatan, k.status_aktif, k.tanggal_masuk
        FROM vw_karyawan k
        JOIN vw_personal p ON k.id_personal = p.id_personal
        LEFT JOIN vw_departemen d ON k.id_departemen = d.departemen_id
        LEFT JOIN vw_jabatan j ON k.id_jabatan = j.jabatan_id
        LEFT JOIN vw_perusahaan comp ON k.id_perusahaan = comp.perusahaan_id
        WHERE k.no_nik = ?
        """
        cursor.execute(sql_karyawan, (nik,))
        rows = cursor.fetchall()
        if not rows:
            print("Employee not found in vw_karyawan!")
            return
        
        for r in rows:
            print(f"Karyawan ID: {r.id_karyawan}")
            print(f"NIK: {r.no_nik}")
            print(f"Nama: {r.nama_lengkap}")
            print(f"Company: {r.nama_perusahaan} (ID: {r.id_perusahaan})")
            print(f"Dept: {r.nama_departemen} (ID: {r.id_departemen})")
            print(f"Jabatan: {r.nama_jabatan} (ID: {r.id_jabatan})")
            print(f"Status Aktif: {r.status_aktif}")
            print(f"Tanggal Masuk: {r.tanggal_masuk}")
            
            # Mapping
            sql_map = """
            SELECT target_hazard_report, target_inspeksi, target_safety_talk, target_observasi, target_coaching, nama_jabatan_standar, nama_jabatan_existing
            FROM vw_r_karyawan_jabatan_mapping_preview
            WHERE karyawan_id = ?
            """
            cursor.execute(sql_map, (r.id_karyawan,))
            map_row = cursor.fetchone()
            if map_row:
                print(f"Target SAP Mapping: Hazard={map_row.target_hazard_report}, Inspeksi={map_row.target_inspeksi}, SafetyTalk={map_row.target_safety_talk}, Observasi={map_row.target_observasi}, Coaching={map_row.target_coaching}")
                print(f"Jabatan Standar: {map_row.nama_jabatan_standar} | Existing: {map_row.nama_jabatan_existing}")
            else:
                print("No custom mapping found in vw_r_karyawan_jabatan_mapping_preview (defaults apply: Hazard=2, Inspeksi=1, ST=1, Obs=0, Coaching=0)")
        
        # Rosters for 2026-09
        sql_roster = """
        SELECT tipe_roster, awal_dinas, akhir_dinas FROM tbl_m_roster WHERE nik = ?
        """
        cursor.execute(sql_roster, (nik,))
        rosters = cursor.fetchall()
        print(f"Rosters count: {len(rosters)}")
        for ros in rosters:
            print(f"  Roster: {ros.tipe_roster} ({ros.awal_dinas} s/d {ros.akhir_dinas})")
            
        # Existing records in September 2026 (2026-09-01 to 2026-09-30)
        # 1. Hazard
        cursor.execute("SELECT COUNT(*) FROM tbl_t_hazard_report WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        hz_cnt = cursor.fetchone()[0]
        
        # 2. Inspection
        cursor.execute("SELECT COUNT(*) FROM tbl_t_inspection WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        insp_cnt = cursor.fetchone()[0]
        
        # 3. Safety Talk
        cursor.execute("SELECT COUNT(*) FROM tbl_t_safety_talk WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        st_cnt = cursor.fetchone()[0]
        
        # 4. Observation
        cursor.execute("SELECT COUNT(*) FROM tbl_t_observation WHERE nik = ? AND created_at >= '2026-09-01' AND created_at <= '2026-09-30 23:59:59' AND is_deleted = 0", (nik,))
        obs_cnt = cursor.fetchone()[0]
        
        # 5. Coaching (creator + participant)
        cursor.execute("SELECT COUNT(*) FROM tbl_t_coaching WHERE nik = ? AND created_at >= '2026-09-01' AND created_at <= '2026-09-30 23:59:59' AND is_deleted = 0", (nik,))
        coach_creator_cnt = cursor.fetchone()[0]
        cursor.execute("""
            SELECT COUNT(*) FROM tbl_t_coaching_participant cp 
            JOIN tbl_t_coaching c ON cp.coaching_id = c.id
            WHERE cp.nik = ? AND c.created_at >= '2026-09-01' AND c.created_at <= '2026-09-30 23:59:59' AND c.is_deleted = 0
        """, (nik,))
        coach_part_cnt = cursor.fetchone()[0]
        
        # 6. P5M
        cursor.execute("SELECT COUNT(*) FROM tbl_t_p5m WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        p5m_cnt = cursor.fetchone()[0]
        
        print("\n--- ACTUAL DATA SEPTEMBER 2026 ---")
        print(f"Hazard: {hz_cnt}")
        print(f"Inspeksi: {insp_cnt}")
        print(f"Safety Talk: {st_cnt}")
        print(f"Observasi: {obs_cnt}")
        print(f"Coaching: {coach_creator_cnt + coach_part_cnt} (Creator: {coach_creator_cnt}, Participant: {coach_part_cnt})")
        print(f"P5M: {p5m_cnt}")
        
    except Exception as e:
        print("Error:", e)

if __name__ == "__main__":
    main()
