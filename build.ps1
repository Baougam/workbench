# Baut Workbench.exe nur mit Bordmitteln von Windows (.NET Framework 4.8 Compiler) – keine Installation nötig.
#   .\build.ps1            → bin\Workbench.exe
#   .\build.ps1 -Test      → bauen + Selbsttest (im Testordner, fasst nichts Echtes an)
#   .\build.ps1 -Bilder    → bauen + alle Ansichten als PNG nach bin\bilder
param([switch]$Test, [switch]$Bilder)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out  = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$exe  = Join-Path $out 'Workbench.exe'

$ico = Join-Path $out 'workbench.ico'
& (Join-Path $root 'tools\icon.ps1') -Ziel $ico

$refs = @(
  'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
  'System.Web.Extensions.dll', 'Microsoft.CSharp.dll',
  "$fw\System.Xaml.dll", "$fw\WPF\WindowsBase.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\PresentationFramework.dll"
) | ForEach-Object { "/r:$_" }
$src = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /nowarn:1702 /utf8output `
  "/out:$exe" "/win32manifest:$root\app.manifest" "/win32icon:$ico" @refs @src
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen" }
Write-Host "Fertig: $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"

if ($Test) {
  $t = Join-Path $env:TEMP 'workbench-selbsttest'
  $p = Start-Process -FilePath $exe -ArgumentList '--selftest', "`"$t`"" -Wait -PassThru
  Get-Content (Join-Path $t 'selftest.txt') -Encoding UTF8
  if ($p.ExitCode -ne 0) { throw "Selbsttest fehlgeschlagen" }
}
if ($Bilder) {
  $b = Join-Path $out 'bilder'
  $p = Start-Process -FilePath $exe -ArgumentList '--render', "`"$b`"" -Wait -PassThru
  if ($p.ExitCode -ne 0) { throw "Bilder fehlgeschlagen" }
  Get-ChildItem $b -Filter *.png | ForEach-Object { $_.FullName }
}
