param([switch]$SkipNetwork, [switch]$RenderPopup)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkRoot = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$gacRoot = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$testOutput = Join-Path $projectRoot 'work\tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null

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
$referenceArgs = $references | ForEach-Object { '/reference:' + $_ }
$sources = @(Get-ChildItem (Join-Path $projectRoot 'src\SelectionTranslator') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $projectRoot 'tests\SmokeTests.cs'
$testExe = Join-Path $testOutput ("SelectionTranslator.SmokeTests-" + [Guid]::NewGuid().ToString('N') + '.exe')

& $compiler /nologo /target:exe /platform:anycpu /langversion:5 /main:SelectionTranslator.SmokeTests "/out:$testExe" $referenceArgs $sources
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed with exit code $LASTEXITCODE." }

$arguments = @()
if ($SkipNetwork) { $arguments += '--skip-network' }
if ($RenderPopup) { $arguments += '--render-popup=' + (Join-Path $testOutput 'popup-preview.png') }
& $testExe $arguments
if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed with exit code $LASTEXITCODE." }
