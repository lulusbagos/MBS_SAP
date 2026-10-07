$connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

$nik = "511231470"

# 1. Karyawan Profile
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT TOP 1 v.no_nik, p.nama_lengkap, comp.nama_perusahaan, d.nama_departemen, j.nama_jabatan
FROM vw_karyawan v
LEFT JOIN vw_personal p ON v.id_personal = p.id_personal
LEFT JOIN vw_perusahaan comp ON v.id_perusahaan = comp.id_perusahaan
LEFT JOIN vw_departemen d ON v.id_departemen = d.id_departemen
LEFT JOIN vw_jabatan j ON v.id_jabatan = j.id_jabatan
WHERE v.no_nik = '$nik'
"@
$reader = $cmd.ExecuteReader()
if ($reader.Read()) {
    Write-Output "PROFILE:"
    Write-Output "NIK        : $($reader['no_nik'])"
    Write-Output "Nama       : $($reader['nama_lengkap'])"
    Write-Output "Perusahaan : $($reader['nama_perusahaan'])"
    Write-Output "Departemen : $($reader['nama_departemen'])"
    Write-Output "Jabatan    : $($reader['nama_jabatan'])"
}
$reader.Close()

# 2. Hazard details
$cmdHaz = $conn.CreateCommand()
$cmdHaz.CommandText = @"
SELECT id, tanggal, waktu, area, lokasi, detil_lokasi, temuan, tindakan_perbaikan, status_temuan, created_at
FROM tbl_t_hazard_report
WHERE nik = '$nik' AND is_deleted = 0
ORDER BY tanggal DESC, waktu DESC
"@
$daHaz = New-Object System.Data.SqlClient.SqlDataAdapter($cmdHaz)
$dtHaz = New-Object System.Data.DataTable
$daHaz.Fill($dtHaz) | Out-Null

Write-Output "`nHAZARD REPORT AUDIT (Total: $($dtHaz.Rows.Count)):"
$dupTemuan = $dtHaz.Rows | Group-Object { $_.temuan.ToString().Trim().ToLower() } | Where-Object { $_.Count -gt 1 }
Write-Output "Jumlah teks temuan yang sama berulang: $($dupTemuan.Count)"
foreach ($g in $dupTemuan) {
    Write-Output "  -> Text: '$($g.Name)' ($($g.Count) kali)"
    foreach ($r in $g.Group) {
        $t = [DateTime]$r['tanggal']
        Write-Output "     [ID: $($r['id'])] Tgl: $($t.ToString('yyyy-MM-dd')) $($r['waktu']) | Lokasi: $($r['lokasi']) ($($r['detil_lokasi'])) | Created: $($r['created_at'])"
    }
}

# Duplicate by Tanggal + Waktu
$dupTimeHaz = $dtHaz.Rows | Group-Object { 
    $t = [DateTime]$_['tanggal']
    "$($t.ToString('yyyy-MM-dd')) $($_.waktu)"
} | Where-Object { $_.Count -gt 1 }
Write-Output "Jumlah hazard dengan Tanggal & Waktu Persis Sama: $($dupTimeHaz.Count)"
foreach ($g in $dupTimeHaz) {
    Write-Output "  -> Waktu Sama: $($g.Name) ($($g.Count) laporan)"
    foreach ($r in $g.Group) {
        Write-Output "     [ID: $($r['id'])] Temuan: $($r['temuan'])"
    }
}

# 3. Inspection details
$cmdInsp = $conn.CreateCommand()
$cmdInsp.CommandText = @"
SELECT id, tanggal, waktu, jenis_inspeksi, area, lokasi, detil_lokasi, pja, nik_pja, created_at
FROM tbl_t_inspection
WHERE nik = '$nik' AND is_deleted = 0
ORDER BY tanggal DESC, waktu DESC
"@
$daInsp = New-Object System.Data.SqlClient.SqlDataAdapter($cmdInsp)
$dtInsp = New-Object System.Data.DataTable
$daInsp.Fill($dtInsp) | Out-Null

Write-Output "`nINSPECTION REPORT AUDIT (Total: $($dtInsp.Rows.Count)):"
# Same tanggal + same detil_lokasi (e.g. same front unit)
$dupInspFront = $dtInsp.Rows | Group-Object { 
    $t = [DateTime]$_['tanggal']
    "$($t.ToString('yyyy-MM-dd')) | $($_.lokasi.ToString().Trim().ToLower()) | $($_.detil_lokasi.ToString().Trim().ToLower())"
} | Where-Object { $_.Count -gt 1 }
Write-Output "Jumlah inspeksi di Tanggal + Lokasi/Front Persis Sama: $($dupInspFront.Count)"
foreach ($g in $dupInspFront) {
    Write-Output "  -> $($g.Name) ($($g.Count) kali pada hari yang sama):"
    foreach ($r in $g.Group) {
        Write-Output "     [ID: $($r['id'])] Jam: $($r['waktu']) | PJA: $($r['pja']) ($($r['nik_pja'])) | Created: $($r['created_at'])"
    }
}

# Same Tanggal & Waktu persis
$dupTimeInsp = $dtInsp.Rows | Group-Object { 
    $t = [DateTime]$_['tanggal']
    "$($t.ToString('yyyy-MM-dd')) $($_.waktu)"
} | Where-Object { $_.Count -gt 1 }
Write-Output "Jumlah inspeksi dengan Tanggal & Waktu Persis Sama: $($dupTimeInsp.Count)"
foreach ($g in $dupTimeInsp) {
    Write-Output "  -> Waktu Sama: $($g.Name) ($($g.Count) laporan):"
    foreach ($r in $g.Group) {
        Write-Output "     [ID: $($r['id'])] Lokasi: $($r['lokasi']) ($($r['detil_lokasi']))"
    }
}

$conn.Close()
