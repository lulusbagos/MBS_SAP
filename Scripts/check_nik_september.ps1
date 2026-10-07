$connStr = "Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT temuan, COUNT(*) as cnt 
FROM tbl_t_hazard_report 
WHERE nik = '511231470' AND is_deleted = 0 
GROUP BY temuan 
HAVING COUNT(*) > 1 
ORDER BY cnt DESC
"@
$r = $cmd.ExecuteReader()
Write-Output "TEMUAN HAZARD YANG DIULANG (DUPLIKAT TEKS):"
while ($r.Read()) {
    Write-Output "[$($r['cnt'])x] $($r['temuan'])"
}
$r.Close()

# Also check specifically for September 2026!
Write-Output "`n=== KHUSUS BULAN SEPTEMBER 2026 ==="
$cmdSep = $conn.CreateCommand()
$cmdSep.CommandText = @"
SELECT id, tanggal, waktu, area, lokasi, detil_lokasi, temuan, tindakan_perbaikan, created_at
FROM tbl_t_hazard_report
WHERE nik = '511231470' AND is_deleted = 0
  AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30'
ORDER BY tanggal, waktu
"@
$r2 = $cmdSep.ExecuteReader()
$hazSep = @()
while ($r2.Read()) {
    $hazSep += [PSCustomObject]@{
        Id = $r2['id']
        Tanggal = [DateTime]$r2['tanggal']
        Waktu = $r2['waktu']
        Lokasi = $r2['lokasi']
        Detil = $r2['detil_lokasi']
        Temuan = $r2['temuan']
        CreatedAt = $r2['created_at']
    }
}
$r2.Close()
Write-Output "Total Hazard September 2026: $($hazSep.Count)"
$hazSep | Group-Object { $_.Tanggal.ToString("yyyy-MM-dd") } | ForEach-Object {
    if ($_.Count -gt 1) {
        Write-Output "  Tanggal $($_.Name): $($_.Count) hazard"
        foreach ($item in $_.Group) {
            Write-Output "    - ID: $($item.Id) ($($item.Waktu)) | Lok: $($item.Lokasi) ($($item.Detil)) | Temuan: $($item.Temuan)"
        }
    }
}

# Inspeksi September 2026
$cmdInspSep = $conn.CreateCommand()
$cmdInspSep.CommandText = @"
SELECT id, tanggal, waktu, jenis_inspeksi, area, lokasi, detil_lokasi, created_at
FROM tbl_t_inspection
WHERE nik = '511231470' AND is_deleted = 0
  AND tanggal >= '2026-09-01' AND tanggal <= '2026-09-30'
ORDER BY tanggal, waktu
"@
$r3 = $cmdInspSep.ExecuteReader()
$inspSep = @()
while ($r3.Read()) {
    $inspSep += [PSCustomObject]@{
        Id = $r3['id']
        Tanggal = [DateTime]$r3['tanggal']
        Waktu = $r3['waktu']
        Lokasi = $r3['lokasi']
        Detil = $r3['detil_lokasi']
        CreatedAt = $r3['created_at']
    }
}
$r3.Close()
Write-Output "`nTotal Inspeksi September 2026: $($inspSep.Count)"
$dupInspSep = $inspSep | Group-Object { "$($_.Tanggal.ToString('yyyy-MM-dd')) | $($_.Lokasi) | $($_.Detil)" } | Where-Object { $_.Count -gt 1 }
Write-Output "Duplikat Tanggal + Lokasi/Front pada September 2026: $($dupInspSep.Count)"
foreach ($item in $dupInspSep) {
    Write-Output "  * $($item.Name) ($($item.Count)x):"
    foreach ($sub in $item.Group) {
        Write-Output "    - ID: $($sub.Id) ($($sub.Waktu)) | Created: $($sub.CreatedAt)"
    }
}

$conn.Close()
