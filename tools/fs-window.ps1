Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms
$w = New-Object System.Windows.Window
$w.WindowStyle = 'None'
$w.WindowState = 'Maximized'
$w.ResizeMode = 'NoResize'
$w.Topmost = $true
$w.ShowInTaskbar = $false
$w.Title = 'FluxWall Fullscreen Test'
$w.Background = [System.Windows.Media.Brushes]::Black
$w.Show()
# Force the rect to span the physical monitor (past the work area / taskbar).
$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$w.WindowState = 'Normal'
$w.Left = $s.X - 8; $w.Top = $s.Y - 8
$w.Width = $s.Width + 16; $w.Height = $s.Height + 16
[System.Windows.Threading.Dispatcher]::Run()
