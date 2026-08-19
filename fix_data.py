import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"
niks = [
    "24051830994",
    "24011950928",
    "23091840871",
    "24051940986",
    "24041930970"
]

def limit_records(cursor, table, nik, max_count):
    # Get all active records for the nik ordered by created_at desc (keep latest)
    cursor.execute(f"SELECT id FROM {table} WHERE nik = ? AND is_deleted = 0 ORDER BY created_at ASC", (nik,))
    rows = cursor.fetchall()
    
    if len(rows) > max_count:
        ids_to_delete = [r[0] for r in rows[max_count:]]
        placeholders = ",".join("?" * len(ids_to_delete))
        cursor.execute(f"UPDATE {table} SET is_deleted = 1 WHERE id IN ({placeholders})", ids_to_delete)
        return len(ids_to_delete)
    return 0

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    try:
        for nik in niks:
            print(f"Fixing records for NIK {nik}...")
            # Hazard limit to 1
            haz_deleted = limit_records(cursor, "tbl_t_hazard_report", nik, 1)
            # Inspection limit to 2
            insp_deleted = limit_records(cursor, "tbl_t_inspection", nik, 2)
            # Observation limit to 2
            obs_deleted = limit_records(cursor, "tbl_t_observation", nik, 2)
            # Coaching limit to 1
            coach_deleted = limit_records(cursor, "tbl_t_coaching", nik, 1)
            # Safety Talk limit to 4
            st_deleted = limit_records(cursor, "tbl_t_safety_talk", nik, 4)
            # P5M limit to 1
            p5m_deleted = limit_records(cursor, "tbl_t_p5m", nik, 1)
            
            print(f"  -> Deleted Hazard: {haz_deleted}, Obs: {obs_deleted}, Coach: {coach_deleted}, Insp: {insp_deleted}, ST: {st_deleted}, P5M: {p5m_deleted}")
            
        conn.commit()
        print("Data successfully updated!")
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
