using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AuraIce.Core.Configuration;
using AuraIce.Core.Status;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;
using MediaPoint = System.Windows.Point;
using MediaSize = System.Windows.Size;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfPath = System.Windows.Shapes.Path;

namespace AuraIce.App;

public partial class MainWindow : Window
{
    private const double GaugeCenter = 160;
    private const double GaugeTrackRadius = 126;
    private const double GaugeStartAngle = 135;
    private const double GaugeSweepAngle = 270;
    private const int GaugeTickCount = 60;

    private static readonly MediaBrush OnlineBrush = new SolidColorBrush(MediaColor.FromRgb(91, 220, 177));
    private static readonly MediaBrush OfflineBrush = new SolidColorBrush(MediaColor.FromRgb(237, 157, 110));
    private static readonly MediaBrush UnknownBrush = new SolidColorBrush(MediaColor.FromRgb(113, 129, 150));
    private static readonly MediaBrush ActiveGaugeBrush = new SolidColorBrush(MediaColor.FromRgb(100, 213, 239));
    private static readonly MediaBrush InactiveGaugeBrush = new SolidColorBrush(MediaColor.FromRgb(43, 64, 86));
    private static readonly MediaBrush GaugeProgressBrush = new LinearGradientBrush(
        MediaColor.FromRgb(74, 182, 212),
        MediaColor.FromRgb(114, 216, 238),
        new MediaPoint(0, 1),
        new MediaPoint(1, 0));

    private readonly AuraIceSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _allowExit;
    private bool _isRefreshing;
    private bool _settingsLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public void AllowExit() => _allowExit = true;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSettingsAsync();
        await RefreshAsync();
        _refreshTimer.Start();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
        if (SettingsPopup.IsOpen)
        {
            await LoadSettingsAsync();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int interval) ||
            interval is < 250 or > 10_000)
        {
            SettingsMessageText.Text = "Use um valor entre 250 e 10000 ms.";
            SettingsMessageText.Foreground = OfflineBrush;
            return;
        }

        try
        {
            AuraIceSettings current = await _settingsStore.LoadAsync();
            await _settingsStore.SaveAsync(current with { UpdateIntervalMilliseconds = interval });
            _settingsLoaded = true;
            SettingsMessageText.Text = "Salvo. O serviço aplicará no próximo ciclo.";
            SettingsMessageText.Foreground = OnlineBrush;
        }
        catch (UnauthorizedAccessException)
        {
            SettingsMessageText.Text = "Sem permissão para salvar as configurações.";
            SettingsMessageText.Foreground = OfflineBrush;
        }
        catch (IOException)
        {
            SettingsMessageText.Text = "Não foi possível gravar as configurações.";
            SettingsMessageText.Foreground = OfflineBrush;
        }
    }

    private async Task LoadSettingsAsync()
    {
        if (_settingsLoaded)
        {
            return;
        }

        try
        {
            AuraIceSettings settings = await _settingsStore.LoadAsync();
            IntervalTextBox.Text = settings.UpdateIntervalMilliseconds.ToString(CultureInfo.CurrentCulture);
            SettingsMessageText.Text = "Use um valor entre 250 e 10000 ms.";
            SettingsMessageText.Foreground = UnknownBrush;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            IntervalTextBox.Text = "1000";
            SettingsMessageText.Text = "Não foi possível carregar as configurações.";
            SettingsMessageText.Foreground = OfflineBrush;
        }
        finally
        {
            _settingsLoaded = true;
        }
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            ReadServiceStatus();
            await ReadTemperatureStatusAsync();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ReadServiceStatus()
    {
        try
        {
            using ServiceController service = new("AuraIceTemperatureService");
            ServiceControllerStatus status = service.Status;
            ServiceStatusText.Text = status switch
            {
                ServiceControllerStatus.Running => "Serviço ativo",
                ServiceControllerStatus.StartPending => "Serviço iniciando",
                ServiceControllerStatus.StopPending => "Serviço parando",
                ServiceControllerStatus.Stopped => "Serviço parado",
                ServiceControllerStatus.Paused => "Serviço pausado",
                _ => "Serviço indisponível"
            };
            ServiceStatusDot.Fill = status == ServiceControllerStatus.Running ? OnlineBrush : OfflineBrush;
        }
        catch (InvalidOperationException)
        {
            ServiceStatusText.Text = "Serviço não instalado";
            ServiceStatusDot.Fill = OfflineBrush;
        }
        catch (Win32Exception)
        {
            ServiceStatusText.Text = "Serviço indisponível";
            ServiceStatusDot.Fill = UnknownBrush;
        }
    }

    private async Task ReadTemperatureStatusAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                AuraIceIpc.PipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(500);
            using var reader = new StreamReader(client);
            string? json = await reader.ReadLineAsync();
            AuraIceStatusSnapshot? status = json is null
                ? null
                : JsonSerializer.Deserialize<AuraIceStatusSnapshot>(json);

            if (status is null)
            {
                throw new InvalidOperationException("O serviço retornou um estado vazio.");
            }

            DeviceStatusText.Text = status.DeviceConnected ? "Display conectado" : "Display buscando";
            DeviceStatusDot.Fill = status.DeviceConnected ? OnlineBrush : OfflineBrush;
            SetGauge(status.TemperatureCelsius, status.SensorName);
            MessageText.Text = string.IsNullOrWhiteSpace(status.LastError)
                ? "Monitoramento em tempo real"
                : status.LastError;
        }
        catch (System.TimeoutException)
        {
            ShowUnavailableStatus("O serviço Aura Ice não respondeu.");
        }
        catch (UnauthorizedAccessException)
        {
            ShowUnavailableStatus("Acesso ao serviço negado. Atualize o Aura Ice para corrigir a permissão.");
        }
        catch (IOException)
        {
            ShowUnavailableStatus("O serviço ainda não iniciou ou não está instalado.");
        }
        catch (Exception)
        {
            ShowUnavailableStatus("Não foi possível consultar a temperatura.");
        }
    }

    private void ShowUnavailableStatus(string message)
    {
        DeviceStatusText.Text = "Display sem conexão";
        DeviceStatusDot.Fill = OfflineBrush;
        SetGauge(null, "Sem leitura");
        MessageText.Text = message;
    }

    private void SetGauge(decimal? temperature, string? sensorName)
    {
        TemperatureText.Text = temperature is { } value
            ? value.ToString("F1", CultureInfo.CurrentCulture)
            : "—";
        GaugeStatusText.Text = temperature is null
            ? sensorName ?? "Aguardando leitura"
            : sensorName ?? "Processador";
        DrawGauge(temperature);
    }

    private void DrawGauge(decimal? temperature)
    {
        GaugeCanvas.Children.Clear();
        AddArc(GaugeCanvas, GaugeTrackRadius, GaugeStartAngle, GaugeSweepAngle, InactiveGaugeBrush, 7);

        double progress = temperature is { } value
            ? Math.Clamp((double)value / 100d, 0d, 1d)
            : 0d;
        int activeTick = temperature is null
            ? -1
            : (int)Math.Round(progress * GaugeTickCount, MidpointRounding.AwayFromZero);

        for (int index = 0; index <= GaugeTickCount; index++)
        {
            double angle = GaugeStartAngle + GaugeSweepAngle * index / GaugeTickCount;
            bool isMajor = index % 6 == 0;
            double outerRadius = isMajor ? 148 : 146;
            double innerRadius = outerRadius - (isMajor ? 13 : 7);
            MediaPoint start = PointOnCircle(outerRadius, angle);
            MediaPoint end = PointOnCircle(innerRadius, angle);
            bool isActive = index <= activeTick;

            GaugeCanvas.Children.Add(new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = isActive ? ActiveGaugeBrush : InactiveGaugeBrush,
                StrokeThickness = isMajor ? 2.2 : 1.35,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        if (temperature is null)
        {
            return;
        }

        double progressAngle = GaugeStartAngle + GaugeSweepAngle * progress;
        AddArc(GaugeCanvas, GaugeTrackRadius, GaugeStartAngle, GaugeSweepAngle * progress, GaugeProgressBrush, 7);
        MediaPoint indicator = PointOnCircle(GaugeTrackRadius, progressAngle);
        var indicatorDot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = ActiveGaugeBrush,
            Stroke = new SolidColorBrush(MediaColor.FromRgb(190, 244, 255)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(indicatorDot, indicator.X - indicatorDot.Width / 2);
        Canvas.SetTop(indicatorDot, indicator.Y - indicatorDot.Height / 2);
        GaugeCanvas.Children.Add(indicatorDot);
    }

    private static void AddArc(Canvas canvas, double radius, double startAngle, double sweepAngle, MediaBrush stroke, double thickness)
    {
        if (sweepAngle <= 0)
        {
            return;
        }

        var figure = new PathFigure
        {
            StartPoint = PointOnCircle(radius, startAngle),
            IsClosed = false
        };
        figure.Segments.Add(new ArcSegment(
            PointOnCircle(radius, startAngle + sweepAngle),
            new MediaSize(radius, radius),
            0,
            sweepAngle > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        canvas.Children.Add(new WpfPath
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    private static MediaPoint PointOnCircle(double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180d;
        return new MediaPoint(
            GaugeCenter + radius * Math.Cos(radians),
            GaugeCenter + radius * Math.Sin(radians));
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DragMove();
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        _refreshTimer.Start();
    }
}
