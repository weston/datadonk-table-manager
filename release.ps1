# Builds DataDonkTM.exe and publishes it as a GitHub release, which the in-app updater picks up.
#
# Usage (from the repo root):   .\release.ps1 0.2.0
#
# Steps: sets <Version> in the .csproj, runs the tests, builds the single-file exe,
# commits + tags vX.Y.Z, pushes, and creates the GitHub release with the exe attached.
# Requires the GitHub CLI (gh) to be logged in.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3" }
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }

$proj = 'src/DataDonkTM/DataDonkTM.csproj'
(Get-Content $proj -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" | Set-Content $proj -NoNewline

dotnet test -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Remove-Item publish\DataDonkTM.pdb -ErrorAction SilentlyContinue

git add -A
git commit -m "Release v$Version"
git tag "v$Version"
git push origin HEAD --tags

$hash = (Get-FileHash publish\DataDonkTM.exe -Algorithm SHA256).Hash
& $gh release create "v$Version" publish\DataDonkTM.exe --title "v$Version" `
    --notes "Download **DataDonkTM.exe** below. New here? See the [installation guide](https://github.com/weston/datadonk-table-manager/blob/main/INSTALL.md).`n`nAlready installed? Click **Update now** in the app.`n`nSHA-256: ``$hash``"
Write-Host "Released v$Version"
