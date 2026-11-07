using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SteadyClock.Desktop.Services;

/// <summary>
/// One instance per desktop user. Uses a named mutex plus a local named pipe only;
/// no network sockets are involved.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string ShowMessage = "SHOW";
    private readonly string _lockPath;
    private readonly string _pipeName;
    private FileStream? _lockStream;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _ownsLock;

    public SingleInstanceService()
    {
        var hash = GetUserHash();
        _lockPath = Path.Combine(Path.GetTempPath(), $"steadyclock-desktop-{hash}.lock");
        _pipeName = $"steadyclock-desktop-{hash}";
    }

    public bool IsOwner => _ownsLock;
    public event EventHandler? ShowRequested;

    public bool TryAcquire()
    {
        try
        {
            _lockStream = new FileStream(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            _ownsLock = true;
            return true;
        }
        catch (IOException)
        {
            _ownsLock = false;
            _lockStream?.Dispose();
            _lockStream = null;
            return false;
        }
    }

    public void StartListening()
    {
        if (!_ownsLock || _listenerTask is not null)
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
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string GetUserHash()
    {
        var identity = string.Join(
            "|",
            Environment.UserName,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.MachineName);
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
        try
        {
            _lockStream?.Dispose();
        }
        catch
        {
            // The lock is released by the OS when the process exits.
        }

        _lockStream = null;
        _ownsLock = false;
    }
}
