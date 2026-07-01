using System.IO.Ports;
using System.Text;
using CutterStudio.Models;

namespace CutterStudio.Services;

/// <summary>
/// Sends ASCII HPGL in bounded blocks so cancellation and progress remain responsive.
/// </summary>
public sealed class SerialCutterService : ISerialCutterService
{
    public IReadOnlyList<string> GetAvailablePorts() =>
        SerialPort.GetPortNames()
            .OrderBy(ParsePortNumber)
            .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task SendAsync(
        CutterSettings settings,
        string hpgl,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var portName = settings.PortName;
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Select a COM port before cutting.", nameof(portName));
        if (string.IsNullOrWhiteSpace(hpgl))
            throw new ArgumentException("The HPGL job is empty.", nameof(hpgl));

        var bytes = Encoding.ASCII.GetBytes(hpgl);
        var handshake = settings.FlowControl.Equals("RTS/CTS", StringComparison.OrdinalIgnoreCase)
            ? Handshake.RequestToSend
            : Handshake.None;
        using var port = new SerialPort(portName, settings.BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = handshake,
            Encoding = Encoding.ASCII,
            WriteTimeout = 5000,
            ReadTimeout = 1000,
            DtrEnable = false
        };

        try
        {
            port.Open();
            port.DiscardOutBuffer();
            port.DiscardInBuffer();
            await Task.Delay(150, cancellationToken);
            const int blockSize = 1024;
            for (var offset = 0; offset < bytes.Length; offset += blockSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(blockSize, bytes.Length - offset);
                await port.BaseStream.WriteAsync(bytes.AsMemory(offset, count), cancellationToken);
                await port.BaseStream.FlushAsync(cancellationToken);
                progress?.Report((offset + count) / (double)bytes.Length);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"{portName} is in use by another application or access was denied.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Communication with {portName} failed. Check the cable, driver, and cutter power.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"The cutter on {portName} did not accept data before the timeout.", ex);
        }
    }

    private static int ParsePortNumber(string port) =>
        int.TryParse(port.AsSpan(3), out var number) ? number : int.MaxValue;
}
