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
using AuraIce.Core.Telemetry;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
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
    private const double GaugeMajorTickOuterRadius = 146;
    private const double GaugeMinorTickOuterRadius = 144;
    private const double GaugeMajorTickLength = 9;
    private const double GaugeMinorTickLength = 6;
    private const decimal GaugeTjMaxCelsius = 95m;
    private const decimal GaugeCautionThresholdCelsius = 80m;
    private const decimal GaugeDangerThresholdCelsius = 90m;
    private const int HistoryLimit = 60;
    private const double SparklineWidth = 168;
    private const double SparklineHeight = 20;

    private static readonly MediaBrush OnlineBrush = new SolidColorBrush(MediaColor.FromRgb(75, 232, 166));
    private static readonly MediaBrush OfflineBrush = new SolidColorBrush(MediaColor.FromRgb(240, 160, 111));
    private static readonly MediaBrush UnknownBrush = new SolidColorBrush(MediaColor.FromRgb(113, 129, 150));
    private static readonly MediaBrush ActiveGaugeBrush = CreateFrozenSolidBrush(91, 220, 249);
    private static readonly MediaBrush InactiveGaugeBrush = CreateFrozenSolidBrush(43, 70, 101);
    private static readonly MediaBrush GaugeProgressBrush = CreateFrozenGradientBrush(
        MediaColor.FromRgb(57, 112, 255),
        MediaColor.FromRgb(65, 223, 242));
    private static readonly GaugePalette NormalGaugePalette = new(
        ActiveGaugeBrush,
        GaugeProgressBrush,
        CreateFrozenSolidBrush(240, 247, 255));
    private static readonly GaugePalette CautionGaugePalette = new(
        CreateFrozenSolidBrush(239, 190, 84),
        CreateFrozenGradientBrush(MediaColor.FromRgb(226, 153, 57), MediaColor.FromRgb(246, 202, 105)),
        CreateFrozenSolidBrush(255, 220, 145));
    private static readonly GaugePalette DangerGaugePalette = new(
        CreateFrozenSolidBrush(239, 112, 76),
        CreateFrozenGradientBrush(MediaColor.FromRgb(218, 79, 59), MediaColor.FromRgb(246, 139, 87)),
        CreateFrozenSolidBrush(255, 174, 143));
    private static readonly GaugePalette CriticalGaugePalette = new(
        CreateFrozenSolidBrush(255, 64, 87),
        CreateFrozenGradientBrush(MediaColor.FromRgb(205, 37, 63), MediaColor.FromRgb(255, 71, 91)),
        CreateFrozenSolidBrush(255, 113, 130));

    private readonly AuraIceSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly HashSet<Task> _settingsOperations = [];
    private readonly Queue<double> _usageHistory = new();
    private readonly Queue<double> _frequencyHistory = new();
    private readonly Queue<double> _voltageHistory = new();
    private readonly Queue<double> _powerHistory = new();
    private bool _allowExit;
    private bool _isRefreshing;
    private bool _isSavingSettings;
    private bool _isShuttingDown;
    private bool _settingsLoaded;
    private TaskCompletionSource<bool>? _refreshCompletion;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public void AllowExit() => _allowExit = true;

    public void BeginShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _refreshTimer.Stop();
        _shutdownCancellation.Cancel();
    }

    public async Task WaitForOperationsStopAsync()
    {
        var operations = _settingsOperations.ToList();
        if (_refreshCompletion is { } completion)
        {
            operations.Add(completion.Task);
        }

        if (operations.Count > 0)
        {
            await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await TrackSettingsOperationAsync(LoadSettingsAsync(_shutdownCancellation.Token));
        if (_isShuttingDown)
        {
            return;
        }

        await RefreshAsync();
        if (!_isShuttingDown)
        {
            _refreshTimer.Start();
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
        if (SettingsPopup.IsOpen)
        {
            await TrackSettingsOperationAsync(LoadSettingsAsync(_shutdownCancellation.Token));
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isShuttingDown || _isSavingSettings)
        {
            return;
        }

        if (!int.TryParse(IntervalTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int interval) ||
            interval is < 250 or > 10_000)
        {
            SettingsMessageText.Text = "Use um valor entre 250 e 10000 ms.";
            SettingsMessageText.Foreground = OfflineBrush;
            return;
        }

        _isSavingSettings = true;
        try
        {
            await TrackSettingsOperationAsync(SaveSettingsAsync(interval));
        }
        finally
        {
            _isSavingSettings = false;
        }
    }

    private async Task SaveSettingsAsync(int interval)
    {
        try
        {
            AuraIceSettings current = await _settingsStore.LoadAsync(_shutdownCancellation.Token);
            await _settingsStore.SaveAsync(current with { UpdateIntervalMilliseconds = interval }, _shutdownCancellation.Token);
            if (_isShuttingDown)
            {
                return;
            }

            _refreshTimer.Interval = TimeSpan.FromMilliseconds(interval);
            _settingsLoaded = true;
            SettingsMessageText.Text = "Salvo. O serviço aplicará no próximo ciclo.";
            SettingsMessageText.Foreground = OnlineBrush;
        }
        catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
        {
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

    private async Task TrackSettingsOperationAsync(Task operation)
    {
        _settingsOperations.Add(operation);
        try
        {
            await operation;
        }
        finally
        {
            _settingsOperations.Remove(operation);
        }
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        if (_settingsLoaded)
        {
            return;
        }

        try
        {
            AuraIceSettings settings = await _settingsStore.LoadAsync(cancellationToken);
            IntervalTextBox.Text = settings.UpdateIntervalMilliseconds.ToString(CultureInfo.CurrentCulture);
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.UpdateIntervalMilliseconds, 250, 10_000));
            SettingsMessageText.Text = "Use entre 250 e 10000 ms.";
            SettingsMessageText.Foreground = UnknownBrush;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            IntervalTextBox.Text = "1000";
            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            SettingsMessageText.Text = "Não foi possível carregar as configurações.";
            SettingsMessageText.Foreground = OfflineBrush;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _settingsLoaded = true;
        }
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing || _isShuttingDown)
        {
            return;
        }

        _isRefreshing = true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshCompletion = completion;
        try
        {
            ReadServiceStatus();
            await ReadStatusAsync(_shutdownCancellation.Token);
        }
        finally
        {
            _isRefreshing = false;
            completion.TrySetResult(true);
            if (ReferenceEquals(_refreshCompletion, completion))
            {
                _refreshCompletion = null;
            }
        }
    }

    private void ReadServiceStatus()
    {
        try
        {
            using ServiceController service = new("AuraIceTemperatureService");
            ServiceControllerStatus status = service.Status;
            bool isRunning = status == ServiceControllerStatus.Running;
            ServiceStatusText.Text = status switch
            {
                ServiceControllerStatus.Running => "Serviço ativo",
                ServiceControllerStatus.StartPending => "Serviço iniciando",
                ServiceControllerStatus.StopPending => "Serviço parando",
                ServiceControllerStatus.Stopped => "Serviço parado",
                ServiceControllerStatus.Paused => "Serviço pausado",
                _ => "Serviço indisponível"
            };
            ServiceStatusDetail.Text = isRunning ? "Coleta compartilhada ativa" : "Sem coleta de sensores";
            ServiceStatusDot.Fill = isRunning ? OnlineBrush : OfflineBrush;
        }
        catch (InvalidOperationException)
        {
            ServiceStatusText.Text = "Serviço não instalado";
            ServiceStatusDetail.Text = "Instalação necessária";
            ServiceStatusDot.Fill = OfflineBrush;
        }
        catch (Win32Exception)
        {
            ServiceStatusText.Text = "Serviço indisponível";
            ServiceStatusDetail.Text = "Não foi possível consultar";
            ServiceStatusDot.Fill = UnknownBrush;
        }
    }

    private async Task ReadStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                AuraIceIpc.PipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(500, cancellationToken);
            using var reader = new StreamReader(client);
            string? json = await reader.ReadLineAsync(cancellationToken);
            AuraIceStatusSnapshot? status = json is null
                ? null
                : JsonSerializer.Deserialize<AuraIceStatusSnapshot>(json);

            if (status is null)
            {
                throw new InvalidOperationException("O serviço retornou um estado vazio.");
            }

            CpuTelemetrySnapshot telemetry = status.Telemetry ?? CpuTelemetrySnapshot.Empty;
            telemetry = RestoreLegacyTemperature(status, telemetry);

            DeviceStatusText.Text = status.DeviceConnected ? "Display conectado" : "Display buscando";
            DeviceStatusDetail.Text = status.DeviceConnected ? "Saída HID detectada" : "Tentando encontrar display";
            DeviceStatusDot.Fill = status.DeviceConnected ? OnlineBrush : OfflineBrush;
            SetTelemetry(telemetry);
            MessageText.Text = string.IsNullOrWhiteSpace(status.LastError)
                ? "Monitoramento em tempo real • leitura compartilhada a cada ciclo"
                : status.LastError;
        }
        catch (System.TimeoutException)
        {
            ShowUnavailableStatus("O serviço Aura Ice não respondeu.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
            ShowUnavailableStatus("Não foi possível consultar os sensores da CPU.");
        }
    }

    private static CpuTelemetrySnapshot RestoreLegacyTemperature(
        AuraIceStatusSnapshot status,
        CpuTelemetrySnapshot telemetry)
    {
        if (telemetry.Temperature.IsAvailable || status.TemperatureCelsius is not { } temperature ||
            string.IsNullOrWhiteSpace(status.SensorName))
        {
            return telemetry;
        }

        CpuMetricReading legacyTemperature = new(
            CpuMetricKind.Temperature,
            temperature,
            "°C",
            null,
            status.SensorName,
            "Aura Ice service",
            status.Timestamp,
            true);
        return telemetry with { Temperature = legacyTemperature };
    }

    private void ShowUnavailableStatus(string message)
    {
        DeviceStatusText.Text = "Display sem conexão";
        DeviceStatusDetail.Text = "Serviço sem resposta";
        DeviceStatusDot.Fill = OfflineBrush;
        SetTelemetry(CpuTelemetrySnapshot.CreateUnavailable(DateTimeOffset.UtcNow, message));
        MessageText.Text = message;
    }

    private void SetTelemetry(CpuTelemetrySnapshot telemetry)
    {
        CpuMetricReading temperature = telemetry.Temperature;
        SetGauge(temperature.IsAvailable ? temperature.Value : null, temperature.IsAvailable ? temperature.SensorName : null);

        RenderMetric(
            telemetry.Usage,
            _usageHistory,
            CpuUsageValue,
            CpuUsageUnit,
            CpuUsageSparkline,
            CpuUsageCard,
            "Uso da CPU");
        RenderMetric(
            telemetry.Frequency,
            _frequencyHistory,
            FrequencyValue,
            FrequencyUnit,
            FrequencySparkline,
            FrequencyCard,
            "Frequência da CPU");
        RenderMetric(
            telemetry.Voltage,
            _voltageHistory,
            VoltageValue,
            VoltageUnit,
            VoltageSparkline,
            VoltageCard,
            "Tensão da CPU");
        RenderMetric(
            telemetry.Power,
            _powerHistory,
            PowerValue,
            PowerUnit,
            PowerSparkline,
            PowerCard,
            "Potência do pacote da CPU");
    }

    private static void RenderMetric(
        CpuMetricReading reading,
        Queue<double> history,
        TextBlock valueText,
        TextBlock unitText,
        Polyline sparkline,
        Border card,
        string metricDescription)
    {
        if (reading is { IsAvailable: true, Value: { } value })
        {
            valueText.Text = FormatValue(reading.Kind, value);
            unitText.Text = reading.Unit;
            unitText.Visibility = Visibility.Visible;
            AppendHistory(history, value);
            sparkline.Points = BuildSparkline(history);
            sparkline.Visibility = history.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            card.ToolTip = $"{metricDescription}: {reading.SensorName ?? "sensor sem nome"}\nFonte: {reading.Source}" +
                           (reading.IsEstimated ? "\nLeitura identificada como estimada pelo provider." : string.Empty);
        }
        else
        {
            valueText.Text = "N/D";
            unitText.Text = string.Empty;
            unitText.Visibility = Visibility.Collapsed;
            sparkline.Visibility = Visibility.Collapsed;
            card.ToolTip = reading.Error ?? $"{metricDescription} indisponível.";
        }
    }

    private static string FormatValue(CpuMetricKind kind, decimal value)
    {
        string format = kind switch
        {
            CpuMetricKind.Usage => "F0",
            CpuMetricKind.Frequency => "F2",
            CpuMetricKind.Voltage => "F2",
            CpuMetricKind.Power => "F1",
            _ => "F1"
        };
        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    private static void AppendHistory(Queue<double> history, decimal value)
    {
        history.Enqueue((double)value);
        while (history.Count > HistoryLimit)
        {
            history.Dequeue();
        }
    }

    private static PointCollection BuildSparkline(IEnumerable<double> values)
    {
        double[] samples = values.ToArray();
        var points = new PointCollection();
        if (samples.Length == 0)
        {
            return points;
        }

        double min = samples.Min();
        double max = samples.Max();
        double range = Math.Max(max - min, 0.0001);
        for (int index = 0; index < samples.Length; index++)
        {
            double x = samples.Length == 1 ? 0 : index * SparklineWidth / (samples.Length - 1);
            double normalized = (samples[index] - min) / range;
            double y = SparklineHeight - 2 - normalized * (SparklineHeight - 5);
            points.Add(new MediaPoint(x, y));
        }

        return points;
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
        GaugePalette palette = GetGaugePalette(temperature);
        TemperatureText.Foreground = palette.ValueBrush;
        TemperatureUnitText.Foreground = palette.ActiveBrush;
        AddArc(
            GaugeCanvas,
            GaugeTrackRadius,
            GaugeStartAngle,
            GaugeSweepAngle,
            InactiveGaugeBrush,
            7,
            PenLineCap.Flat,
            PenLineCap.Round);

        double progress = temperature is { } value
            ? Math.Clamp((double)(value / GaugeTjMaxCelsius), 0d, 1d)
            : 0d;
        int activeTick = temperature is null
            ? -1
            : (int)Math.Round(progress * GaugeTickCount, MidpointRounding.AwayFromZero);

        for (int index = 0; index <= GaugeTickCount; index++)
        {
            double angle = GaugeStartAngle + GaugeSweepAngle * index / GaugeTickCount;
            bool isMajor = index % 6 == 0;
            double outerRadius = isMajor ? GaugeMajorTickOuterRadius : GaugeMinorTickOuterRadius;
            double innerRadius = outerRadius - (isMajor ? GaugeMajorTickLength : GaugeMinorTickLength);
            MediaPoint start = PointOnCircle(outerRadius, angle);
            MediaPoint end = PointOnCircle(innerRadius, angle);
            bool isActive = index <= activeTick;

            GaugeCanvas.Children.Add(new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = isActive ? palette.ActiveBrush : InactiveGaugeBrush,
                StrokeThickness = isMajor ? 2 : 1.25,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }

        if (temperature is null)
        {
            return;
        }

        double progressAngle = GaugeStartAngle + GaugeSweepAngle * progress;
        AddArc(
            GaugeCanvas,
            GaugeTrackRadius,
            GaugeStartAngle,
            GaugeSweepAngle * progress,
            palette.ProgressBrush,
            8,
            PenLineCap.Flat,
            PenLineCap.Round);
        MediaPoint indicator = PointOnCircle(GaugeTrackRadius, progressAngle);
        var indicatorDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = palette.ActiveBrush,
            Stroke = CreateFrozenSolidBrush(240, 247, 255),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(indicatorDot, indicator.X - indicatorDot.Width / 2);
        Canvas.SetTop(indicatorDot, indicator.Y - indicatorDot.Height / 2);
        GaugeCanvas.Children.Add(indicatorDot);
    }

    private static GaugePalette GetGaugePalette(decimal? temperature)
    {
        if (temperature is null || temperature < GaugeCautionThresholdCelsius)
        {
            return NormalGaugePalette;
        }

        if (temperature < GaugeDangerThresholdCelsius)
        {
            return CautionGaugePalette;
        }

        return temperature <= GaugeTjMaxCelsius
            ? DangerGaugePalette
            : CriticalGaugePalette;
    }

    private static MediaBrush CreateFrozenSolidBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static MediaBrush CreateFrozenGradientBrush(MediaColor startColor, MediaColor endColor)
    {
        var brush = new LinearGradientBrush(
            startColor,
            endColor,
            new MediaPoint(0, 1),
            new MediaPoint(1, 0));
        brush.Freeze();
        return brush;
    }

    private static void AddArc(
        Canvas canvas,
        double radius,
        double startAngle,
        double sweepAngle,
        MediaBrush stroke,
        double thickness,
        PenLineCap startLineCap = PenLineCap.Round,
        PenLineCap endLineCap = PenLineCap.Round)
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
            StrokeStartLineCap = startLineCap,
            StrokeEndLineCap = endLineCap
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.RequestExit();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        if (System.Windows.Application.Current is App app)
        {
            app.RequestExit();
        }
    }

    private readonly record struct GaugePalette(
        MediaBrush ActiveBrush,
        MediaBrush ProgressBrush,
        MediaBrush ValueBrush);
}
