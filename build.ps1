[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path $compiler)) { throw '.NET Framework 4.8 is required.' }

$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'NailongDesktop.exe'
$references = @('WindowsBase', 'PresentationCore', 'PresentationFramework') | ForEach-Object {
    '/reference:' + (Join-Path $framework "WPF\$_.dll")
}
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName
$icon = Join-Path $PSScriptRoot 'media\nailong.ico'
$model = Join-Path $PSScriptRoot 'media\model.bin'
$texture = Join-Path $PSScriptRoot 'media\model-albedo.png'
$laugh = Join-Path $PSScriptRoot 'media\laugh.wav'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 "/out:$exe" `
    "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$icon" "/resource:$model,Nailong.Model" "/resource:$texture,Nailong.Texture" `
    "/resource:$laugh,Nailong.Laugh.wav" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Xaml.dll @references @sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Write-Output $exe
