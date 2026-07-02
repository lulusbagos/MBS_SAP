$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName Microsoft.VisualBasic

$csvPath = 'D:\Incident Register Update 2026 Update(SOURCE DATA).csv'
if (!(Test-Path $csvPath)) {
    throw "File tidak ditemukan: $csvPath"
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

function Get-FieldValue($fields, $idx) {
    if ($null -eq $idx) { return '' }
    if ($idx -lt 0 -or $idx -ge $fields.Count) { return '' }
    if ($null -eq $fields[$idx]) { return '' }
    return ($fields[$idx].ToString().Trim())
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

try {
    $cmd = $connection.CreateCommand()
    $cmd.Transaction = $transaction
    $cmd.CommandText = @"
INSERT INTO tbl_t_incident_news
    (judul, konten, lokasi, tanggal_kejadian, kategori, dibuat_oleh, nik_pembuat, is_published, created_at)
VALUES
    (@judul, @konten, @lokasi, @tanggal_kejadian, @kategori, @dibuat_oleh, @nik_pembuat, 1, @created_at)
"@

    $null = $cmd.Parameters.Add('@judul', [System.Data.SqlDbType]::NVarChar, 300)
    $null = $cmd.Parameters.Add('@konten', [System.Data.SqlDbType]::NVarChar, -1)
    $null = $cmd.Parameters.Add('@lokasi', [System.Data.SqlDbType]::NVarChar, 150)
    $null = $cmd.Parameters.Add('@tanggal_kejadian', [System.Data.SqlDbType]::DateTime)
    $null = $cmd.Parameters.Add('@kategori', [System.Data.SqlDbType]::NVarChar, 100)
    $null = $cmd.Parameters.Add('@dibuat_oleh', [System.Data.SqlDbType]::NVarChar, 150)
    $null = $cmd.Parameters.Add('@nik_pembuat', [System.Data.SqlDbType]::NVarChar, 50)
    $null = $cmd.Parameters.Add('@created_at', [System.Data.SqlDbType]::DateTime)

    $createdBy = 'System Import CSV'
    $nik = 'SYSTEM'

    $parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($csvPath)
    $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
    $parser.SetDelimiters(',')
    $parser.HasFieldsEnclosedInQuotes = $true

    $headerFound = $false
    $colAktual = $null
    $colTitle = $null
    $colDesc = $null
    $colLokasi = $null
    $colTanggal = $null

    $totalDataRows = 0
    $inserted = 0
    $skipped = 0

    while (-not $parser.EndOfData) {
        $fields = $null
        try {
            $fields = $parser.ReadFields()
        }
        catch {
            $skipped++
            continue
        }

        if ($null -eq $fields -or $fields.Count -eq 0) {
            $skipped++
            continue
        }

        if (-not $headerFound) {
            $map = @{}
            for ($i = 0; $i -lt $fields.Count; $i++) {
                $key = Normalize-Header $fields[$i]
                if (-not [string]::IsNullOrWhiteSpace($key) -and -not $map.ContainsKey($key)) {
                    $map[$key] = $i
                }
            }

            $maybeAktual = Find-Col $map @('aktualinsiden', 'actualincident')
            if ($null -ne $maybeAktual) {
                $headerFound = $true
                $colAktual = $maybeAktual
                $colTitle = Find-Col $map @('judul', 'title', 'kejadian', 'incident', 'briefdescription')
                $colDesc = Find-Col $map @('konten', 'keterangan', 'deskripsi', 'kronologi', 'detail', 'uraian', 'description')
                $colLokasi = Find-Col $map @('lokasi', 'location', 'site', 'area')
                $colTanggal = Find-Col $map @('tanggalkejadian', 'tanggal', 'date', 'tgl', 'tglkejadian')
            }
            continue
        }

        $totalDataRows++

        $aktualRaw = Get-FieldValue $fields $colAktual
        if ([string]::IsNullOrWhiteSpace($aktualRaw)) {
            $skipped++
            continue
        }

        $kategori = Normalize-Category $aktualRaw
        $judul = Get-FieldValue $fields $colTitle
        $konten = Get-FieldValue $fields $colDesc
        $lokasi = Get-FieldValue $fields $colLokasi
        $tanggal = Parse-DateFlexible (Get-FieldValue $fields $colTanggal)

        if ([string]::IsNullOrWhiteSpace($judul)) {
            $judul = "$kategori - Imported Incident"
        }

        if ([string]::IsNullOrWhiteSpace($konten)) {
            $konten = "Data import CSV dengan aktual insiden: $aktualRaw."
        }

        if ($judul.Length -gt 300) { $judul = $judul.Substring(0, 300) }
        if ($lokasi.Length -gt 150) { $lokasi = $lokasi.Substring(0, 150) }

        $cmd.Parameters['@judul'].Value = $judul
        $cmd.Parameters['@konten'].Value = $konten
        $cmd.Parameters['@lokasi'].Value = $lokasi
        if ($null -eq $tanggal) { $cmd.Parameters['@tanggal_kejadian'].Value = [DBNull]::Value } else { $cmd.Parameters['@tanggal_kejadian'].Value = $tanggal }
        $cmd.Parameters['@kategori'].Value = $kategori
        $cmd.Parameters['@dibuat_oleh'].Value = $createdBy
        $cmd.Parameters['@nik_pembuat'].Value = $nik
        $cmd.Parameters['@created_at'].Value = if ($null -eq $tanggal) { [datetime]::Now } else { $tanggal }

        [void]$cmd.ExecuteNonQuery()
        $inserted++
    }

    $parser.Close()

    if (-not $headerFound) {
        throw "Kolom 'ACTUAL INCIDENT' tidak ditemukan pada CSV."
    }

    $transaction.Commit()
    Write-Output "Import selesai. Total row terbaca: $totalDataRows | Inserted: $inserted | Skipped: $skipped"
}
catch {
    try { $transaction.Rollback() } catch {}
    throw
}
finally {
    if ($connection.State -eq [System.Data.ConnectionState]::Open) {
        $connection.Close()
    }
}
