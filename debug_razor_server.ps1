# Debug script to find Razor Language Server
Write-Host "=== Razor Language Server Debug ===" -ForegroundColor Yellow

# Check VS Code installation
Write-Host "`n1. Checking VS Code installation..." -ForegroundColor Cyan
$vscodePath = Get-Command code -ErrorAction SilentlyContinue
if ($vscodePath) {
    Write-Host "✓ VS Code found at: $($vscodePath.Source)" -ForegroundColor Green
} else {
    Write-Host "✗ VS Code not found in PATH" -ForegroundColor Red
}

# Check VS Code extensions directory
Write-Host "`n2. Checking VS Code extensions..." -ForegroundColor Cyan
$userProfile = $env:USERPROFILE
$extensionsPath = "$userProfile\.vscode\extensions"

if (Test-Path $extensionsPath) {
    Write-Host "✓ Extensions directory found: $extensionsPath" -ForegroundColor Green
    
    # Look for C# extensions
    $csharpExtensions = Get-ChildItem $extensionsPath -Directory | Where-Object { 
        $_.Name -like "ms-dotnettools.csharp-*" -or $_.Name -like "ms-dotnettools.csdevkit-*"
    }
    
    if ($csharpExtensions) {
        Write-Host "✓ Found C# extensions:" -ForegroundColor Green
        foreach ($ext in $csharpExtensions) {
            Write-Host "  - $($ext.Name)" -ForegroundColor White
            
            # Check for rzls.exe in this extension
            $razorDir = Join-Path $ext.FullName ".razor"
            $rzlsPath = Join-Path $razorDir "rzls.exe"
            
            if (Test-Path $rzlsPath) {
                Write-Host "    ✓ Found rzls.exe at: $rzlsPath" -ForegroundColor Green
                
                # Test if executable works
                try {
                    $version = & $rzlsPath --version 2>$null
                    Write-Host "    ✓ rzls.exe is working, version: $version" -ForegroundColor Green
                } catch {
                    Write-Host "    ✗ rzls.exe found but not working: $($_.Exception.Message)" -ForegroundColor Red
                }
            } else {
                Write-Host "    ✗ No rzls.exe in .razor directory" -ForegroundColor Yellow
                
                # Check alternative locations
                $langServerDir = Join-Path $ext.FullName "languageserver"
                $altRzlsPath = Join-Path $langServerDir "rzls.exe"
                if (Test-Path $altRzlsPath) {
                    Write-Host "    ✓ Found rzls.exe in languageserver directory: $altRzlsPath" -ForegroundColor Green
                }
            }
        }
    } else {
        Write-Host "✗ No C# extensions found" -ForegroundColor Red
    }
} else {
    Write-Host "✗ VS Code extensions directory not found: $extensionsPath" -ForegroundColor Red
}

# Check for VS Code Insiders
Write-Host "`n3. Checking VS Code Insiders..." -ForegroundColor Cyan
$insidersPath = "$userProfile\.vscode-insiders\extensions"
if (Test-Path $insidersPath) {
    Write-Host "✓ VS Code Insiders extensions found: $insidersPath" -ForegroundColor Green
}

# Check for rzls as global tool
Write-Host "`n4. Checking for rzls as dotnet global tool..." -ForegroundColor Cyan
try {
    $rzlsGlobal = Get-Command rzls -ErrorAction SilentlyContinue
    if ($rzlsGlobal) {
        Write-Host "✓ rzls found as global tool: $($rzlsGlobal.Source)" -ForegroundColor Green
    } else {
        Write-Host "✗ rzls not found as global tool" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Error checking for global rzls: $($_.Exception.Message)" -ForegroundColor Red
}

# Manual search for rzls.exe anywhere
Write-Host "`n5. Manual search for rzls.exe..." -ForegroundColor Cyan
Write-Host "Searching in common locations..."

$searchPaths = @(
    "C:\Program Files\Microsoft VS Code",
    "C:\Users\$env:USERNAME\AppData\Local\Programs\Microsoft VS Code",
    $userProfile
)

foreach ($searchPath in $searchPaths) {
    if (Test-Path $searchPath) {
        Write-Host "Searching in: $searchPath" -ForegroundColor Gray
        try {
            $found = Get-ChildItem $searchPath -Recurse -Name "rzls.exe" -ErrorAction SilentlyContinue | Select-Object -First 5
            if ($found) {
                foreach ($f in $found) {
                    $fullPath = Join-Path $searchPath $f
                    Write-Host "  ✓ Found: $fullPath" -ForegroundColor Green
                }
            }
        } catch {
            Write-Host "  ✗ Search failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "`n=== Debug Complete ===" -ForegroundColor Yellow
Write-Host "Please share this output to help diagnose the issue." -ForegroundColor White