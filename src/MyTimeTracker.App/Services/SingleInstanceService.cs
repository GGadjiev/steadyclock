using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MyTimeTracker.App.Services;

/// <summary>
/// One instance per signed-in user/session. IPC uses a local named pipe only;
/// no network sockets are created.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string ShowMessage = "SHOW";
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _ownsMutex;

    public SingleInstanceService()
    {
        var userHash = GetUserHash();
        _mutexName = $"Local\\SteadyClock.{userHash}";
        _pipeName = $"SteadyClock.{userHash}";
    }

    public bool IsOwner => _ownsMutex;
    public event EventHandler? ShowRequested;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
    }

    public void StartListening()
    {
        if (!_ownsMutex || _listenerTask is not null)
        {
            return;
        }

        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_listenerCts.Token));
    }

    public async Task<bool> SignalExistingInstanceAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await client.ConnectAsync(150, cancellationToken).ConfigureAwait(false);
                await using var writer = new StreamWriter(client, Encoding.UTF8);
                await writer.WriteLineAsync(ShowMessage.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
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
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(message, ShowMessage, StringComparison.Ordinal))
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        new Action(() => ShowRequested?.Invoke(this, EventArgs.Empty)));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // A stale client must not kill the listener. Retry after a short pause.
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string GetUserHash()
    {
        string identity;
        try
        {
            using var windowsIdentity = WindowsIdentity.GetCurrent();
            identity = windowsIdentity.User?.Value ?? Environment.UserName;
        }
        catch
        {
            identity = Environment.UserName;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash)[..16];
    }

    public void Dispose()
    {
        try
        {
            _listenerCts?.Cancel();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _listenerCts?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // The OS will release the mutex when the process exits.
            }
        }

        _mutex?.Dispose();
    }
}
