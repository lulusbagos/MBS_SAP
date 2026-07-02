$ErrorActionPreference = 'Stop'
$xlsx = 'D:\Book1.xlsx'

$base = Join-Path $env:USERPROFILE '.nuget\packages'
$closedXmlDll = Get-ChildItem "$base\closedxml\*\lib\netstandard2.0\ClosedXML.dll" -ErrorAction Stop | Sort-Object FullName -Descending | Select-Object -First 1
$docFormatDll = Get-ChildItem "$base\documentformat.openxml\*\lib\netstandard2.0\DocumentFormat.OpenXml.dll" -ErrorAction Stop | Sort-Object FullName -Descending | Select-Object -First 1
$excelFmtDll = Get-ChildItem "$base\excelnumberformat\*\lib\netstandard2.0\ExcelNumberFormat.dll" -ErrorAction Stop | Sort-Object FullName -Descending | Select-Object -First 1
$rbushDll = Get-ChildItem "$base\rbush\*\lib\netstandard2.0\RBush.dll" -ErrorAction Stop | Sort-Object FullName -Descending | Select-Object -First 1
$parserDll = Get-ChildItem "$base\closedxml.parser\*\lib\netstandard2.0\ClosedXML.Parser.dll" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1

[void][System.Reflection.Assembly]::LoadFrom($docFormatDll.FullName)
[void][System.Reflection.Assembly]::LoadFrom($excelFmtDll.FullName)
[void][System.Reflection.Assembly]::LoadFrom($rbushDll.FullName)
if ($null -ne $parserDll) { [void][System.Reflection.Assembly]::LoadFrom($parserDll.FullName) }
[void][System.Reflection.Assembly]::LoadFrom($closedXmlDll.FullName)

function Normalize-Header([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    return [string]::Concat(($text.ToLowerInvariant().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) }))
}

$wb = New-Object ClosedXML.Excel.XLWorkbook($xlsx)
$ws = $wb.Worksheets | Where-Object { $_.Name.Trim().ToLower() -eq 'source data' } | Select-Object -First 1
if ($null -eq $ws) { $ws = $wb.Worksheet(1) }

$lastRow = ($ws.LastRowUsed()).RowNumber()
$lastCol = ($ws.LastColumnUsed()).ColumnNumber()

$headerRow = $null
$colAktual = $null
for ($r = 1; $r -le [Math]::Min($lastRow, 50); $r++) {
    for ($c = 1; $c -le $lastCol; $c++) {
        $h = Normalize-Header($ws.Cell($r, $c).GetString())
        if ($h -eq 'aktualinsiden' -or $h -eq 'actualincident') {
            $headerRow = $r
            $colAktual = $c
            break
        }
    }
    if ($null -ne $headerRow) { break }
}

if ($null -eq $headerRow) {
    throw 'Header ACTUAL INCIDENT / AKTUAL INSIDEN tidak ditemukan.'
}

$valid = 0
$blank = 0
for ($r = $headerRow + 1; $r -le $lastRow; $r++) {
    $v = $ws.Cell($r, $colAktual).GetString()
    if ([string]::IsNullOrWhiteSpace($v)) { $blank++ } else { $valid++ }
}

Write-Output "SheetUsed: $($ws.Name)"
Write-Output "HeaderRow: $headerRow"
Write-Output "AktualIncidentCol: $colAktual"
Write-Output "LastRow: $lastRow"
Write-Output "ValidRows(AktualIncident not blank): $valid"
Write-Output "BlankRows: $blank"
