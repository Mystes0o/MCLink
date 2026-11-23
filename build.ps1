$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /codepage:65001 /target:winexe /out:MCLink.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Main.cs Room.cs Session.cs
    if ($LASTEXITCODE) { throw 'MCLink build failed' }
} finally { Pop-Location }
