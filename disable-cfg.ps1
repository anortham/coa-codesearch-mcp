# Disable Control Flow Guard and Shadow Stack for COA.CodeNav.McpServer.exe
Write-Host "Disabling CFG and Shadow Stack for COA.CodeNav.McpServer.exe..." -ForegroundColor Yellow

try {
    # Disable CFG and UserShadowStack
    Set-ProcessMitigation -Name COA.CodeNav.McpServer.exe -Disable CFG,UserShadowStack
    Write-Host "Successfully disabled CFG and Shadow Stack!" -ForegroundColor Green
    
    # Show current settings
    Write-Host "`nCurrent mitigation settings:" -ForegroundColor Cyan
    Get-ProcessMitigation -Name COA.CodeNav.McpServer.exe
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "`nPlease run this script as Administrator!" -ForegroundColor Yellow
}

Write-Host "`nPress any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")