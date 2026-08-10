import pyodbc
import pandas as pd
import datetime

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes')
query = """
SELECT no_lambung, COUNT(*) as count 
FROM tbl_t_p2h_report 
WHERE no_lambung LIKE '%02%12%' AND tanggal >= ?
GROUP BY no_lambung
"""
df = pd.read_sql(query, conn, params=[datetime.datetime.now() - datetime.timedelta(days=30)])
print("Variants in last 1 month in SQL Server:")
print(df)
