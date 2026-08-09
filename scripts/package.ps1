[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\NutManager.App\NutManager.App.csproj'
$rid = 'win-x64'
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$rid"
$packageDirectory = Join-Path $repositoryRoot 'artifacts\packages'
$packagePath = Join-Path $packageDirectory 'NutManager-win-x64.zip'
$executableName = 'NutManager.App.exe'

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand)
{
    $dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath))
    {
        throw 'O SDK .NET não foi encontrado.'
    }
}
else
{
    $dotnetPath = $dotnetCommand.Source
}

if (Test-Path -LiteralPath $publishDirectory)
{
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDirectory, $packageDirectory | Out-Null
Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue

& $dotnetPath publish $project `
    --configuration Release `
    --runtime $rid `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish falhou para $rid."
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $executableName)))
{
    throw "O executável publicado esperado não foi encontrado: $executableName"
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $packagePath -CompressionLevel Optimal -Force

Write-Host "Pacote criado: $packagePath"
