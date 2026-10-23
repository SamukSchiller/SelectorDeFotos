# Cierra Selector de Fotos (guardando), compila y lo vuelve a abrir si estaba abierto.
#   powershell -ExecutionPolicy Bypass -File actualizar.ps1            -> reabre solo si estaba abierta
#   powershell -ExecutionPolicy Bypass -File actualizar.ps1 -Abrir     -> la abre siempre al terminar
param([switch]$Abrir)

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $dir "SelectorDeFotos.exe"

$procs = @(Get-Process SelectorDeFotos -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
$estabaAbierta = $procs.Count -gt 0

if ($estabaAbierta) {
    # Cierre normal (como pulsar la X): la app guarda marcas y ajustes al cerrarse.
    foreach ($p in $procs) { [void]$p.CloseMainWindow() }
    $limite = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $limite -and @(Get-Process -Id ($procs.Id) -ErrorAction SilentlyContinue).Count -gt 0) {
        Start-Sleep -Milliseconds 200
    }
    if (@(Get-Process -Id ($procs.Id) -ErrorAction SilentlyContinue).Count -gt 0) {
        Write-Output "NO SE CERRO: la app sigue abierta (quizas hay un dialogo abierto). No se compilo nada."
        exit 2
    }
    Write-Output "App cerrada correctamente."
}

# Windows puede tardar un momento en soltar el .exe tras cerrar la app: esperar (hasta 5 s) a poder escribirlo
$limite = (Get-Date).AddSeconds(5)
while ((Test-Path $exe) -and (Get-Date) -lt $limite) {
    try { $f = [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None'); $f.Close(); break }
    catch { Start-Sleep -Milliseconds 200 }
}

& cmd /c "`"$dir\build.cmd`""
if ($LASTEXITCODE -ne 0) {
    Write-Output "ERROR de compilacion."
    if (($estabaAbierta -or $Abrir) -and (Test-Path $exe)) { Start-Process $exe; Write-Output "Se reabrio la version anterior." }
    exit 1
}

if ($estabaAbierta -or $Abrir) {
    Start-Process $exe
    Write-Output "App abierta de nuevo."
} else {
    Write-Output "La app no estaba abierta: no se abre."
}
