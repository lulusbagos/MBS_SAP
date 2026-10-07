$niks = @("25022021183", "25051771130", "23062940814")

$body = @{
    niks = $niks
    month = 10
    year = 2026
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "http://localhost:5111/Performance/GetEmployeesComplianceData?month=10&year=2026" -Method Get -TimeoutSec 15
    $filtered = $res | Where-Object { $niks -contains $_.nik }
    $filtered | Select-Object nik, nama, jabatan, totalTarget, totalActual, compliancePercent, targetDetail | Format-List
} catch {
    Write-Host "HTTP error: $_"
}
