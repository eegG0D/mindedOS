using System.IO.Ports;

namespace MindedOS.Sensor;

/// <summary>
/// Streams robot commands out a serial COM port (typically a Bluetooth SPP port
/// paired to a robot or robotic arm). Open the port, then Send each command line;
/// the robot firmware reads newline-terminated commands like LEFT/RIGHT/UP/DOWN.
/// </summary>
public sealed class RobotLink : IDisposable
{
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen == true;
    public string? PortName { get; private set; }

    /// <summary>COM ports currently available (Bluetooth SPP ports appear here once paired).</summary>
    public static string[] AvailablePorts() => SerialPort.GetPortNames();

    public bool Open(string portName, int baud = 9600)
    {
        Close();
        try
        {
            _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                WriteTimeout = 1000,
                NewLine = "\n",
            };
            _port.Open();
            PortName = portName;
            return true;
        }
        catch (Exception)
        {
            _port = null;
            PortName = null;
            return false;
        }
    }

    /// <summary>Send one command line to the robot; returns false if not open or the write failed.</summary>
    public bool Send(string command)
    {
        if (_port?.IsOpen != true) return false;
        try
        {
            _port.Write(MindedOS.Engine.IotCommandMap.Wire(command));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Close()
    {
        try { if (_port?.IsOpen == true) _port.Close(); }
        catch { /* ignore */ }
        _port?.Dispose();
        _port = null;
        PortName = null;
    }

    public void Dispose() => Close();
}
