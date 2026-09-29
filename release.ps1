# Publishes a new version.  Usage (from the repo root):  .\release.ps1 0.2.0
#
# Sets the version, runs the tests, commits, tags vX.Y.Z and pushes. GitHub then builds
# DataDonkTM.exe, signs a record of which commit it was built from, and publishes the release
# (see .github/workflows/release.yml). Apps show the update bar within 12 hours.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3" }

$proj = 'src/DataDonkTM/DataDonkTM.csproj'
(Get-Content $proj -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" | Set-Content $proj -NoNewline

dotnet test -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

git add -A
git commit -m "Release v$Version"
git tag "v$Version"
git push origin HEAD "v$Version"
Write-Host "Pushed v$Version. GitHub is building it: https://github.com/weston/datadonk-table-manager/actions"
