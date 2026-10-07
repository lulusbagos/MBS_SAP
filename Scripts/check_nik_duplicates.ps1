$connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

$nik = "511231470"

# 1. Employee info
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
    Write-Output ""
} else {
    Write-Output "Karyawan NIK $nik tidak ditemukan di vw_karyawan!"
}
$reader.Close()

# 2. Check Hazard Table: tbl_t_hazard_report
$cmdHaz = $conn.CreateCommand()
$cmdHaz.CommandText = @"
SELECT id, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, tindakan_perbaikan, status_temuan, created_at, is_deleted
FROM tbl_t_hazard_report
WHERE nik = '$nik' AND is_deleted = 0
ORDER BY tanggal DESC, waktu DESC
"@
$daHaz = New-Object System.Data.SqlClient.SqlDataAdapter($cmdHaz)
$dtHaz = New-Object System.Data.DataTable
$daHaz.Fill($dtHaz) | Out-Null

Write-Output "=== TOTAL LAPORAN HAZARD: $($dtHaz.Rows.Count) ==="
if ($dtHaz.Rows.Count -gt 0) {
    # Check duplicate scenarios:
    # A) Exact same Temuan
    # B) Same date & similar/same Temuan
    # C) Same date & same Area/Lokasi
    $groupsTemuan = $dtHaz.Rows | Group-Object { "$($_.temuan.ToString().Trim().ToLower())" }
    $dupTemuanCount = 0
    foreach ($g in $groupsTemuan) {
        if ($g.Count -gt 1) {
            $dupTemuanCount++
            Write-Output "`n>>> DUPLIKASI HAZARD: TEMUAN PERSIS SAMA (Count: $($g.Count)):"
            foreach ($row in $g.Group) {
                $tgl = if ($row['tanggal'] -ne [DBNull]::Value) { [DateTime]$row['tanggal'] } else { "" }
                $tglStr = if ($tgl -is [DateTime]) { $tgl.ToString("yyyy-MM-dd") } else { "-" }
                Write-Output "    - ID: $($row['id']) | Tgl: $tglStr $($row['waktu']) | Lokasi: $($row['lokasi']) / $($row['detil_lokasi']) | Status: $($row['status_temuan']) | Created: $($row['created_at'])"
                Write-Output "      Temuan: $($row['temuan'])"
            }
        }
    }
    if ($dupTemuanCount -eq 0) {
        Write-Output "Tidak ada duplikasi teks Temuan yang persis sama."
    }

    # Check same tanggal & waktu close to each other (within same day)
    $groupsDate = $dtHaz.Rows | Group-Object { 
        $t = if ($_['tanggal'] -ne [DBNull]::Value) { [DateTime]$_['tanggal'] } else { [DateTime]::MinValue }
        $t.ToString("yyyy-MM-dd")
    }
    Write-Output "`n--- Sebaran Hazard per Tanggal ---"
    foreach ($gd in $groupsDate | Sort-Object Name -Descending) {
        Write-Output "Tanggal $($gd.Name): $($gd.Count) laporan hazard"
        if ($gd.Count -gt 1) {
            foreach ($row in $gd.Group) {
                Write-Output "  * ID: $($row['id']) | Jam: $($row['waktu']) | Lok: $($row['lokasi']) | Temuan: $($row['temuan']) | Created: $($row['created_at'])"
            }
        }
    }

    Write-Output "`n--- Daftar Lengkap Seluruh Hazard ($($dtHaz.Rows.Count) item) ---"
    foreach ($row in $dtHaz.Rows) {
        $tgl = if ($row['tanggal'] -ne [DBNull]::Value) { [DateTime]$row['tanggal'] } else { "" }
        $tglStr = if ($tgl -is [DateTime]) { $tgl.ToString("yyyy-MM-dd") } else { "-" }
        Write-Output "  ID: $($row['id']) | Tgl: $tglStr $($row['waktu']) | Area: $($row['area']) | Lok: $($row['lokasi']) ($($row['detil_lokasi'])) | Status: $($row['status_temuan'])"
        Write-Output "    Temuan: $($row['temuan'])"
        Write-Output "    Tindakan: $($row['tindakan_perbaikan'])"
        Write-Output "    Created: $($row['created_at'])"
        Write-Output "  --------------------------------------------------"
    }
}
Write-Output ""

# 3. Check Inspection Table: tbl_t_inspection
$cmdInsp = $conn.CreateCommand()
$cmdInsp.CommandText = @"
SELECT id, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, pja, nik_pja, created_at, is_deleted
FROM tbl_t_inspection
WHERE nik = '$nik' AND is_deleted = 0
ORDER BY tanggal DESC, waktu DESC
"@
$daInsp = New-Object System.Data.SqlClient.SqlDataAdapter($cmdInsp)
$dtInsp = New-Object System.Data.DataTable
$daInsp.Fill($dtInsp) | Out-Null

Write-Output "=== TOTAL LAPORAN INSPEKSI: $($dtInsp.Rows.Count) ==="
if ($dtInsp.Rows.Count -gt 0) {
    # Check duplicate inspection:
    # A) Same Tanggal & same JenisInspeksi & same Lokasi
    # B) Same Tanggal & same JenisInspeksi
    $groupsJenisLok = $dtInsp.Rows | Group-Object { 
        $t = if ($_['tanggal'] -ne [DBNull]::Value) { [DateTime]$_['tanggal'] } else { [DateTime]::MinValue }
        "$($t.ToString('yyyy-MM-dd'))_$($_.jenis_inspeksi.ToString().Trim().ToLower())_$($_.lokasi.ToString().Trim().ToLower())"
    }
    $dupInspCount = 0
    foreach ($g in $groupsJenisLok) {
        if ($g.Count -gt 1) {
            $dupInspCount++
            Write-Output "`n>>> DUPLIKASI INSPEKSI: TANGGAL, JENIS & LOKASI SAMA (Count: $($g.Count)):"
            foreach ($row in $g.Group) {
                $tgl = if ($row['tanggal'] -ne [DBNull]::Value) { [DateTime]$row['tanggal'] } else { "" }
                $tglStr = if ($tgl -is [DateTime]) { $tgl.ToString("yyyy-MM-dd") } else { "-" }
                Write-Output "    - ID: $($row['id']) | Tgl: $tglStr $($row['waktu']) | Jenis: $($row['jenis_inspeksi']) | Lok: $($row['lokasi']) ($($row['detil_lokasi'])) | Created: $($row['created_at'])"
            }
        }
    }
    if ($dupInspCount -eq 0) {
        Write-Output "Tidak ada duplikasi (Tanggal + Jenis + Lokasi Sama) pada Inspeksi."
    }

    # Check same tanggal & jenis inspeksi (even if location differs slightly)
    $groupsJenis = $dtInsp.Rows | Group-Object { 
        $t = if ($_['tanggal'] -ne [DBNull]::Value) { [DateTime]$_['tanggal'] } else { [DateTime]::MinValue }
        "$($t.ToString('yyyy-MM-dd'))_$($_.jenis_inspeksi.ToString().Trim().ToLower())"
    }
    $dupJenisCount = 0
    foreach ($g in $groupsJenis) {
        if ($g.Count -gt 1) {
            $dupJenisCount++
            Write-Output "`n>>> POTENSI DUPLIKASI: TANGGAL & JENIS SAMA PADA HARI YANG SAMA (Count: $($g.Count)):"
            foreach ($row in $g.Group) {
                $tgl = if ($row['tanggal'] -ne [DBNull]::Value) { [DateTime]$row['tanggal'] } else { "" }
                $tglStr = if ($tgl -is [DateTime]) { $tgl.ToString("yyyy-MM-dd") } else { "-" }
                Write-Output "    - ID: $($row['id']) | Tgl: $tglStr $($row['waktu']) | Jenis: $($row['jenis_inspeksi']) | Area: $($row['area']) | Lok: $($row['lokasi']) ($($row['detil_lokasi'])) | Created: $($row['created_at'])"
            }
        }
    }

    Write-Output "`n--- Daftar Lengkap Seluruh Inspeksi ($($dtInsp.Rows.Count) item) ---"
    foreach ($row in $dtInsp.Rows) {
        $tgl = if ($row['tanggal'] -ne [DBNull]::Value) { [DateTime]$row['tanggal'] } else { "" }
        $tglStr = if ($tgl -is [DateTime]) { $tgl.ToString("yyyy-MM-dd") } else { "-" }
        Write-Output "  ID: $($row['id']) | Tgl: $tglStr $($row['waktu']) | Jenis: $($row['jenis_inspeksi']) | Area: $($row['area']) | Lok: $($row['lokasi']) ($($row['detil_lokasi'])) | PJA: $($row['pja']) ($($row['nik_pja']))"
        Write-Output "    Created: $($row['created_at'])"
        Write-Output "  --------------------------------------------------"
    }
}

$conn.Close()
