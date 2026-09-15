# Prueba sincronizada de dos pujas. No garantiza solapamiento en el servidor.
# Exit 0: exactamente HTTP 200 válido + HTTP 409 con BID_CONFLICT.
# Exit 1: error o resultado no concluyente. No aplica migraciones.

$ErrorActionPreference = "Stop"

function Assert-BidResults {
    param(
        [object[]]$Results,
        [Guid]$AuctionId,
        [decimal]$ExpectedAmount
    )

    if ($Results.Count -ne 2) {
        throw "Se esperaban exactamente dos resultados."
    }

    $Accepted = @($Results | Where-Object StatusCode -eq "200")
    $Conflicts = @($Results | Where-Object StatusCode -eq "409")

    if ($Accepted.Count -ne 1 -or $Conflicts.Count -ne 1) {
        throw "Resultado no concluyente: se esperaba exactamente un HTTP 200 y un HTTP 409."
    }

    foreach ($Body in @($Accepted[0].Body, $Conflicts[0].Body)) {
        if (-not ([string]$Body).Trim().StartsWith("{")) {
            throw "Las respuestas HTTP 200 y 409 deben contener objetos JSON."
        }
    }

    $SuccessBody = $Accepted[0].Body | ConvertFrom-Json -ErrorAction Stop
    $ConflictBody = $Conflicts[0].Body | ConvertFrom-Json -ErrorAction Stop
    $ResponseAuctionId = [Guid]::Empty

    if ($ConflictBody -isnot [PSCustomObject] -or
        $ConflictBody.code -isnot [string] -or
        $ConflictBody.code -cne "BID_CONFLICT") {
        throw "El HTTP 409 no corresponde a BID_CONFLICT."
    }

    if ($SuccessBody -isnot [PSCustomObject] -or
        $SuccessBody.subastaId -isnot [string] -or
        -not ($SuccessBody.monto -is [int] -or
              $SuccessBody.monto -is [long] -or
              $SuccessBody.monto -is [double] -or
              $SuccessBody.monto -is [decimal]) -or
        -not [Guid]::TryParse([string]$SuccessBody.subastaId, [ref]$ResponseAuctionId) -or
        $ResponseAuctionId -ne $AuctionId -or
        [decimal]$SuccessBody.monto -ne $ExpectedAmount) {
        throw "El HTTP 200 no corresponde a la subasta y monto esperados."
    }
}

$ApiBaseUrl = "http://localhost:5000"
$Vendedor = "22222222-2222-2222-2222-222222222222"
$PostorA = "33333333-3333-3333-3333-333333333333"
$PostorB = "44444444-4444-4444-4444-444444444444"
$Monto = 12000
$Jobs = @()
$TemporaryDirectory = $null
$ExitCode = 1

try {
    $SqlCmd = (Get-Command sqlcmd -CommandType Application -ErrorAction Stop).Source
    $Curl = (Get-Command curl.exe -CommandType Application -ErrorAction Stop).Source

        $Sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @LockResult int;
EXEC @LockResult = sys.sp_getapplock
    @Resource = N'SubastaYa.StressTest.Setup',
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 10000;
IF @LockResult < 0
    THROW 51000, 'No se pudo bloquear la preparacion de datos de prueba.', 1;

IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Id = '$Vendedor')
    INSERT INTO Usuarios (Id, Email, Nombre, PasswordHash, FechaRegistro)
    VALUES ('$Vendedor', 'vendedor.test@subastaya.com', 'Vendedor Test', 'DEV_TEST', SYSDATETIMEOFFSET());
IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Id = '$PostorA')
    INSERT INTO Usuarios (Id, Email, Nombre, PasswordHash, FechaRegistro)
    VALUES ('$PostorA', 'postor.a@subastaya.com', 'Postor A', 'DEV_TEST', SYSDATETIMEOFFSET());
IF NOT EXISTS (SELECT 1 FROM Usuarios WHERE Id = '$PostorB')
    INSERT INTO Usuarios (Id, Email, Nombre, PasswordHash, FechaRegistro)
    VALUES ('$PostorB', 'postor.b@subastaya.com', 'Postor B', 'DEV_TEST', SYSDATETIMEOFFSET());

IF NOT EXISTS (SELECT 1 FROM Billeteras WHERE UsuarioId = '$Vendedor')
    INSERT INTO Billeteras (Id, UsuarioId, SaldoTotal, SaldoRetenido, SaldoDisponible)
    VALUES (NEWID(), '$Vendedor', 0, 0, 0);
IF NOT EXISTS (SELECT 1 FROM Billeteras WHERE UsuarioId = '$PostorA')
    INSERT INTO Billeteras (Id, UsuarioId, SaldoTotal, SaldoRetenido, SaldoDisponible)
    VALUES (NEWID(), '$PostorA', 1000000, 0, 1000000);
IF NOT EXISTS (SELECT 1 FROM Billeteras WHERE UsuarioId = '$PostorB')
    INSERT INTO Billeteras (Id, UsuarioId, SaldoTotal, SaldoRetenido, SaldoDisponible)
    VALUES (NEWID(), '$PostorB', 1000000, 0, 1000000);

IF EXISTS (
    SELECT 1 FROM Billeteras
    WHERE UsuarioId IN ('$PostorA', '$PostorB') AND SaldoDisponible < $Monto
)
    THROW 51001, 'Saldo disponible insuficiente en una billetera de prueba; no se resetean retenciones.', 1;
COMMIT TRANSACTION;
"@

    Write-Host "Preparando datos de prueba en (localdb)\MSSQLLocalDB / SubastaYaDb..."
    & $SqlCmd -S "(localdb)\MSSQLLocalDB" -d "SubastaYaDb" -E -b -l 10 -t 20 -Q $Sql

    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd no pudo preparar los datos de prueba."
    }

    $AuctionRequest = @{
        titulo = "Stress Test Concurrencia"
        descripcion = "Subasta generada automaticamente para probar concurrencia."
        categoria = "Tecnologia"
        precioInicial = 10000
        incrementoMinimo = 1000
        fechaInicioUtc = [DateTimeOffset]::UtcNow.AddSeconds(-5).ToString("o")
        fechaFinUtc = [DateTimeOffset]::UtcNow.AddMinutes(10).ToString("o")
    } | ConvertTo-Json

    $Auction = Invoke-RestMethod -Uri "$ApiBaseUrl/api/auctions" -Method Post `
        -Headers @{ "X-User-Id" = $Vendedor } `
        -ContentType "application/json; charset=utf-8" -Body $AuctionRequest -TimeoutSec 30

    $AuctionId = [Guid]::Empty

    if (-not [Guid]::TryParse([string]$Auction.id, [ref]$AuctionId) -or
        $AuctionId -eq [Guid]::Empty) {
        throw "La API no devolvio un ID de subasta valido."
    }

    Write-Host "Subasta creada: $AuctionId. Monto de cada puja: $Monto."
    $Url = "$ApiBaseUrl/api/auctions/$AuctionId/bids"
    $TemporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("subastaya-stress-" + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $TemporaryDirectory | Out-Null
    $GateFile = Join-Path $TemporaryDirectory "start.txt"

    $Worker = {
        param($Url, $UserId, $Monto, $Directory, $GateFile, $Curl, $Label)
        $ErrorActionPreference = "Stop"
        $RequestFile = Join-Path $Directory "$Label-request.json"
        $ResponseFile = Join-Path $Directory "$Label-response.json"
        $ErrorFile = Join-Path $Directory "$Label-curl-error.txt"
        $ReadyFile = Join-Path $Directory "$Label-ready.txt"

        # Preparar el request antes de anunciar que el trabajador esta listo.
        Set-Content -LiteralPath $RequestFile -Value ("{""monto"":$Monto}") -Encoding ASCII
        Set-Content -LiteralPath $ReadyFile -Value "ready" -Encoding ASCII
        $Deadline = [DateTime]::UtcNow.AddSeconds(30)

        while (-not (Test-Path -LiteralPath $GateFile)) {
            if ([DateTime]::UtcNow -ge $Deadline) {
                throw "${Label}: timeout esperando la barrera."
            }
            Start-Sleep -Milliseconds 10
        }

        $StatusCode = & $Curl --silent --show-error --connect-timeout 10 --max-time 30 `
            -o $ResponseFile -w "%{http_code}" -X POST $Url `
            -H "Content-Type: application/json" -H "X-User-Id: $UserId" `
            --data-binary "@$RequestFile" 2> $ErrorFile
        $CurlExitCode = $LASTEXITCODE

        if ($CurlExitCode -ne 0) {
            $Details = Get-Content -LiteralPath $ErrorFile -Raw
            throw "cURL fallo para $Label (exit $CurlExitCode): $Details"
        }

        [PSCustomObject]@{
            Postor = $Label
            StatusCode = ([string]$StatusCode).Trim()
            Body = [IO.File]::ReadAllText($ResponseFile, [Text.Encoding]::UTF8)
        }
    }

    foreach ($Bidder in @(
        @{ Id = $PostorA; Label = "POSTOR-A" },
        @{ Id = $PostorB; Label = "POSTOR-B" }
    )) {
        $Jobs += Start-Job -ScriptBlock $Worker `
            -ArgumentList $Url, $Bidder.Id, $Monto, $TemporaryDirectory, $GateFile, $Curl, $Bidder.Label
    }

    $ReadyDeadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not ((Test-Path -LiteralPath (Join-Path $TemporaryDirectory "POSTOR-A-ready.txt")) -and
                 (Test-Path -LiteralPath (Join-Path $TemporaryDirectory "POSTOR-B-ready.txt")))) {
        if (@($Jobs | Where-Object State -in @("Failed", "Stopped")).Count -gt 0) {
            throw "Un job fallo antes de estar listo."
        }
        if ([DateTime]::UtcNow -ge $ReadyDeadline) {
            throw "Timeout preparando los dos jobs."
        }
        Start-Sleep -Milliseconds 10
    }

    Set-Content -LiteralPath $GateFile -Value "start" -Encoding ASCII
    $Jobs | Wait-Job -Timeout 45 | Out-Null

    if (@($Jobs | Where-Object State -ne "Completed").Count -gt 0) {
        foreach ($Job in $Jobs) {
            foreach ($FailedJob in @($Job) + @($Job.ChildJobs)) {
                if ($FailedJob.JobStateInfo.Reason) {
                    Write-Warning ($FailedJob.JobStateInfo.Reason.ToString())
                }
            }
        }
        throw "Los dos jobs no finalizaron correctamente dentro del timeout."
    }

    $Results = @($Jobs | Receive-Job -ErrorAction Stop)
    foreach ($Result in $Results) {
        Write-Host "$($Result.Postor) -> HTTP $($Result.StatusCode)"
        Write-Host $Result.Body
    }

    Assert-BidResults -Results $Results -AuctionId $AuctionId -ExpectedAmount $Monto
    Write-Host "PRUEBA EXITOSA: HTTP 200 valido + HTTP 409 BID_CONFLICT."
    $ExitCode = 0
}
catch {
    Write-Warning ("PRUEBA NO CONCLUYENTE O ERROR: " + $_.Exception.Message)
}
finally {
    try {
        foreach ($Job in $Jobs) {
            if ($Job.State -in @("Running", "NotStarted", "Blocked")) {
                Stop-Job -Job $Job
            }
            Remove-Job -Job $Job -Force
        }

        if ($TemporaryDirectory -and (Test-Path -LiteralPath $TemporaryDirectory)) {
            # Solo archivos de este directorio unico; no hay borrado recursivo.
            Get-ChildItem -LiteralPath $TemporaryDirectory -File |
                ForEach-Object { Remove-Item -LiteralPath $_.FullName }
            Remove-Item -LiteralPath $TemporaryDirectory
        }
    }
    catch {
        Write-Warning ("No se pudo completar la limpieza: " + $_.Exception.Message)
        $ExitCode = 1
    }
}

exit $ExitCode
