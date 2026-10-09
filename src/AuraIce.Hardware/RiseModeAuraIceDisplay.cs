using AuraIce.Core.Protocol;
using HidSharp;

namespace AuraIce.Hardware;

public sealed record HidDisplayInfo(string Path, int VendorId, int ProductId, int OutputReportLength);

public sealed class RiseModeAuraIceDisplay : IDisposable
{
    private readonly int _vendorId;
    private readonly int _productId;
    private HidDevice? _device;
    private HidStream? _stream;

    public RiseModeAuraIceDisplay(int vendorId = 0xAA88, int productId = 0x8666)
    {
        _vendorId = vendorId;
        _productId = productId;
    }

    public bool IsConnected => _stream is not null;

    public IReadOnlyList<HidDisplayInfo> Discover()
    {
        return DeviceList.Local
            .GetHidDevices(_vendorId, _productId)
            .Select(device => new HidDisplayInfo(
                device.DevicePath,
                device.VendorID,
                device.ProductID,
                device.GetMaxOutputReportLength()))
            .ToArray();
    }

    public bool TryConnect(out string error)
    {
        Disconnect();

        foreach (HidDevice device in DeviceList.Local.GetHidDevices(_vendorId, _productId))
        {
            if (device.TryOpen(out HidStream stream))
            {
                _device = device;
                _stream = stream;
                error = string.Empty;
                return true;
            }
        }

        error = $"Nenhum HID Rise Mode encontrado (VID 0x{_vendorId:X4}, PID 0x{_productId:X4}).";
        return false;
    }

    public void WriteTemperature(decimal celsius)
    {
        if (_stream is null || _device is null)
        {
            throw new InvalidOperationException("O display não está conectado.");
        }

        int reportLength = _device.GetMaxOutputReportLength();
        if (reportLength < TemperatureDisplayPayload.MinimumHidReportLength)
        {
            throw new InvalidOperationException(
                $"O HID informa relatório de saída de {reportLength} bytes; eram esperados pelo menos {TemperatureDisplayPayload.MinimumHidReportLength}, incluindo o byte de Report ID.");
        }

        // Os dados do cooler começam após o byte Report ID exigido pelo HidSharp.
        // O relatório pode ser menor que o payload documentado; nesse caso,
        // somente os bytes que cabem são enviados e o restante é preenchido com zeros.
        byte[] report = TemperatureDisplayPayload.CreateHidOutputReport(celsius, reportLength);
        _stream.Write(report, 0, report.Length);
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _stream = null;
        _device = null;
    }

    public void Dispose() => Disconnect();
}
