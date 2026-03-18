# Verify if files exist
Write-Host "Verifying files..."

$files = @(
    "LICENSE.txt",
    "ReleaseNotes.md",
    "GettingStarted.md",
    "Resources\README.md"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "✓ $file exists"
    } else {
        Write-Host "✗ $file does not exist"
    }
}

Write-Host "Verification complete!"
