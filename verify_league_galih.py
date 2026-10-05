import pyodbc
import math

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    nik = "23021900738"
    
    # Check targets
    cursor.execute("""
        SELECT target_hazard_report, target_inspeksi, target_safety_talk, target_observasi, target_coaching
        FROM vw_r_karyawan_jabatan_mapping_preview m
        JOIN vw_karyawan k ON m.karyawan_id = k.id_karyawan
        WHERE k.no_nik = ?
    """, (nik,))
    map_row = cursor.fetchone()
    hTar, insTar, stTar, obsTar, cTar = map_row
    
    # Roster onsite days
    # In Sept 2026 (30 days), roster: 2026-09-21 to 2026-11-06 -> 10 days onsite
    totalDays = 30
    onsiteDays = 10
    hasRoster = True
    rat = onsiteDays / totalDays
    
    def scale_target(base, r, days):
        if base == 0 or days == 0: return 0
        scaled = int(round(base * r))
        return max(scaled, 1)
        
    scaled_h = scale_target(hTar, rat, onsiteDays)
    scaled_i = scale_target(insTar, rat, onsiteDays)
    scaled_st = scale_target(stTar, rat, onsiteDays)
    scaled_o = scale_target(obsTar, rat, onsiteDays)
    scaled_c = scale_target(cTar, rat, onsiteDays)
    
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
    c_creator = cursor.fetchone()[0]
    cursor.execute("""
        SELECT COUNT(*) FROM tbl_t_coaching_participant cp 
        JOIN tbl_t_coaching c ON cp.coaching_id = c.id
        WHERE cp.nik = ? AND c.created_at >= '2026-09-01' AND c.created_at <= '2026-09-30 23:59:59' AND c.is_deleted = 0
    """, (nik,))
    c_part = cursor.fetchone()[0]
    act_c = c_creator + c_part

    def get_dot(act, tgt):
        if tgt == 0:
            return "W" if act > 0 else "-"
        rate = (act / tgt) * 100
        if rate >= 80: return "W"
        if rate >= 40: return "D"
        return "L"

    print("=== LEAGUE STATUS FOR GALIH DWI RESPATI ===")
    print(f"Hazard:      Target Base={hTar}, Scaled={scaled_h} | Actual={act_h} -> Form Dot: {get_dot(act_h, scaled_h)}")
    print(f"Inspeksi:    Target Base={insTar}, Scaled={scaled_i} | Actual={act_i} -> Form Dot: {get_dot(act_i, scaled_i)}")
    print(f"Safety Talk: Target Base={stTar}, Scaled={scaled_st} | Actual={act_st} -> Form Dot: {get_dot(act_st, scaled_st)}")
    print(f"Observasi:   Target Base={obsTar}, Scaled={scaled_o} | Actual={act_o} -> Form Dot: {get_dot(act_o, scaled_o)}")
    print(f"Coaching:    Target Base={cTar}, Scaled={scaled_c} | Actual={act_c} -> Form Dot: {get_dot(act_c, scaled_c)}")
    print(f"P5M:         (Excluded / Kecuali P5M)")
    
    totalTgtScaled = scaled_h + scaled_i + scaled_st + scaled_o + scaled_c
    totalActScaled = min(act_h, scaled_h) + min(act_i, scaled_i) + min(act_st, scaled_st) + min(act_o, scaled_o) + min(act_c, scaled_c)
    complianceScaled = (totalActScaled / totalTgtScaled) * 100
    
    totalTgtBase = hTar + insTar + stTar + obsTar + cTar
    totalActBase = min(act_h, hTar) + min(act_i, insTar) + min(act_st, stTar) + min(act_o, obsTar) + min(act_c, cTar)
    complianceBase = (totalActBase / totalTgtBase) * 100
    
    print(f"\nTotal Target (Scaled): {totalTgtScaled} | Total Actual (Capped): {totalActScaled} -> Compliance: {complianceScaled:.1f}%")
    print(f"Total Target (Base):   {totalTgtBase} | Total Actual (Capped): {totalActBase} -> Compliance: {complianceBase:.1f}%")

if __name__ == "__main__":
    main()
