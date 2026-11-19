param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools/dotnet10/dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $projectRoot
try {
    foreach ($project in @('ConstructionSaaS.API', 'ConstructionSaaS.Web')) {
        $destination = Join-Path $projectRoot ('artifacts/publish/' + $project)
        & $sdkCommand publish "$project/$project.csproj" -c $Configuration --no-restore -o $destination -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:UseAppHost=false
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project." }
    }
    Write-Host 'Publish artifacts ready under artifacts/publish. Configure production secrets and verify health checks before routing traffic.'
} finally { Pop-Location }
