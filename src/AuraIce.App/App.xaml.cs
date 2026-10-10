using System.Threading;
using Forms = System.Windows.Forms;

namespace AuraIce.App;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = "Local\\AuraIceController.Instance";
    private const string ShowWindowEventName = "Local\\AuraIceController.ShowWindow";
    private const string UninstallEventName = "Local\\AuraIceController.Uninstall";

    private Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _applicationIcon;
    private MainWindow? _window;
    private Task? _exitTask;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private EventWaitHandle? _uninstallEvent;
    private RegisteredWaitHandle? _showWindowRegistration;
    private RegisteredWaitHandle? _uninstallRegistration;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument => argument.Equals("--shutdown", StringComparison.OrdinalIgnoreCase)))
        {
            SignalExistingInstance(UninstallEventName);
            Shutdown();
            return;
        }

        if (e.Args.Any(argument => argument.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstallerMode();
            return;
        }

        if (!TryAcquireInstanceMutex())
        {
            SignalExistingInstance(ShowWindowEventName);
            Shutdown();
            return;
        }

        InitializeInstanceSignals();
        _window = new MainWindow();
        _applicationIcon = Environment.ProcessPath is { } executablePath
            ? System.Drawing.Icon.ExtractAssociatedIcon(executablePath)
            : null;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Aura Ice Controller",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                ShowWindow();
            }
        };

        if (e.Args.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase)))
        {
            _window.Hide();
        }
        else
        {
            ShowWindow();
        }
    }

    private bool TryAcquireInstanceMutex()
    {
        var mutex = new Mutex(false, InstanceMutexName);
        bool ownsMutex;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            mutex.Dispose();
            return false;
        }

        _instanceMutex = mutex;
        return true;
    }

    private void InitializeInstanceSignals()
    {
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _uninstallEvent = new EventWaitHandle(false, EventResetMode.AutoReset, UninstallEventName);
        _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showWindowEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowWindow)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        _uninstallRegistration = ThreadPool.RegisterWaitForSingleObject(
            _uninstallEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(RequestExit)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void RunUninstallerMode()
    {
        System.Windows.MessageBoxResult answer = System.Windows.MessageBox.Show(
            "Deseja remover o Aura Ice Controller deste computador?",
            "Desinstalar Aura Ice Controller",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            Shutdown();
            return;
        }

        try
        {
            SignalExistingInstance(UninstallEventName);

            using var mutex = new Mutex(false, InstanceMutexName);
            bool ownsMutex;
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(20));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                throw new TimeoutException("O Aura Ice ainda está em execução. Feche-o pela bandeja e tente novamente.");
            }

            try
            {
                BundleUninstaller.Launch();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Não foi possível iniciar a desinstalação. Você também pode removê-lo em Configurações > Aplicativos.\n\n{exception.Message}",
                "Aura Ice Controller",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            Shutdown();
        }
    }

    private static void SignalExistingInstance(string eventName)
    {
        try
        {
            using EventWaitHandle signal = EventWaitHandle.OpenExisting(eventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The tray app is not running, so there is nothing to signal.
        }
    }

    private Forms.ContextMenuStrip CreateContextMenu()
    {
        var renderer = new AuraIceTrayMenuRenderer();
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = renderer,
            BackColor = AuraIceTrayMenuRenderer.MenuBackground,
            ForeColor = AuraIceTrayMenuRenderer.MenuForeground,
            Font = new System.Drawing.Font("Segoe UI", 9F),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            AutoSize = true,
            Padding = new System.Windows.Forms.Padding(4)
        };

        var openItem = new Forms.ToolStripMenuItem("Abrir widget")
        {
            Padding = new System.Windows.Forms.Padding(12, 4, 12, 4),
            Margin = new System.Windows.Forms.Padding(6, 4, 6, 4),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        openItem.Click += (_, _) => ShowWindow();
        TrackPointerPress(openItem);

        var exitItem = new Forms.ToolStripMenuItem("Sair")
        {
            Padding = new System.Windows.Forms.Padding(12, 4, 12, 4),
            Margin = new System.Windows.Forms.Padding(6, 4, 6, 4),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        exitItem.Click += (_, _) => RequestExit();
        TrackPointerPress(exitItem);

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator
        {
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
        });
        menu.Items.Add(exitItem);
        menu.Opened += (_, _) => ApplyRoundedCorners(menu, AuraIceTrayMenuRenderer.LogicalMenuCornerRadius);
        menu.SizeChanged += (_, _) =>
        {
            if (menu.IsHandleCreated)
                ApplyRoundedCorners(menu, AuraIceTrayMenuRenderer.LogicalMenuCornerRadius);
        };
        menu.Opening += (_, _) =>
        {
            int itemWidth = menu.Items.OfType<Forms.ToolStripMenuItem>()
                .Max(item => item.GetPreferredSize(System.Drawing.Size.Empty).Width + item.Margin.Horizontal);
            menu.MinimumSize = new System.Drawing.Size(itemWidth, 0);
        };
        menu.Closed += (_, _) => renderer.SetPointerPressedItem(null);
        return menu;

        void TrackPointerPress(Forms.ToolStripMenuItem item)
        {
            item.MouseDown += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    renderer.SetPointerPressedItem(item);
                }
            };
            item.MouseUp += (_, _) => renderer.SetPointerPressedItem(null);
            item.MouseLeave += (_, _) => renderer.SetPointerPressedItem(null);
        }
    }

    private static void ApplyRoundedCorners(Forms.ContextMenuStrip menu, float radius)
    {
        if (menu.Width <= 0 || menu.Height <= 0)
            return;

        using var path = new System.Drawing.Drawing2D.GraphicsPath();

        int scaledRadius = Math.Max(1, (int)Math.Round(radius * menu.DeviceDpi / 96F));
        int diameter = scaledRadius * 2;

        var bounds = new System.Drawing.Rectangle(
            0, 0,
            menu.Width,
            menu.Height
        );

        path.AddArc(
            bounds.Left,
            bounds.Top,
            diameter,
            diameter,
            180, 90);

        path.AddArc(
            bounds.Right - diameter,
            bounds.Top,
            diameter,
            diameter,
            270, 90);

        path.AddArc(
            bounds.Right - diameter,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            0, 90);

        path.AddArc(
            bounds.Left,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            90, 90);

        path.CloseFigure();

        var oldRegion = menu.Region;
        menu.Region = new System.Drawing.Region(path);
        oldRegion?.Dispose();
    }

    private void ShowWindow()
    {
        if (_window is null || _exitTask is not null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    internal void RequestExit()
    {
        _exitTask ??= ExitApplicationAsync();
    }

    private async Task ExitApplicationAsync()
    {
        // Let the current Click or Closing event return before Shutdown closes the window.
        await Task.Yield();
        try
        {
            _window?.BeginShutdown();
            if (_window is not null)
            {
                await _window.WaitForOperationsStopAsync();
            }
        }
        catch (TimeoutException exception)
        {
            System.Diagnostics.Trace.TraceWarning("Timed out waiting for Aura Ice UI operations to stop: {0}", exception.Message);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Aura Ice shutdown cleanup failed: {0}", exception.Message);
        }
        finally
        {
            _window?.AllowExit();
            try
            {
                DisposeTrayIcon();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Failed to dispose Aura Ice tray resources: {0}", exception.Message);
            }

            Shutdown();
        }
    }

    private void DisposeTrayIcon()
    {
        Forms.NotifyIcon? notifyIcon = _notifyIcon;
        _notifyIcon = null;
        if (notifyIcon is null)
        {
            return;
        }

        Forms.ContextMenuStrip? contextMenu = notifyIcon.ContextMenuStrip;
        try
        {
            notifyIcon.Visible = false;
        }
        finally
        {
            try
            {
                notifyIcon.ContextMenuStrip = null;
            }
            finally
            {
                try
                {
                    notifyIcon.Dispose();
                }
                finally
                {
                    contextMenu?.Dispose();
                }
            }
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        TryCleanup("window", () => _window?.BeginShutdown());
        TryCleanup("show-window registration", () => { _showWindowRegistration?.Unregister(null); });
        TryCleanup("uninstall registration", () => { _uninstallRegistration?.Unregister(null); });
        TryCleanup("show-window event", () => _showWindowEvent?.Dispose());
        TryCleanup("uninstall event", () => _uninstallEvent?.Dispose());
        TryCleanup("tray icon", DisposeTrayIcon);
        TryCleanup("application icon", () => _applicationIcon?.Dispose());
        _applicationIcon = null;

        if (_instanceMutex is { } instanceMutex)
        {
            TryCleanup("instance mutex release", () =>
            {
                try
                {
                    instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The current thread does not own the mutex.
                }
            });
            TryCleanup("instance mutex", instanceMutex.Dispose);
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private static void TryCleanup(string resource, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Failed to release Aura Ice {0}: {1}", resource, exception.Message);
        }
    }
}
