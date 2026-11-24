param(
    [string]$SdkRoot = $env:ANDROID_HOME
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $project 'runtime/android-tools'
if (!$SdkRoot) { $SdkRoot = Join-Path $tools 'sdk' }
$jniDir = Join-Path $tools 'jni/arm64-v8a'
$jni = Join-Path $jniDir 'libeasytier_android_jni.so'
$expected = 'FC0AED1D095D53434CB77E6EA239CCEE9928C1C0EB37917B2D56F6E8D5404DCF'
$url = 'https://raw.githubusercontent.com/originalFactor/EasyTierCompose/v1.0.10/app/src/main/jniLibs/arm64-v8a/libeasytier_android_jni.so'

New-Item -Force -ItemType Directory $jniDir | Out-Null
if (!(Test-Path -LiteralPath $jni) -or (Get-FileHash -LiteralPath $jni).Hash -ne $expected) {
    $part = "$jni.part"
    if ((Test-Path -LiteralPath $part) -and (Get-Item -LiteralPath $part).Length -ge 17340944) {
        if ((Get-FileHash -LiteralPath $part).Hash -ne $expected) { Remove-Item -LiteralPath $part -Force }
    }
    if (!(Test-Path -LiteralPath $part) -or (Get-FileHash -LiteralPath $part).Hash -ne $expected) {
        & curl.exe --fail --location --retry 2 --max-time 300 --continue-at - --output $part $url
    }
    if (!(Test-Path -LiteralPath $part) -or (Get-FileHash -LiteralPath $part).Hash -ne $expected) {
        throw 'EasyTier library download did not complete or failed SHA-256 verification.'
    }
    Move-Item -Force -LiteralPath $part -Destination $jni
}

if (!(Test-Path -LiteralPath (Join-Path $SdkRoot 'platforms/android-35/android.jar')) -or
    !(Test-Path -LiteralPath (Join-Path $SdkRoot 'build-tools/35.0.0/aapt2.exe'))) {
    $bundledManager = Join-Path $tools 'cmdline-tools/bin/sdkmanager.bat'
    $manager = Get-Command sdkmanager.bat -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $bundledManager) { $manager = $bundledManager }
    elseif ($manager) { $manager = $manager.Source }
    else { throw 'Install the Android SDK command-line tools and put sdkmanager.bat on PATH.' }
    & $manager "--sdk_root=$SdkRoot" 'platforms;android-35' 'build-tools;35.0.0' 'platform-tools'
    if ($LASTEXITCODE -ne 0) { throw 'Android SDK setup failed.' }
}
