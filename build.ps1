$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$windowsBase = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'
$presentationCore = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll'

foreach ($needed in @($compiler, $windowsBase, $presentationCore)) {
    if (-not (Test-Path -LiteralPath $needed)) { throw "missing .net framework file: $needed" }
}

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$app = Join-Path $dist 'lock in.exe'
$icon = Join-Path $PSScriptRoot 'studying.ico'
$photo = Join-Path $PSScriptRoot 'studying-photo.png'
$main = Join-Path $PSScriptRoot 'studying.cs'
$ui = Join-Path $PSScriptRoot 'ReferenceUi.cs'

& $compiler /nologo /target:winexe "/out:$app" "/win32icon:$icon" "/resource:$photo,StudyingPhoto" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll `
    "/reference:$windowsBase" "/reference:$presentationCore" $main $ui

if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Write-Output "built $app"
