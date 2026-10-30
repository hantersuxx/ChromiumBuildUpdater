using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace ChromiumBuildUpdater.Core.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string ActivationMessage = "activate";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listenerTask;

    public event Action? ActivationRequested;

    private SingleInstanceService(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _listenerTask = ListenForActivationAsync(_cancellation.Token);
    }

    public static SingleInstanceService? TryCreate(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        string identity = $"{Environment.UserName}:{applicationId}";
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        string mutexName = $"CBU-{suffix}";
        string pipeName = $"CBU-{suffix}";
        var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            NotifyPrimaryInstance(pipeName);
            return null;
        }

        return new SingleInstanceService(mutex, pipeName);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _listenerTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
        _mutex.Dispose();
    }

    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                string? message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(message, ActivationMessage, StringComparison.Ordinal))
                    ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                Trace.WriteLine($"Single-instance activation channel failed: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Trace.WriteLine($"Single-instance activation channel was denied: {ex.Message}");
                break;
            }
        }
    }

    private static void NotifyPrimaryInstance(string pipeName)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.Out,
                    PipeOptions.None);
                client.Connect(150);
                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
                writer.WriteLine(ActivationMessage);
                writer.Flush();
                return;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException ex)
            {
                Trace.WriteLine($"Could not notify the existing app instance: {ex.Message}");
                return;
            }
        }
    }
}
