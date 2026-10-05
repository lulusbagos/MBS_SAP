import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    niks = ["24051940986", "24041930970", "24051830994"]
    
    for nik in niks:
        print(f"\n==========================================")
        print(f"CHECKING NIK: {nik}")
        print(f"==========================================")
        
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
        r = cursor.fetchone()
        if not r:
            print("Employee not found in vw_karyawan!")
            continue
        
        print(f"ID: {r.id_karyawan} | NIK: {r.no_nik} | Nama: {r.nama_lengkap}")
        print(f"Company: {r.nama_perusahaan} (ID: {r.id_perusahaan})")
        print(f"Dept: {r.nama_departemen} (ID: {r.id_departemen}) | Jabatan: {r.nama_jabatan} (ID: {r.id_jabatan})")
        print(f"Status: {r.status_aktif} | Tgl Masuk: {r.tanggal_masuk}")
        
        # Mapping
        sql_map = """
        SELECT target_hazard_report, target_inspeksi, target_safety_talk, target_observasi, target_coaching, nama_jabatan_standar, nama_jabatan_existing
        FROM vw_r_karyawan_jabatan_mapping_preview
        WHERE karyawan_id = ?
        """
        cursor.execute(sql_map, (r.id_karyawan,))
        map_row = cursor.fetchone()
        if map_row:
            hTar, insTar, stTar, obsTar, cTar = map_row[0], map_row[1], map_row[2], map_row[3], map_row[4]
            print(f"Target SAP Mapping: Hazard={hTar}, Inspeksi={insTar}, SafetyTalk={stTar}, Observasi={obsTar}, Coaching={cTar}")
            print(f"Jabatan Standar: {map_row[5]} | Existing: {map_row[6]}")
        else:
            hTar, insTar, stTar, obsTar, cTar = 2, 1, 1, 0, 0
            print(f"Default Target Mapping: Hazard={hTar}, Inspeksi={insTar}, SafetyTalk={stTar}, Observasi={obsTar}, Coaching={cTar}")
            
        # Rosters
        sql_roster = "SELECT tipe_roster, awal_dinas, akhir_dinas FROM tbl_m_roster WHERE nik = ?"
        cursor.execute(sql_roster, (nik,))
        rosters = cursor.fetchall()
        print(f"Rosters count: {len(rosters)}")
        onsiteDays = 30
        hasRoster = False
        startOfMonth = "2026-09-01"
        endOfMonth = "2026-09-30"
        
        for ros in rosters:
            print(f"  Roster: {ros.tipe_roster} ({ros.awal_dinas} s/d {ros.akhir_dinas})")
            hasRoster = True
            # Compute overlap with Sept 2026
            # (simple check)
            
        # Existing records in September 2026
        cursor.execute("SELECT COUNT(*) FROM tbl_t_hazard_report WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        hz_cnt = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_inspection WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        insp_cnt = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_safety_talk WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        st_cnt = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_observation WHERE nik = ? AND created_at >= '2026-09-01' AND created_at <= '2026-09-30 23:59:59' AND is_deleted = 0", (nik,))
        obs_cnt = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_coaching WHERE nik = ? AND created_at >= '2026-09-01' AND created_at <= '2026-09-30 23:59:59' AND is_deleted = 0", (nik,))
        c_creat = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_coaching_participant cp JOIN tbl_t_coaching c ON cp.coaching_id = c.id WHERE cp.nik = ? AND c.created_at >= '2026-09-01' AND c.created_at <= '2026-09-30 23:59:59' AND c.is_deleted = 0", (nik,))
        c_part = cursor.fetchone()[0]
        coach_cnt = c_creat + c_part
        cursor.execute("SELECT COUNT(*) FROM tbl_t_p5m WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        p5m_cnt = cursor.fetchone()[0]
        
        print(f"Actual Sept 2026: Hazard={hz_cnt}, Inspeksi={insp_cnt}, SafetyTalk={st_cnt}, Observasi={obs_cnt}, Coaching={coach_cnt}, P5M={p5m_cnt}")

if __name__ == "__main__":
    main()
