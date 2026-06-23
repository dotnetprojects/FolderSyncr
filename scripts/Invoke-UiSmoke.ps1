param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'FolderSyncr.slnx'
$exe = Join-Path $repoRoot 'FolderSyncr\bin\Debug\net10.0-windows\FolderSyncr.exe'
$lightScreenshot = Join-Path $repoRoot 'docs\screenshots\foldersyncr-light.png'
$lightMenuScreenshot = Join-Path $repoRoot 'docs\screenshots\foldersyncr-light-menu.png'
$darkScreenshot = Join-Path $repoRoot 'docs\screenshots\foldersyncr-dark.png'

if (-not $SkipBuild) {
    dotnet build $solution
}

Get-Process FolderSyncr -ErrorAction SilentlyContinue | Stop-Process -Force

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

if (-not ('FolderSyncrSmokeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class FolderSyncrSmokeNative
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@
}

function Wait-MainWindow {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $Process.Refresh()
    } while ($Process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)

    if ($Process.MainWindowHandle -eq 0) {
        throw 'FolderSyncr main window did not appear.'
    }

    return [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
}

function Find-ByHelp {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$HelpText
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::HelpTextProperty,
        $HelpText)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType
    )

    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    if ($ControlType) {
        $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ControlType)
        $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    }
    else {
        $condition = $nameCondition
    }

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Label
    )

    if ($null -eq $Element) {
        throw "$Label was not found."
    }

    $patterns = $Element.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }
    if ($patterns -contains 'InvokePatternIdentifiers.Pattern') {
        $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        return
    }

    throw "$Label does not expose InvokePattern. Patterns: $($patterns -join ', ')"
}

function Wait-WindowTitle {
    param(
        [int]$ProcessId,
        [string]$Title,
        [int]$TimeoutSeconds = 5
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)

        for ($i = 0; $i -lt $windows.Count; $i++) {
            $window = $windows.Item($i)
            if (($window.Current.ProcessId -eq $ProcessId) -and
                ($window.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) -and
                ($window.Current.Name -eq $Title)) {
                return $window
            }
        }

        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)

    throw "$Title dialog was not found."
}

function Close-Window {
    param(
        [System.Windows.Automation.AutomationElement]$Window
    )

    $pattern = $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $pattern.Close()
}

function Expand-Menu {
    param(
        [System.Windows.Automation.AutomationElement]$MenuItem,
        [string]$Label
    )

    if ($null -eq $MenuItem) {
        throw "$Label menu was not found."
    }

    $pattern = $MenuItem.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        $pattern.Expand()
    }
}

function Capture-Window {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Path
    )

    $rect = New-Object FolderSyncrSmokeNative+RECT
    [FolderSyncrSmokeNative]::GetWindowRect($Process.MainWindowHandle, [ref]$rect) | Out-Null

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw 'Invalid window rectangle for screenshot.'
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()

    $file = Get-Item $Path
    if ($file.Length -lt 10000) {
        throw "Screenshot looks too small: $Path"
    }
}

$process = Start-Process -FilePath $exe -PassThru
try {
    $root = Wait-MainWindow -Process $process

    Invoke-Element (Find-ByHelp $root 'Comparison settings') 'Comparison settings button'
    Close-Window (Wait-WindowTitle -ProcessId $process.Id -Title 'Comparison settings')

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Invoke-Element (Find-ByHelp $root 'Filter files') 'Filter files button'
    Close-Window (Wait-WindowTitle -ProcessId $process.Id -Title 'File filters')

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Invoke-Element (Find-ByHelp $root 'Cloud path') 'Cloud path button'
    Invoke-Element (Find-ByHelp $root 'Swap sides') 'Swap sides button'
    Invoke-Element (Find-ByHelp $root 'Close Configuration pane') 'Close Configuration pane button'

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $menuItemType = [System.Windows.Automation.ControlType]::MenuItem
    Expand-Menu (Find-ByName $root 'View' $menuItemType) 'View'
    Invoke-Element (Find-ByName ([System.Windows.Automation.AutomationElement]::RootElement) 'Show Configuration' $menuItemType) 'Show Configuration menu item'

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Invoke-Element (Find-ByHelp $root 'Close Overview pane') 'Close Overview pane button'
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Invoke-Element (Find-ByHelp $root 'Show Overview pane') 'Show Overview pane button'

    Capture-Window $process $lightScreenshot

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Expand-Menu (Find-ByName $root 'View' $menuItemType) 'View'
    Start-Sleep -Milliseconds 300
    Capture-Window $process $lightMenuScreenshot

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Invoke-Element (Find-ByHelp $root 'Switch between light and dark mode') 'Theme button'
    Start-Sleep -Milliseconds 500

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Expand-Menu (Find-ByName $root 'File' $menuItemType) 'File'
    Start-Sleep -Milliseconds 300
    Capture-Window $process $darkScreenshot

    Write-Host 'FolderSyncr UI smoke passed.'
    Write-Host "Light screenshot: $lightScreenshot"
    Write-Host "Light menu screenshot: $lightMenuScreenshot"
    Write-Host "Dark screenshot: $darkScreenshot"
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not $process.HasExited) {
            $process.Kill()
        }
    }
}
