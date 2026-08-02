# Replace namespaces after moving Models and Migrations
# Run from repo root: .\scripts\update-namespaces.ps1
# This script performs targeted, reversible text replacements. Review before running.
# It will:
#  - Change model namespaces from Bookmarkarr.Api.Models -> Bookmarkarr.Domain.Models (in bookmarkarr.domain/Models)
#  - Replace using Bookmarkarr.Api.Models -> using Bookmarkarr.Domain.Models across the repo
#  - Update the Infrastructure DbContext namespace to Bookmarkarr.Infrastructure.Models
#  - Update migration namespaces from Bookmarkarr.Api.Migrations -> Bookmarkarr.Infrastructure.Migrations
#
# Note: This script edits files in-place. It's recommended to run in a clean git working tree so you can inspect & commit changes.

Set-StrictMode -Version Latest

Write-Host "Updating model file namespaces in bookmarkarr.domain/Models..."
Get-ChildItem -Path .\bookmarkarr.domain\Models -Filter *.cs -File -Recurse | ForEach-Object {
    (Get-Content -Raw -LiteralPath $_.FullName) -replace 'namespace\s+Bookmarkarr\.Api\.Models', 'namespace Bookmarkarr.Domain.Models' |
        Set-Content -LiteralPath $_.FullName -Encoding UTF8
    Write-Host "Updated namespace in $($_.FullName)"
}

Write-Host "Updating using directives across repository: Bookmarkarr.Api.Models -> Bookmarkarr.Domain.Models"
Get-ChildItem -Path . -Filter *.cs -File -Recurse | ForEach-Object {
    $path = $_.FullName
    $content = Get-Content -Raw -LiteralPath $path
    if ($content -match 'using\s+Bookmarkarr\.Api\.Models') {
        $new = $content -replace 'using\s+Bookmarkarr\.Api\.Models', 'using Bookmarkarr.Domain.Models'
        Set-Content -LiteralPath $path -Value $new -Encoding UTF8
        Write-Host "Replaced using in $path"
    }
}

Write-Host "Updating DbContext namespace in bookmarkarr.infrastructure/Models/BookmarkarrDbContext.cs..."
$dbcPath = ".\bookmarkarr.infrastructure\Models\BookmarkarrDbContext.cs"
if (Test-Path $dbcPath) {
    $dbcContent = Get-Content -Raw -LiteralPath $dbcPath
    # Ensure model types are referenced from Bookmarkarr.Domain.Models via using
    if ($dbcContent -notmatch 'using Bookmarkarr.Domain.Models') {
        $dbcContent = "using Bookmarkarr.Domain.Models`r`n" + $dbcContent
    }
    # Replace whatever namespace it has now to the infrastructure namespace
    $dbcContent = $dbcContent -replace 'namespace\s+Bookmarkarr\.Api\.Models', 'namespace Bookmarkarr.Infrastructure.Models'
    $dbcContent = $dbcContent -replace 'namespace\s+Bookmarkarr\.Domain\.Models', 'namespace Bookmarkarr.Infrastructure.Models'
    Set-Content -LiteralPath $dbcPath -Value $dbcContent -Encoding UTF8
    Write-Host "Updated DbContext namespace in $dbcPath"
} else {
    Write-Host "DbContext file not found at $dbcPath - skipping"
}

Write-Host "Updating migration namespaces in bookmarkarr.infrastructure/Migrations..."
Get-ChildItem -Path .\bookmarkarr.infrastructure\Migrations -Filter *.cs -File -Recurse | ForEach-Object {
    $p = $_.FullName
    $c = Get-Content -Raw -LiteralPath $p
    if ($c -match 'namespace\s+Bookmarkarr\.Api\.Migrations') {
        $new = $c -replace 'namespace\s+Bookmarkarr\.Api\.Migrations', 'namespace Bookmarkarr.Infrastructure.Persistence.Migrations'
        Set-Content -LiteralPath $p -Value $new -Encoding UTF8
        Write-Host "Updated migration namespace in $p"
    }
}

Write-Host "Namespace updates complete. Please run 'git status' to review changes, then 'dotnet build' to verify."
