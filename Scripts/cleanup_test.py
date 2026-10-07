import pyodbc

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cur = conn.cursor()
cur.execute("DELETE FROM tbl_m_bbs_behavior WHERE behavior_text LIKE '%Test%'")
cur.execute("DELETE FROM tbl_m_bbs_topic WHERE name LIKE '%Test%'")
cur.execute("DELETE FROM tbl_m_bbs_category WHERE name LIKE '%Test%'")
cur.execute("DELETE FROM tbl_t_bbs_observation WHERE observation_no LIKE '%TEST%'")
conn.commit()

cur.execute("SELECT COUNT(*) FROM tbl_m_bbs_category")
c_count = cur.fetchone()[0]
cur.execute("SELECT COUNT(*) FROM tbl_m_bbs_topic")
t_count = cur.fetchone()[0]
cur.execute("SELECT COUNT(*) FROM tbl_m_bbs_behavior")
b_count = cur.fetchone()[0]
print(f"Current master counts: Categories={c_count}, Topics={t_count}, Behaviors={b_count}")
