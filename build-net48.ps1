param(
    [switch]$NoZip
)

& (Join-Path $PSScriptRoot 'build.ps1') -NoZip:$NoZip
exit $LASTEXITCODE
