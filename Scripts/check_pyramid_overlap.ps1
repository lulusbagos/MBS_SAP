$ErrorActionPreference = 'Stop'
$connStr = 'Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;'
$cn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$cn.Open()

$q = @"
WITH src AS (
    SELECT
        LOWER(ISNULL(kategori,'')) AS kategori,
        LOWER(ISNULL(kategori,'') + ' ' + ISNULL(judul,'') + ' ' + ISNULL(konten,'')) AS text_all
    FROM tbl_t_incident_news
    WHERE is_published = 1
)
SELECT
    SUM(CASE WHEN kategori = 'near miss' OR text_all LIKE '%near miss%' OR text_all LIKE '%nyaris%' OR text_all LIKE '%hampir celaka%' THEN 1 ELSE 0 END) AS NearMiss,
    SUM(CASE WHEN kategori = 'property damaged' OR text_all LIKE '%property%' OR text_all LIKE '%damage%' OR text_all LIKE '%damaged%' OR text_all LIKE '%kerusakan%' OR text_all LIKE '%aset%' OR text_all LIKE '%alat rusak%' THEN 1 ELSE 0 END) AS PropertyDamaged,
    SUM(CASE WHEN kategori = 'first aid injury' OR text_all LIKE '%first aid%' OR text_all LIKE '%p3k%' OR text_all LIKE '%pertolongan pertama%' THEN 1 ELSE 0 END) AS FirstAid,
    SUM(CASE WHEN kategori = 'medical treatment injury' OR text_all LIKE '%medical treatment%' OR text_all LIKE '%rawat jalan%' OR text_all LIKE '%klinik%' OR text_all LIKE '%dokter%' OR kategori = 'sedang' THEN 1 ELSE 0 END) AS MedicalTreatment,
    SUM(CASE WHEN kategori = 'ringan' THEN 1 ELSE 0 END) AS Ringan,
    SUM(CASE WHEN kategori = 'berat' THEN 1 ELSE 0 END) AS Berat,
    SUM(CASE WHEN kategori = 'mati' OR kategori = 'fatal' OR text_all LIKE '%meninggal%' OR text_all LIKE '%death%' THEN 1 ELSE 0 END) AS Mati,
    COUNT(1) AS TotalRows
FROM src;
"@

$cmd = $cn.CreateCommand()
$cmd.CommandText = $q
$r = $cmd.ExecuteReader()
if ($r.Read()) {
    $near = [int]$r['NearMiss']
    $prop = [int]$r['PropertyDamaged']
    $fa = [int]$r['FirstAid']
    $med = [int]$r['MedicalTreatment']
    $ringan = [int]$r['Ringan']
    $berat = [int]$r['Berat']
    $mati = [int]$r['Mati']
    $total = [int]$r['TotalRows']
    $sumPyramid = $near + $prop + $fa + $med + $ringan + $berat + $mati

    Write-Output "NearMiss=$near"
    Write-Output "PropertyDamaged=$prop"
    Write-Output "FirstAid=$fa"
    Write-Output "MedicalTreatment=$med"
    Write-Output "Ringan=$ringan"
    Write-Output "Berat=$berat"
    Write-Output "Mati=$mati"
    Write-Output "TotalRows=$total"
    Write-Output "SumPyramid=$sumPyramid"
}
$r.Close()
$cn.Close()
