$ErrorActionPreference = 'Stop'

$xlsxPath = 'D:\Book1.xlsx'
if (!(Test-Path $xlsxPath)) {
    throw "File tidak ditemukan: $xlsxPath"
}

$connStr = 'Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;MultipleActiveResultSets=True;'

function Normalize-Header([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    return [string]::Concat(($text.ToLowerInvariant().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) }))
}

function Find-Col($map, [string[]]$aliases) {
    foreach ($a in $aliases) {
        if ($map.ContainsKey($a)) { return [int]$map[$a] }
        foreach ($k in $map.Keys) {
            if ($k.Contains($a)) { return [int]$map[$k] }
        }
    }
    return $null
}

function Normalize-Category([string]$raw) {
    if ($null -eq $raw) { $raw = '' }
    $text = $raw.Trim().ToLowerInvariant()

    if ($text.Contains('near miss') -or $text.Contains('nyaris') -or $text.Contains('hampir celaka')) { return 'Near Miss' }
    if ($text.Contains('property') -or $text.Contains('damaged') -or $text.Contains('damage') -or $text.Contains('kerusakan')) { return 'Property Damaged' }
    if ($text.Contains('first aid') -or $text.Contains('p3k') -or $text.Contains('pertolongan pertama')) { return 'First Aid Injury' }
    if ($text.Contains('medical treatment') -or $text.Contains('rawat jalan') -or $text.Contains('klinik') -or $text.Contains('dokter')) { return 'Medical Treatment Injury' }
    if ($text.Contains('mati') -or $text.Contains('fatal') -or $text.Contains('meninggal') -or $text.Contains('death')) { return 'Mati' }
    if ($text.Contains('berat') -or $text.Contains('rawat inap')) { return 'Berat' }
    if ($text.Contains('ringan')) { return 'Ringan' }

    return 'Near Miss'
}

function Parse-DateFlexible([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }

    $d = [datetime]::MinValue
    if ([datetime]::TryParse($raw, [ref]$d)) { return $d }

    $formats = @(
        'dd/MM/yyyy', 'd/M/yyyy', 'dd-MM-yyyy', 'd-M-yyyy',
        'dd-MMM-yy', 'd-MMM-yy', 'dd-MMM-yyyy', 'd-MMM-yyyy',
        'MM/dd/yyyy', 'M/d/yyyy'
    )

    foreach ($fmt in $formats) {
        if ([datetime]::TryParseExact($raw, $fmt, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$d)) {
            return $d
        }
    }

    return $null
}

$connection = New-Object System.Data.SqlClient.SqlConnection($connStr)
$connection.Open()
$transaction = $connection.BeginTransaction()

$excel = $null
$workbook = $null

try {
    # Bersihkan tabel sesuai instruksi user
    $clearCmd = $connection.CreateCommand()
    $clearCmd.Transaction = $transaction
    $clearCmd.CommandText = 'DELETE FROM tbl_t_incident_news;'
    [void]$clearCmd.ExecuteNonQuery()

    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $workbook = $excel.Workbooks.Open($xlsxPath)

    $worksheet = $null
    foreach ($ws in $workbook.Worksheets) {
        if (($ws.Name).Trim().ToLower() -eq 'source data') {
            $worksheet = $ws
            break
        }
    }
    if ($null -eq $worksheet) {
        $worksheet = $workbook.Worksheets.Item(1)
    }

    $used = $worksheet.UsedRange
    $startRow = [int]$used.Row
    $rowCount = [int]$used.Rows.Count
    $startCol = [int]$used.Column
    $colCount = [int]$used.Columns.Count
    $lastRow = $startRow + $rowCount - 1
    $lastCol = $startCol + $colCount - 1

    $headerRow = $null
    for ($r = $startRow; $r -le [Math]::Min($lastRow, $startRow + 80); $r++) {
        $found = $false
        for ($c = $startCol; $c -le $lastCol; $c++) {
            $h = Normalize-Header([string]$worksheet.Cells.Item($r, $c).Text)
            if ($h -eq 'aktualinsiden' -or $h -eq 'actualincident') {
                $headerRow = $r
                $found = $true
                break
            }
        }
        if ($found) { break }
    }

    if ($null -eq $headerRow) {
        throw 'Header ACTUAL INCIDENT / AKTUAL INSIDEN tidak ditemukan di Excel.'
    }

    $map = @{}
    for ($c = $startCol; $c -le $lastCol; $c++) {
        $key = Normalize-Header([string]$worksheet.Cells.Item($headerRow, $c).Text)
        if (-not [string]::IsNullOrWhiteSpace($key) -and -not $map.ContainsKey($key)) {
            $map[$key] = $c
        }
    }

    $colAktual = Find-Col $map @('aktualinsiden', 'actualincident')
    if ($null -eq $colAktual) { throw "Kolom 'ACTUAL INCIDENT' tidak ditemukan." }

    $colTitle = Find-Col $map @('judul', 'title', 'kejadian', 'incident', 'briefdescription')
    $colDesc = Find-Col $map @('konten', 'keterangan', 'deskripsi', 'kronologi', 'detail', 'uraian', 'description')
    $colLokasi = Find-Col $map @('lokasi', 'location', 'site', 'area')
    $colTanggal = Find-Col $map @('tanggalkejadian', 'tanggal', 'date', 'tgl', 'tglkejadian')

    $insertCmd = $connection.CreateCommand()
    $insertCmd.Transaction = $transaction
    $insertCmd.CommandText = @"
INSERT INTO tbl_t_incident_news
    (judul, konten, lokasi, tanggal_kejadian, kategori, dibuat_oleh, nik_pembuat, is_published, created_at)
VALUES
    (@judul, @konten, @lokasi, @tanggal_kejadian, @kategori, @dibuat_oleh, @nik_pembuat, 1, @created_at)
"@

    $null = $insertCmd.Parameters.Add('@judul', [System.Data.SqlDbType]::NVarChar, 300)
    $null = $insertCmd.Parameters.Add('@konten', [System.Data.SqlDbType]::NVarChar, -1)
    $null = $insertCmd.Parameters.Add('@lokasi', [System.Data.SqlDbType]::NVarChar, 150)
    $null = $insertCmd.Parameters.Add('@tanggal_kejadian', [System.Data.SqlDbType]::DateTime)
    $null = $insertCmd.Parameters.Add('@kategori', [System.Data.SqlDbType]::NVarChar, 100)
    $null = $insertCmd.Parameters.Add('@dibuat_oleh', [System.Data.SqlDbType]::NVarChar, 150)
    $null = $insertCmd.Parameters.Add('@nik_pembuat', [System.Data.SqlDbType]::NVarChar, 50)
    $null = $insertCmd.Parameters.Add('@created_at', [System.Data.SqlDbType]::DateTime)

    $createdBy = 'System Import Excel'
    $nik = 'SYSTEM'

    $inserted = 0
    $skipped = 0

    for ($r = $headerRow + 1; $r -le $lastRow; $r++) {
        $aktualRaw = [string]$worksheet.Cells.Item($r, $colAktual).Text
        if ([string]::IsNullOrWhiteSpace($aktualRaw)) {
            $skipped++
            continue
        }

        $judul = if ($null -ne $colTitle) { [string]$worksheet.Cells.Item($r, $colTitle).Text } else { '' }
        $konten = if ($null -ne $colDesc) { [string]$worksheet.Cells.Item($r, $colDesc).Text } else { '' }
        $lokasi = if ($null -ne $colLokasi) { [string]$worksheet.Cells.Item($r, $colLokasi).Text } else { '' }
        $tanggalRaw = if ($null -ne $colTanggal) { [string]$worksheet.Cells.Item($r, $colTanggal).Text } else { '' }
        $tanggal = Parse-DateFlexible $tanggalRaw

        $kategori = Normalize-Category $aktualRaw

        if ([string]::IsNullOrWhiteSpace($judul)) { $judul = "$kategori - Imported Incident" }
        if ([string]::IsNullOrWhiteSpace($konten)) { $konten = "Data import Excel dengan aktual insiden: $aktualRaw." }

        if ($judul.Length -gt 300) { $judul = $judul.Substring(0, 300) }
        if ($lokasi.Length -gt 150) { $lokasi = $lokasi.Substring(0, 150) }

        $insertCmd.Parameters['@judul'].Value = $judul
        $insertCmd.Parameters['@konten'].Value = $konten
        $insertCmd.Parameters['@lokasi'].Value = $lokasi
        if ($null -eq $tanggal) { $insertCmd.Parameters['@tanggal_kejadian'].Value = [DBNull]::Value } else { $insertCmd.Parameters['@tanggal_kejadian'].Value = $tanggal }
        $insertCmd.Parameters['@kategori'].Value = $kategori
        $insertCmd.Parameters['@dibuat_oleh'].Value = $createdBy
        $insertCmd.Parameters['@nik_pembuat'].Value = $nik
        $insertCmd.Parameters['@created_at'].Value = if ($null -eq $tanggal) { [datetime]::Now } else { $tanggal }

        [void]$insertCmd.ExecuteNonQuery()
        $inserted++
    }

    $transaction.Commit()

    Write-Output "Import Excel selesai. Inserted: $inserted | Skipped: $skipped | Sheet: $($worksheet.Name)"
}
catch {
    try { $transaction.Rollback() } catch {}
    throw
}
finally {
    if ($null -ne $workbook) {
        $workbook.Close($false)
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($workbook) | Out-Null
    }
    if ($null -ne $excel) {
        $excel.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    if ($connection.State -eq [System.Data.ConnectionState]::Open) {
        $connection.Close()
    }
}
