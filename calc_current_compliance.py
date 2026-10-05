import pyodbc
import datetime

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    niks = ["24051940986", "24041930970", "24051830994"]
    
    for nik in niks:
        print(f"\n--- NIK {nik} ---")
        # Employee name
        cursor.execute("SELECT p.nama_lengkap, d.nama_departemen FROM vw_karyawan k JOIN vw_personal p ON k.id_personal = p.id_personal LEFT JOIN vw_departemen d ON k.id_departemen = d.departemen_id WHERE k.no_nik = ?", (nik,))
        row = cursor.fetchone()
        name = row[0]
        dept = row[1]
        print(f"Name: {name} | Dept: {dept}")
        
        # Mappings
        cursor.execute("SELECT target_hazard_report, target_inspeksi, target_safety_talk, target_observasi, target_coaching FROM vw_r_karyawan_jabatan_mapping_preview m JOIN vw_karyawan k ON m.karyawan_id = k.id_karyawan WHERE k.no_nik = ?", (nik,))
        map_row = cursor.fetchone()
        hTar, insTar, stTar, obsTar, cTar = map_row[0], map_row[1], map_row[2], map_row[3], map_row[4]
        
        # Rosters
        cursor.execute("SELECT tipe_roster, awal_dinas, akhir_dinas FROM tbl_m_roster WHERE nik = ?", (nik,))
        rosters = cursor.fetchall()
        
        startOfMonth = datetime.date(2026, 9, 1)
        endOfMonth = datetime.date(2026, 9, 30)
        totalDays = 30
        
        computedOnsite = 0
        hasRoster = False
        for r in rosters:
            hasRoster = True
            if r.tipe_roster == "TUGAS":
                continue
            overlapStart = max(r.awal_dinas, startOfMonth)
            overlapEnd = min(r.akhir_dinas, endOfMonth)
            if overlapStart <= overlapEnd:
                computedOnsite += (overlapEnd - overlapStart).days + 1
                
        onsiteDays = computedOnsite if hasRoster else totalDays
        ratio = onsiteDays / totalDays if hasRoster else 1.0
        
        def scale_tgt(base):
            if base == 0 or onsiteDays == 0: return 0
            scaled = int(round(base * ratio))
            return max(scaled, 1)
            
        scaled_h = scale_tgt(hTar)
        scaled_i = scale_tgt(insTar)
        scaled_st = scale_tgt(stTar)
        scaled_o = scale_tgt(obsTar)
        scaled_c = scale_tgt(cTar)
        
        # Actuals
        cursor.execute("SELECT COUNT(*) FROM tbl_t_hazard_report WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        act_h = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_inspection WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        act_i = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_safety_talk WHERE nik = ? AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30' AND is_deleted = 0", (nik,))
        act_st = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_observation WHERE nik = ? AND created_at >= '2026-09-01' AND created_at <= '2026-09-30 23:59:59' AND is_deleted = 0", (nik,))
        act_o = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_coaching WHERE nik = ? AND created_at >= '2026-09-01' AND created_at <= '2026-09-30 23:59:59' AND is_deleted = 0", (nik,))
        c_creat = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM tbl_t_coaching_participant cp JOIN tbl_t_coaching c ON cp.coaching_id = c.id WHERE cp.nik = ? AND c.created_at >= '2026-09-01' AND c.created_at <= '2026-09-30 23:59:59' AND c.is_deleted = 0", (nik,))
        c_part = cursor.fetchone()[0]
        act_c = c_creat + c_part
        
        totalTgt = scaled_h + scaled_i + scaled_st + scaled_o + scaled_c
        totalAct = min(act_h, scaled_h) + min(act_i, scaled_i) + min(act_st, scaled_st) + min(act_o, scaled_o) + min(act_c, scaled_c)
        compliance = (totalAct / totalTgt * 100) if totalTgt > 0 else 0
        
        print(f"Onsite Days: {onsiteDays}/{totalDays} (Ratio: {ratio:.3f})")
        print(f"Target Scaled: H={scaled_h}, I={scaled_i}, ST={scaled_st}, O={scaled_o}, C={scaled_c} | Total={totalTgt}")
        print(f"Target Base:   H={hTar}, I={insTar}, ST={stTar}, O={obsTar}, C={cTar} | Total={hTar+insTar+stTar+obsTar+cTar}")
        print(f"Actual Now:    H={act_h}, I={act_i}, ST={act_st}, O={act_o}, C={act_c} | Total Capped={totalAct}")
        print(f"Current Compliance: {compliance:.1f}%")

if __name__ == "__main__":
    main()
