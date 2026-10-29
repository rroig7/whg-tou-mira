using System.Net;
using System.Net.Sockets;
using BepInEx.Logging;

namespace TouMiraBclBridge;

/// <summary>
/// Loopback-only TCP server that pushes newline-delimited JSON snapshots to every connected
/// BetterCrewLink. Writes happen off the Unity thread and only the newest snapshot is kept, so a
/// stalled reader can never block the game.
/// </summary>
internal sealed class BridgeServer(int port, ManualLogSource log) : IDisposable
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _pending = new(0, 1);
    private readonly object _lock = new();
    private readonly List<TcpClient> _clients = [];
    private TcpListener? _listener;
    private byte[]? _latest;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            log.LogWarning($"Could not listen on 127.0.0.1:{port} ({ex.SocketErrorCode}); BetterCrewLink won't see TOU-Mira state.");
            _listener = null;
            return;
        }

        _ = Task.Run(AcceptLoop);
        _ = Task.Run(SendLoop);
        log.LogInfo($"Listening for BetterCrewLink on 127.0.0.1:{port}.");
    }

    /// <summary>Queues a snapshot line. Safe to call from the Unity thread every frame.</summary>
    public void Publish(byte[] line)
    {
        lock (_lock)
        {
            _latest = line;
        }

        WakeSender();
    }

    private void WakeSender()
    {
        if (_pending.CurrentCount != 0)
        {
            return;
        }

        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled; the sender will pick up _latest.
        }
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning($"Accept failed: {ex.Message}");
                continue;
            }

            client.NoDelay = true;
            lock (_lock)
            {
                _clients.Add(client);
            }

            log.LogInfo("BetterCrewLink connected.");
            // SendLoop is the only writer; wake it so the newcomer gets the current snapshot.
            WakeSender();
        }
    }

    private async Task SendLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _pending.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            byte[]? line;
            TcpClient[] clients;
            lock (_lock)
            {
                line = _latest;
                clients = [.. _clients];
            }

            if (line == null)
            {
                continue;
            }

            foreach (var client in clients)
            {
                await SendTo(client, line);
            }
        }
    }

    private async Task SendTo(TcpClient client, byte[] line)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(WriteTimeout);
            await client.GetStream().WriteAsync(line, timeout.Token);
        }
        catch (Exception)
        {
            Drop(client);
        }
    }

    private void Drop(TcpClient client)
    {
        bool removed;
        lock (_lock)
        {
            removed = _clients.Remove(client);
        }

        client.Dispose();
        if (removed)
        {
            log.LogInfo("BetterCrewLink disconnected.");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
        }
    }
}
