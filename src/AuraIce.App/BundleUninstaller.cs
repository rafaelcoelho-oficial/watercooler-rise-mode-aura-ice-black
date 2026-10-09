using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AuraIce.App;

internal static class BundleUninstaller
{
    private const string AppDisplayName = "Aura Ice Controller";
    private const string AppPublisher = "Aura Ice";
    private const string SetupExecutableName = "AuraIce-Setup.exe";
    private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static void Launch()
    {
        string command = FindUninstallCommand()
            ?? throw new InvalidOperationException("O instalador registrado do Aura Ice não foi encontrado.");
        (string executablePath, string arguments) = ParseCommand(command);

        if (!Path.GetFileName(executablePath).Equals(SetupExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O comando de remoção registrado não corresponde ao instalador Aura Ice.");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("O instalador em cache não foi encontrado.", executablePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    private static string? FindUninstallCommand()
    {
        RegistryView[] registryViews = [RegistryView.Registry64, RegistryView.Registry32];
        foreach (RegistryView registryView in registryViews)
        {
            using RegistryKey machineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using RegistryKey? uninstallKey = machineKey.OpenSubKey(UninstallRegistryPath);
            if (uninstallKey is null)
            {
                continue;
            }

            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
            {
                using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);
                if (appKey?.GetValue("DisplayName") is not string displayName ||
                    !displayName.Equals(AppDisplayName, StringComparison.OrdinalIgnoreCase) ||
                    appKey.GetValue("Publisher") is not string publisher ||
                    !publisher.Equals(AppPublisher, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (appKey.GetValue("UninstallString") is string uninstallCommand &&
                    uninstallCommand.Contains(SetupExecutableName, StringComparison.OrdinalIgnoreCase))
                {
                    return uninstallCommand;
                }
            }
        }

        return null;
    }

    private static (string ExecutablePath, string Arguments) ParseCommand(string command)
    {
        string trimmedCommand = command.Trim();
        if (trimmedCommand.Length == 0)
        {
            throw new InvalidOperationException("O comando de desinstalação está vazio.");
        }

        string executablePath;
        string arguments;
        if (trimmedCommand[0] == '"')
        {
            int closingQuote = trimmedCommand.IndexOf('"', 1);
            if (closingQuote < 0)
            {
                throw new InvalidOperationException("O caminho do instalador registrado está malformado.");
            }

            executablePath = trimmedCommand[1..closingQuote];
            arguments = trimmedCommand[(closingQuote + 1)..].Trim();
        }
        else
        {
            int executableExtension = trimmedCommand.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (executableExtension < 0)
            {
                throw new InvalidOperationException("O comando registrado não aponta para um executável.");
            }

            int executableEnd = executableExtension + ".exe".Length;
            executablePath = trimmedCommand[..executableEnd];
            arguments = trimmedCommand[executableEnd..].Trim();
        }

        if (executablePath.Length == 0)
        {
            throw new InvalidOperationException("O caminho do instalador registrado está vazio.");
        }

        return (executablePath, arguments);
    }
}
