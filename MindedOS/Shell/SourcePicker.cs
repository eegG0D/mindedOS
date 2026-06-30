using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using MindedOS.Sensor;

namespace MindedOS.Shell;

/// <summary>
/// Modal shown by the Connect button: choose the simulate (synthetic) board or
/// a real OpenBCI headset on a COM port. Returns a configured
/// <see cref="OpenBciLink"/>, or null if the user cancels.
/// </summary>
public static class SourcePicker
{
    public static OpenBciLink? Choose(Window owner)
    {
        var simulate = new RadioButton
        {
            Content = "Simulate — BrainFlow synthetic board (no hardware)",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var real = new RadioButton
        {
            Content = "Real OpenBCI headset",
            Margin = new Thickness(0, 0, 0, 8),
        };

        var boardBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        boardBox.Items.Add("Cyton + Daisy (16 channels)");
        boardBox.Items.Add("Cyton (8 channels)");
        boardBox.Items.Add("Ganglion (4 channels)");
        boardBox.SelectedIndex = 0;

        var portBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var p in SerialPort.GetPortNames()) portBox.Items.Add(p);
        if (portBox.Items.Count > 0) portBox.SelectedIndex = 0;

        var hint = new TextBlock
        {
            Text = portBox.Items.Count == 0 ? "No COM ports found — plug in the dongle." : "Select the dongle's COM port.",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var realPanel = new StackPanel { Margin = new Thickness(22, 0, 0, 0), IsEnabled = false };
        realPanel.Children.Add(new TextBlock { Text = "Board", Margin = new Thickness(0, 0, 0, 2) });
        realPanel.Children.Add(boardBox);
        realPanel.Children.Add(new TextBlock { Text = "Port", Margin = new Thickness(0, 0, 0, 2) });
        realPanel.Children.Add(portBox);
        realPanel.Children.Add(hint);

        real.Checked += (_, _) => realPanel.IsEnabled = true;
        simulate.Checked += (_, _) => realPanel.IsEnabled = false;

        var connectBtn = new Button { Content = "Connect", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new Button { Content = "Cancel", IsCancel = true, Width = 90 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        buttons.Children.Add(connectBtn);
        buttons.Children.Add(cancelBtn);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Choose EEG source", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(simulate);
        root.Children.Add(real);
        root.Children.Add(realPanel);
        root.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Connect",
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            MinWidth = 360,
        };

        OpenBciLink? result = null;
        connectBtn.Click += (_, _) =>
        {
            if (real.IsChecked == true)
            {
                var port = portBox.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(port))
                {
                    hint.Text = "Pick a COM port first.";
                    hint.Foreground = System.Windows.Media.Brushes.IndianRed;
                    return;
                }
                result = boardBox.SelectedIndex switch
                {
                    1 => OpenBciLink.Cyton(port),
                    2 => OpenBciLink.Ganglion(port),
                    _ => OpenBciLink.CytonDaisy(port),
                };
            }
            else
            {
                result = OpenBciLink.Simulated();
            }
            dialog.DialogResult = true;
        };

        return dialog.ShowDialog() == true ? result : null;
    }
}
