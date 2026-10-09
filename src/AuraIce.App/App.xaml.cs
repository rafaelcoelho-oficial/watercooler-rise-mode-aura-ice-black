using System.Diagnostics;
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
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private EventWaitHandle? _uninstallEvent;
    private RegisteredWaitHandle? _showWindowRegistration;
    private RegisteredWaitHandle? _uninstallRegistration;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

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
            (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication)),
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
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir widget", null, (_, _) => ShowWindow());
        menu.Items.Add("Desinstalar Aura Ice Controller...", null, (_, _) => StartUninstallerProcess());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApplication());
        return menu;
    }

    private void StartUninstallerProcess()
    {
        try
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Não foi possível localizar o executável do Aura Ice.");
            Process.Start(new ProcessStartInfo(executablePath, "--uninstall") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Não foi possível abrir o desinstalador.\n\n{exception.Message}",
                "Aura Ice Controller",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    private void ExitApplication()
    {
        _window?.AllowExit();
        _notifyIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _showWindowRegistration?.Unregister(null);
        _uninstallRegistration?.Unregister(null);
        _showWindowEvent?.Dispose();
        _uninstallEvent?.Dispose();
        _notifyIcon?.Dispose();
        _applicationIcon?.Dispose();

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // It can already be released if startup did not acquire it.
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
