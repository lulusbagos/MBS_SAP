import psycopg2
import pandas as pd

conn_str = "host='172.16.1.96' port='5432' dbname='sysinteg_indexsafe2' user='postgres' password='index.123'"
try:
    conn = psycopg2.connect(conn_str)
    
    query = """
    SELECT company, COUNT(*)
    FROM p2h_trans pht
    LEFT JOIN vehicle_masters vm ON vm.id = pht.vehicle_id
    WHERE vm.code = 'IR-020012'
    GROUP BY company
    """
    df = pd.read_sql(query, conn)
    print("Companies for IR-020012 in Postgres:")
    print(df)
    
except Exception as e:
    print("Error:", e)
