# LumPanel derleme: .NET SDK'daki Roslyn ile .NET Framework 4.8'e (VDS'te kurulum gerekmez).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sdk = Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object Name -Descending | Select-Object -First 1
$csc = Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
$ref = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$refs = 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.Management','Microsoft.CSharp','System.IO.Compression','System.Security','System.Net.Http' |
    ForEach-Object { "-r:$ref\$_.dll" }
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
$out = Join-Path $root 'dist\LumPanel.exe'
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& dotnet $csc -nologo -noconfig -nostdlib+ -target:winexe -langversion:latest -optimize+ -platform:anycpu -utf8output `
    "-win32icon:$root\app.ico" "-win32manifest:$root\app.manifest" "-out:$out" @refs @src
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız" }
Write-Host "OK -> $out ($([int]((Get-Item $out).Length / 1KB)) KB)"
