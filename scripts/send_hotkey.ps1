param([string]$Keys = "^%f")
Add-Type -AssemblyName System.Windows.Forms
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Write-Host "Sent: $Keys"
