$connStr = 'Server=172.16.1.93;Database=DB_SAP;User Id=sa;Password=technical.indexim.123;TrustServerCertificate=True;'
$sql = Get-Content 'd:\4. PROJECT\2. Web\MBS_SAP\dbtest\view_definition.sql' -Raw
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql
$cmd.CommandTimeout = 120
$res = $cmd.ExecuteNonQuery()
$conn.Close()
Write-Output "ALTER VIEW EXECUTED SUCCESSFULLY! Result: $res"
