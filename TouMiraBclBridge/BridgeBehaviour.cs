using UnityEngine;

namespace TouMiraBclBridge;

/// <summary>Samples TOU-Mira state ten times a second and publishes it when it changes.</summary>
public sealed class BridgeBehaviour(IntPtr ptr) : MonoBehaviour(ptr)
{
    private const float SampleInterval = 0.1f;
    // BetterCrewLink treats a snapshot older than 3s as "bridge gone", so resend unchanged state.
    private const float HeartbeatInterval = 1f;

    private readonly SnapshotBuilder _builder = new();
    private byte[]? _last;
    private float _nextSample;
    private float _nextHeartbeat;
    private bool _loggedError;

    public void Update()
    {
        var server = BridgePlugin.Server;
        var now = Time.unscaledTime;
        if (server == null || now < _nextSample)
        {
            return;
        }

        _nextSample = now + SampleInterval;

        byte[] line;
        try
        {
            line = _builder.Build();
        }
        catch (Exception ex)
        {
            // Scene transitions can briefly leave game objects half-destroyed; skip that frame.
            if (!_loggedError)
            {
                _loggedError = true;
                BepInEx.Logging.Logger.CreateLogSource("TouMiraBclBridge").LogWarning($"Snapshot failed: {ex}");
            }

            return;
        }

        if (_last != null && now < _nextHeartbeat && line.AsSpan().SequenceEqual(_last))
        {
            return;
        }

        _last = line;
        _nextHeartbeat = now + HeartbeatInterval;
        server.Publish(line);
    }
}
