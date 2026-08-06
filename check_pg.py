import psycopg2

conn_str = "host=172.16.1.96 port=5432 dbname=sysinteg_indexsafe2 user=postgres password=index.123"
try:
    conn = psycopg2.connect(conn_str)
    cur = conn.cursor()
    
    query = "SELECT date, time, title, employee_name, employee_nik FROM vw_safetytalkdetail WHERE date = '2026-11-07'"
    cur.execute(query)
    rows = cur.fetchall()
    
    print(f"Found {len(rows)} rows for 2026-11-07.")
    for i, row in enumerate(rows[:10]):
        print(f"{i+1}. Date: {row[0]}, Time: {row[1]}, Title: {row[2]}, Name: {row[3]}, NIK: {row[4]}")
        
    cur.close()
    conn.close()
except Exception as e:
    print(f"Error: {e}")
