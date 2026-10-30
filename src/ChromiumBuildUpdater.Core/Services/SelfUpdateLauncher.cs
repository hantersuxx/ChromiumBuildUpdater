using System.Diagnostics;
using System.Globalization;

namespace ChromiumBuildUpdater.Core.Services;

public static class SelfUpdateLauncher
{
    private const string ApplyArgument = "--apply-update";
    private const string CleanupHelperArgument = "--cleanup-update-helper";
    private const string UpdateFailureArgument = "--update-failed";

    public static bool IsApplyRequest(string[] args) =>
        args.Length > 0 && string.Equals(args[0], ApplyArgument, StringComparison.Ordinal);

    public static bool IsCleanupHelperRequest(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CleanupHelperArgument, StringComparison.Ordinal);

    public static bool TryGetUpdateFailureMessage(string[] args, out string message)
    {
        if (args.Length == 2 &&
            string.Equals(args[0], UpdateFailureArgument, StringComparison.Ordinal))
        {
            message = args[1];
            return true;
        }

        message = "";
        return false;
    }

    public static void CleanupHelper(string[] args)
    {
        if (args.Length != 3 ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int helperProcessId))
        {
            return;
        }

        string helperDirectory = Path.GetFullPath(args[2]);
        string tempDirectory = Path.GetFullPath(Path.GetTempPath());
        if (!helperDirectory.StartsWith(
                Path.Combine(tempDirectory, "ChromiumBuildUpdater-helper-"),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return;
        }

        using (Process? helper = TryGetProcess(helperProcessId))
            helper?.WaitForExit();
        if (Directory.Exists(helperDirectory))
        {
            try
            {
                Directory.Delete(helperDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static void Start(StagedAppUpdate update, string targetPath, int parentProcessId)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (parentProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        if (!Path.IsPathFullyQualified(targetPath) || !File.Exists(targetPath))
            throw new FileNotFoundException("The current application executable could not be found.", targetPath);
        if (!IsUpdaterExecutable(targetPath) ||
            !IsOwnedTempDirectory(update.WorkingDirectory, "ChromiumBuildUpdater-update-") ||
            !IsExpectedStagedExecutable(update.ExecutablePath, update.WorkingDirectory))
        {
            throw new InvalidDataException("The staged app update paths are invalid.");
        }
        if (!File.Exists(update.ExecutablePath))
            throw new FileNotFoundException("The verified application update could not be found.", update.ExecutablePath);

        string helperDirectory = Directory.CreateTempSubdirectory("ChromiumBuildUpdater-helper-").FullName;
        string helperPath = Path.Combine(helperDirectory, Path.GetFileName(targetPath));
        try
        {
            File.Copy(targetPath, helperPath);
            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode mode = File.GetUnixFileMode(helperPath);
                File.SetUnixFileMode(helperPath, mode | UnixFileMode.UserExecute);
            }

            var startInfo = new ProcessStartInfo(helperPath)
            {
                UseShellExecute = false,
                WorkingDirectory = helperDirectory
            };
            startInfo.ArgumentList.Add(ApplyArgument);
            startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(update.ExecutablePath);
            startInfo.ArgumentList.Add(targetPath);
            startInfo.ArgumentList.Add(update.WorkingDirectory);
            using Process helperProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The update helper process could not be started.");
        }
        catch
        {
            try
            {
                Directory.Delete(helperDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    public static int RunApplyRequest(string[] args)
    {
        if (args.Length != 5 ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parentProcessId))
        {
            return 2;
        }

        string stagedPath = args[2];
        string targetPath = args[3];
        string workingDirectory = args[4];
        if (!IsUpdaterExecutable(targetPath) ||
            !IsOwnedTempDirectory(workingDirectory, "ChromiumBuildUpdater-update-") ||
            !IsExpectedStagedExecutable(stagedPath, workingDirectory))
        {
            return 2;
        }

        try
        {
            using (Process? parent = TryGetProcess(parentProcessId))
                parent?.WaitForExit();

            string replacementPath = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.new");
            try
            {
                File.Copy(stagedPath, replacementPath);
                if (!OperatingSystem.IsWindows())
                {
                    UnixFileMode mode = File.GetUnixFileMode(replacementPath);
                    File.SetUnixFileMode(replacementPath, mode | UnixFileMode.UserExecute);
                }

                File.Move(replacementPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(replacementPath))
                    File.Delete(replacementPath);
            }

            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            var restartInfo = new ProcessStartInfo(targetPath) { UseShellExecute = true };
            restartInfo.ArgumentList.Add(CleanupHelperArgument);
            restartInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            restartInfo.ArgumentList.Add(
                Path.GetDirectoryName(Environment.ProcessPath) ?? Path.GetTempPath());
            Process.Start(restartInfo)?.Dispose();
            return 0;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or PlatformNotSupportedException or ArgumentException or
            System.Security.SecurityException)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    var restartInfo = new ProcessStartInfo(targetPath) { UseShellExecute = true };
                    restartInfo.ArgumentList.Add(UpdateFailureArgument);
                    restartInfo.ArgumentList.Add(ex.Message);
                    Process.Start(restartInfo)?.Dispose();
                }
            }
            catch (Exception restartException) when (
                restartException is IOException or UnauthorizedAccessException or InvalidOperationException or
                System.ComponentModel.Win32Exception or PlatformNotSupportedException or
                System.Security.SecurityException)
            {
            }

            return 1;
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsUpdaterExecutable(string path) =>
        Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetFileNameWithoutExtension(path), "ChromiumBuildUpdater", StringComparison.Ordinal);

    private static bool IsExpectedStagedExecutable(string executablePath, string workingDirectory) =>
        Path.IsPathFullyQualified(executablePath) &&
        string.Equals(Path.GetFileName(executablePath), "ChromiumBuildUpdater", StringComparison.Ordinal) &&
        string.Equals(
            Path.GetDirectoryName(Path.GetFullPath(executablePath)),
            Path.GetFullPath(workingDirectory),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsOwnedTempDirectory(string path, string namePrefix)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;

        string fullPath = Path.GetFullPath(path);
        string tempPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        return string.Equals(
                   Path.GetDirectoryName(fullPath),
                   tempPath,
                   OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
               Path.GetFileName(fullPath).StartsWith(
                   namePrefix,
                   OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
