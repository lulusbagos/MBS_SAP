$connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

$nik = "511231470"

# 1. Karyawan
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
    Write-Output "=== DATA KARYAWAN ==="
    Write-Output "NIK        : $($reader['no_nik'])"
    Write-Output "Nama       : $($reader['nama_lengkap'])"
    Write-Output "Perusahaan : $($reader['nama_perusahaan'])"
    Write-Output "Departemen : $($reader['nama_departemen'])"
    Write-Output "Jabatan    : $($reader['nama_jabatan'])"
}
$reader.Close()

# 2. Hazard
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

Write-Output "`n=== AUDIT HAZARD (Total: $($dtHaz.Rows.Count) Laporan) ==="

# Check duplicate by exact text 'temuan'
$dupTemuan = $dtHaz.Rows | Group-Object { $_.temuan.ToString().Trim().ToLower() } | Where-Object { $_.Count -gt 1 }
Write-Output "1. Duplikasi Teks Temuan Persis: $($dupTemuan.Count) kasus"
foreach ($g in $dupTemuan) {
    Write-Output "   * Temuan ($($g.Count)x): '$($g.Name)'"
    foreach ($r in $g.Group) {
        $t = [DateTime]$r['tanggal']
        Write-Output "     - ID: $($r['id']) | Tgl: $($t.ToString('yyyy-MM-dd')) $($r['waktu']) | Lokasi: $($r['lokasi']) ($($r['detil_lokasi'])) | CreatedAt: $($r['created_at'])"
    }
}

# Check duplicate by same date & same area/lokasi
$dupDateLok = $dtHaz.Rows | Group-Object { 
    $t = [DateTime]$_['tanggal']
    "$($t.ToString('yyyy-MM-dd')) | $($_.lokasi.ToString().Trim().ToLower())"
} | Where-Object { $_.Count -gt 1 }
Write-Output "`n2. Laporan Hazard Lebih dari 1 pada Tanggal & Lokasi Sama: $($dupDateLok.Count) tanggal"
foreach ($g in $dupDateLok) {
    Write-Output "   * Tgl & Lok: $($g.Name) ($($g.Count) laporan)"
    foreach ($r in $g.Group) {
        $desc = if ($r['temuan'].ToString().Length -gt 60) { $r['temuan'].ToString().Substring(0, 60) + "..." } else { $r['temuan'].ToString() }
        Write-Output "     - ID: $($r['id']) | Jam: $($r['waktu']) | Temuan: $desc"
    }
}

# 3. Inspeksi
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

Write-Output "`n=== AUDIT INSPEKSI (Total: $($dtInsp.Rows.Count) Laporan) ==="

# Check duplicate by same date + same jenis_inspeksi + same lokasi
$dupInspLok = $dtInsp.Rows | Group-Object { 
    $t = [DateTime]$_['tanggal']
    "$($t.ToString('yyyy-MM-dd')) | $($_.jenis_inspeksi.ToString().Trim().ToLower()) | $($_.lokasi.ToString().Trim().ToLower()) | $($_.detil_lokasi.ToString().Trim().ToLower())"
} | Where-Object { $_.Count -gt 1 }
Write-Output "1. Duplikasi Inspeksi Persis (Tanggal, Jenis, Lokasi, Detil Lokasi Sama): $($dupInspLok.Count) kasus"
foreach ($g in $dupInspLok) {
    Write-Output "   * $($g.Name) ($($g.Count)x)"
    foreach ($r in $g.Group) {
        Write-Output "     - ID: $($r['id']) | Jam: $($r['waktu']) | PJA: $($r['pja']) | CreatedAt: $($r['created_at'])"
    }
}

# Check multiple inspection on same date
$dupInspDate = $dtInsp.Rows | Group-Object { 
    $t = [DateTime]$_['tanggal']
    $t.ToString('yyyy-MM-dd')
} | Where-Object { $_.Count -gt 1 }
Write-Output "`n2. Tanggal dengan Lebih dari 1 Inspeksi: $($dupInspDate.Count) hari"
foreach ($g in $dupInspDate) {
    Write-Output "   * Tanggal: $($g.Name) ($($g.Count) inspeksi)"
    foreach ($r in $g.Group) {
        Write-Output "     - ID: $($r['id']) | Jam: $($r['waktu']) | Jenis: $($r['jenis_inspeksi']) | Lok: $($r['lokasi']) ($($r['detil_lokasi'])) | CreatedAt: $($r['created_at'])"
    }
}

$conn.Close()
