import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

niks = [
    "24051830994",
    "23091840871",
    "24011950928",
    "24051940986",
    "24041930970"
]

targets = {
    "tbl_t_hazard_report": 1,
    "tbl_t_observation": 2,
    "tbl_t_coaching": 1,
    "tbl_t_safety_talk": 3
}

def fix_records(cursor, table, nik, max_count):
    # Set all records for this nik to deleted first
    cursor.execute(f"UPDATE {table} SET is_deleted = 1 WHERE nik = ?", (nik,))
    
    # Get the top N latest records (which are in August)
    cursor.execute(f"SELECT TOP (?) id FROM {table} WHERE nik = ? ORDER BY created_at DESC", (max_count, nik))
    rows = cursor.fetchall()
    
    restored = 0
    if rows:
        ids_to_restore = [r[0] for r in rows]
        placeholders = ",".join("?" * len(ids_to_restore))
        cursor.execute(f"UPDATE {table} SET is_deleted = 0 WHERE id IN ({placeholders})", ids_to_restore)
        restored = len(ids_to_restore)
        
    return restored

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    try:
        for nik in niks:
            print(f"Fixing records for NIK {nik}...")
            res = {}
            for table, count in targets.items():
                restored = fix_records(cursor, table, nik, count)
                res[table] = restored
                
            print(f"  -> Restored: Hazard={res['tbl_t_hazard_report']}, Obs={res['tbl_t_observation']}, Coach={res['tbl_t_coaching']}, ST={res['tbl_t_safety_talk']}")
            
        conn.commit()
        print("Data successfully updated!")
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
