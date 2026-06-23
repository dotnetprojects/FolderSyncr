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
$darkSettingsScreenshot = Join-Path $repoRoot 'docs\screenshots\foldersyncr-dark-settings.png'
$sampleRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('FolderSyncr.UiSmoke.' + [Guid]::NewGuid().ToString('N'))
$sampleLeft = Join-Path $sampleRoot 'Left'
$sampleRight = Join-Path $sampleRoot 'Right'

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

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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

function Invoke-ElementByHelpWhenEnabled {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$HelpText,
        [string]$Label,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $root = Focus-MainWindow $Process
        $element = Find-ByHelp $root $HelpText
        if ($null -ne $element -and $element.Current.IsEnabled) {
            Invoke-Element $element $Label
            return
        }

        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "$Label was not found enabled."
}

function Wait-ControlTypeCount {
    param(
        [System.Diagnostics.Process]$Process,
        [System.Windows.Automation.ControlType]$ControlType,
        [int]$MinimumCount,
        [string]$Label,
        [int]$TimeoutSeconds = 10
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($items.Count -ge $MinimumCount) {
            return $items
        }

        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "$Label did not appear."
}

function New-SmokeData {
    param(
        [string]$LeftPath,
        [string]$RightPath
    )

    New-Item -ItemType Directory -Path $LeftPath, $RightPath -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $LeftPath 'Docs'), (Join-Path $RightPath 'Docs') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $LeftPath 'Media'), (Join-Path $RightPath 'Media') -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $LeftPath 'Docs\left-only.txt') -Value 'Only on the left side.'
    Set-Content -LiteralPath (Join-Path $RightPath 'Docs\right-only.txt') -Value 'Only on the right side.'
    Set-Content -LiteralPath (Join-Path $LeftPath 'Docs\changed.txt') -Value 'Left version of the changed document.'
    Set-Content -LiteralPath (Join-Path $RightPath 'Docs\changed.txt') -Value 'Right version of the changed document.'
    Set-Content -LiteralPath (Join-Path $LeftPath 'Media\same.bin') -Value 'same payload'
    Set-Content -LiteralPath (Join-Path $RightPath 'Media\same.bin') -Value 'same payload'

    1..40 | ForEach-Object {
        $name = 'Batch\left-file-{0:00}.txt' -f $_
        $path = Join-Path $LeftPath $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value "Left batch file $_"
    }

    (Get-Item -LiteralPath (Join-Path $LeftPath 'Docs\changed.txt')).LastWriteTime = (Get-Date).AddMinutes(-10)
    (Get-Item -LiteralPath (Join-Path $RightPath 'Docs\changed.txt')).LastWriteTime = Get-Date
}

function Focus-MainWindow {
    param(
        [System.Diagnostics.Process]$Process
    )

    [FolderSyncrSmokeNative]::SetForegroundWindow([IntPtr]$Process.MainWindowHandle) | Out-Null
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
    try {
        $root.SetFocus()
    }
    catch [System.Runtime.InteropServices.COMException] {
    }

    return $root
}

function Open-DialogByHelp {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$HelpText,
        [string]$Title,
        [string]$Label
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $root = Focus-MainWindow $Process
        Invoke-Element (Find-ByHelp $root $HelpText) $Label

        try {
            return Wait-WindowTitle -ProcessId $Process.Id -Title $Title -TimeoutSeconds 5
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds 350
        }
    }

    throw "$Title dialog was not found after invoking $Label. Last error: $lastError"
}

function Wait-WindowTitle {
    param(
        [int]$ProcessId,
        [string]$Title,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            Start-Sleep -Milliseconds 150
            continue
        }
        catch [System.Runtime.InteropServices.COMException] {
            Start-Sleep -Milliseconds 150
            continue
        }

        for ($i = 0; $i -lt $windows.Count; $i++) {
            $window = $windows.Item($i)
            try {
                if (($window.Current.ProcessId -eq $ProcessId) -and
                    ($window.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) -and
                    ($window.Current.Name -eq $Title)) {
                    return $window
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
                continue
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

    $handle = $Window.Current.NativeWindowHandle
    $name = $Window.Current.Name
    $pattern = $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $pattern.Close()

    if (Wait-WindowClosed -Handle $handle -TimeoutSeconds 2) {
        return
    }

    $cancelButton = Find-ByName $Window 'Cancel' ([System.Windows.Automation.ControlType]::Button)
    if ($null -ne $cancelButton) {
        Invoke-Element $cancelButton "$name Cancel button"
        if (Wait-WindowClosed -Handle $handle -TimeoutSeconds 3) {
            return
        }
    }

    $okButton = Find-ByName $Window 'OK' ([System.Windows.Automation.ControlType]::Button)
    if ($null -ne $okButton) {
        Invoke-Element $okButton "$name OK button"
        if (Wait-WindowClosed -Handle $handle -TimeoutSeconds 3) {
            return
        }
    }

    throw "Window did not close: $name"
}

function Wait-WindowClosed {
    param(
        [int]$Handle,
        [int]$TimeoutSeconds = 5
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 100
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $condition)

        $found = $false
        for ($i = 0; $i -lt $windows.Count; $i++) {
            try {
                if ($windows.Item($i).Current.NativeWindowHandle -eq $Handle) {
                    $found = $true
                    break
                }
            }
            catch {
                continue
            }
        }

        if (-not $found) {
            return $true
        }
    } while ((Get-Date) -lt $deadline)

    return $false
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

function Capture-Handle {
    param(
        [IntPtr]$Handle,
        [string]$Path
    )

    Set-CaptureForeground $Handle
    $rect = New-Object FolderSyncrSmokeNative+RECT
    [FolderSyncrSmokeNative]::GetWindowRect($Handle, [ref]$rect) | Out-Null

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
    Clear-CaptureTopMost $Handle

    $file = Get-Item $Path
    if ($file.Length -lt 10000) {
        throw "Screenshot looks too small: $Path"
    }
}

function Set-CaptureForeground {
    param(
        [IntPtr]$Handle
    )

    $topMost = [IntPtr]::op_Explicit(-1)
    $flags = 0x0001 -bor 0x0002 -bor 0x0040
    [FolderSyncrSmokeNative]::SetWindowPos($Handle, $topMost, 0, 0, 0, 0, $flags) | Out-Null
    [FolderSyncrSmokeNative]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 250
}

function Clear-CaptureTopMost {
    param(
        [IntPtr]$Handle
    )

    $notTopMost = [IntPtr]::op_Explicit(-2)
    $flags = 0x0001 -bor 0x0002 -bor 0x0040
    [FolderSyncrSmokeNative]::SetWindowPos($Handle, $notTopMost, 0, 0, 0, 0, $flags) | Out-Null
}

function Capture-Rectangle {
    param(
        [double]$Left,
        [double]$Top,
        [double]$Width,
        [double]$Height,
        [string]$Path
    )

    if ($Width -le 0 -or $Height -le 0) {
        throw 'Invalid screenshot rectangle.'
    }

    $bitmap = New-Object System.Drawing.Bitmap([int][Math]::Ceiling($Width), [int][Math]::Ceiling($Height))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int][Math]::Floor($Left), [int][Math]::Floor($Top), 0, 0, $bitmap.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()

    $file = Get-Item $Path
    if ($file.Length -lt 10000) {
        throw "Screenshot looks too small: $Path"
    }
}

function Capture-Window {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Path
    )

    Focus-MainWindow $Process | Out-Null
    Start-Sleep -Milliseconds 250
    Capture-Handle ([IntPtr]$Process.MainWindowHandle) $Path
}

function Capture-AutomationWindow {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$Path
    )

    Capture-Handle ([IntPtr]$Window.Current.NativeWindowHandle) $Path
}

function Scroll-WindowContentToBottom {
    param(
        [System.Windows.Automation.AutomationElement]$Window
    )

    $scrollCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ScrollBar)
    $scrollBars = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $scrollCondition)
    for ($i = 0; $i -lt $scrollBars.Count; $i++) {
        $bar = $scrollBars.Item($i)
        try {
            if ($bar.Current.Orientation -ne [System.Windows.Automation.OrientationType]::Vertical) {
                continue
            }

            $range = $bar.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
            $range.SetValue($range.Current.Maximum)
            Start-Sleep -Milliseconds 250
            return
        }
        catch {
            continue
        }
    }
}

function Capture-AutomationWindowWithDropdown {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$Path
    )

    Set-CaptureForeground ([IntPtr]$Window.Current.NativeWindowHandle)
    $bounds = $Window.Current.BoundingRectangle
    Capture-Rectangle $bounds.Left $bounds.Top $bounds.Width ($bounds.Height + 260) $Path
    Clear-CaptureTopMost ([IntPtr]$Window.Current.NativeWindowHandle)
}

New-SmokeData -LeftPath $sampleLeft -RightPath $sampleRight
$process = Start-Process -FilePath $exe -ArgumentList @('-dirpair', $sampleLeft, $sampleRight) -PassThru
try {
    $root = Wait-MainWindow -Process $process

    Close-Window (Open-DialogByHelp $process 'Comparison settings' 'Synchronization settings' 'Comparison settings button')

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Close-Window (Open-DialogByHelp $process 'Filter files' 'Synchronization settings' 'Filter settings button')

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Close-Window (Open-DialogByHelp $process 'Synchronization settings' 'Synchronization settings' 'Synchronization settings button')

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Close-Window (Open-DialogByHelp $process 'Edit folder pairs' 'Folder pairs' 'Folder pairs button')

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
    Start-Sleep -Milliseconds 250
    $root = Focus-MainWindow $process
    $showOverviewButton = Find-ByHelp $root 'Show Overview pane'
    if ($null -ne $showOverviewButton) {
        Invoke-Element $showOverviewButton 'Show Overview pane button'
    }
    else {
        Expand-Menu (Find-ByName $root 'View' $menuItemType) 'View'
        Invoke-Element (Find-ByName ([System.Windows.Automation.AutomationElement]::RootElement) 'Show Overview' $menuItemType) 'Show Overview menu item'
    }

    Invoke-ElementByHelpWhenEnabled $process 'Compare selected folders' 'Compare button'
    Wait-ControlTypeCount $process ([System.Windows.Automation.ControlType]::ComboBox) 1 'Populated operation action choices' | Out-Null
    Start-Sleep -Milliseconds 300

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

    $fileMenu = Find-ByName ([System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)) 'File' $menuItemType
    $fileMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 200

    $settingsWindow = Open-DialogByHelp $process 'Synchronization settings' 'Synchronization settings' 'Dark synchronization settings button'
    Start-Sleep -Milliseconds 300
    Capture-AutomationWindow $settingsWindow $darkSettingsScreenshot
    Close-Window $settingsWindow

    Write-Host 'FolderSyncr UI smoke passed.'
    Write-Host "Light screenshot: $lightScreenshot"
    Write-Host "Light menu screenshot: $lightMenuScreenshot"
    Write-Host "Dark screenshot: $darkScreenshot"
    Write-Host "Dark settings screenshot: $darkSettingsScreenshot"
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not $process.HasExited) {
            $process.Kill()
        }
    }

    if (Test-Path -LiteralPath $sampleRoot) {
        Remove-Item -LiteralPath $sampleRoot -Recurse -Force
    }
}
