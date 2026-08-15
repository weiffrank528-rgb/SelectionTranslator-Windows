param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$StageOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'src\SelectionTranslator'
$outputRoot = Join-Path $projectRoot "outputs\SelectionTranslator-$Configuration"
$stagingRoot = Join-Path $projectRoot ("work\build\" + [Guid]::NewGuid().ToString('N'))
$frameworkRoot = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$gacRoot = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework C# compiler was not found. Install Visual Studio Build Tools or the .NET Framework 4.8 developer pack.'
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$references = @(
    (Join-Path $frameworkRoot 'System.dll'),
    (Join-Path $frameworkRoot 'System.Core.dll'),
    (Join-Path $frameworkRoot 'System.Drawing.dll'),
    (Join-Path $frameworkRoot 'System.Net.Http.dll'),
    (Join-Path $frameworkRoot 'System.Security.dll'),
    (Join-Path $frameworkRoot 'System.Web.dll'),
    (Join-Path $frameworkRoot 'System.Web.Extensions.dll'),
    (Join-Path $frameworkRoot 'System.Windows.Forms.dll'),
    (Get-ChildItem (Join-Path $gacRoot 'System.Speech') -Filter System.Speech.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName),
    (Get-ChildItem (Join-Path $gacRoot 'UIAutomationClient') -Filter UIAutomationClient.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName),
    (Get-ChildItem (Join-Path $gacRoot 'UIAutomationTypes') -Filter UIAutomationTypes.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName),
    (Get-ChildItem (Join-Path $gacRoot 'WindowsBase') -Filter WindowsBase.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName)
)

$sourceFiles = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
$define = if ($Configuration -eq 'Debug') { '/define:DEBUG;TRACE' } else { '/define:TRACE' }
$debugArgs = if ($Configuration -eq 'Debug') { @('/debug:full', '/optimize-') } else { @('/debug:pdbonly', '/optimize+') }
$referenceArgs = $references | ForEach-Object { '/reference:' + $_ }
$outputExe = Join-Path $outputRoot 'SelectionTranslator.exe'
$stagedExe = Join-Path $stagingRoot 'SelectionTranslator.exe'

& $compiler /nologo /target:winexe /platform:anycpu /langversion:5 $define $debugArgs "/out:$stagedExe" $referenceArgs $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }

$stagedPdb = [IO.Path]::ChangeExtension($stagedExe, '.pdb')
if ($StageOnly) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stagingRoot -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingRoot -Force
    Write-Host "Staged: $stagedExe"
    return
}

Copy-Item -LiteralPath $stagedExe -Destination $outputExe -Force
if (Test-Path -LiteralPath $stagedPdb) {
    Copy-Item -LiteralPath $stagedPdb -Destination ([IO.Path]::ChangeExtension($outputExe, '.pdb')) -Force
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputRoot -Force
Write-Host "Built: $outputExe"
