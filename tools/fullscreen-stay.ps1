Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$w = New-Object System.Windows.Window -Property @{
  WindowStyle = 'None'
  WindowState = 'Maximized'
  Background = [System.Windows.Media.Brushes]::DarkGreen
  Title = 'Fullscreen Test'
}
$t = New-Object System.Windows.Controls.TextBlock
$t.Text = 'FULLSCREEN TEST APP'
$t.FontSize = 40
$t.Foreground = [System.Windows.Media.Brushes]::White
$w.Content = $t
$w.Show()
[System.Windows.Threading.Dispatcher]::Run()
