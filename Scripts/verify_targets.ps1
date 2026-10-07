$connStr = 'Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;'
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "
SELECT k.no_nik, v.nama_jabatan_standar, v.kategori_pengawas, v.target_hazard_report, v.target_inspeksi, v.target_safety_talk, v.target_observasi, v.target_coaching
FROM dbo.vw_r_karyawan_jabatan_mapping_preview v
JOIN ONE_DB_MITRA.dbo.tbl_t_karyawan k ON v.karyawan_id = k.id_karyawan
WHERE k.no_nik IN ('25022021183', '25051771130', '23062940814')
"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output "NIK: $($reader['no_nik']) | Jabatan: $($reader['nama_jabatan_standar']) | Kategori: $($reader['kategori_pengawas']) | H: $($reader['target_hazard_report']), I: $($reader['target_inspeksi']), ST: $($reader['target_safety_talk']), O: $($reader['target_observasi']), C: $($reader['target_coaching'])"
}
$reader.Close()
$conn.Close()
