[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Rid = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\NutManager.App\NutManager.App.csproj'
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$Rid"
$packageDirectory = Join-Path $repositoryRoot 'artifacts\packages'
$packageName = if ($Rid -eq 'win-x64') { 'NutManager-win-x64.zip' } else { 'NutManager-linux-x64.tar.gz' }
$packagePath = Join-Path $packageDirectory $packageName
$executableName = if ($Rid -eq 'win-x64') { 'NutManager.App.exe' } else { 'NutManager.App' }

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
    --runtime $Rid `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish falhou para $Rid."
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $executableName)))
{
    throw "O executável publicado esperado não foi encontrado: $executableName"
}

if ($Rid -eq 'win-x64')
{
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $packagePath -CompressionLevel Optimal -Force
}
else
{
    & tar -czf $packagePath -C $publishDirectory .
    if ($LASTEXITCODE -ne 0)
    {
        throw 'A criação do pacote tar.gz falhou.'
    }
}

Write-Host "Pacote criado: $packagePath"
