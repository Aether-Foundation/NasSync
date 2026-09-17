using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// Named pipe server running in the tray process. Handles state queries and commands
/// from the WinUI 3 settings window (separate process).
///
/// <para>
/// Protocol:
/// - Request: <c>GET_STATE\n</c> → Response: JSON <see cref="AppStateDto"/>
/// - Request: <c>COMMAND:{command}\n</c> → Executes command on the engine
/// </para>
/// </summary>
internal sealed class NamedPipeServer : IDisposable
{
    private const string PIPE_NAME = "NasSync-AppState";

    private readonly SyncEngine _engine;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Creates a new NamedPipeServer for the given sync engine.
    /// </summary>
    /// <param name="engine">The sync engine to query state from and send commands to.</param>
    public NamedPipeServer(SyncEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Starts listening for pipe connections in the background.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ListenLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stops listening for pipe connections.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    /// <summary>
    /// Main listen loop — waits for pipe connections and handles requests.
    /// </summary>
    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    PIPE_NAME,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);
                _ = HandleConnectionAsync(server);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PipeServer] Listen error: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
    }

    /// <summary>
    /// Handles a single pipe connection — reads the request and sends a response.
    /// </summary>
    private async Task HandleConnectionAsync(NamedPipeServerStream server)
    {
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            string? request = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(request))
            {
                server.Disconnect();
                server.Dispose();
                return;
            }

            if (request == "GET_STATE")
            {
                var state = new AppStateDto(
                    SyncState: _engine.State.ToString(),
                    PendingCount: _engine.GetQueueStatus().PendingCount,
                    InProgressCount: _engine.GetQueueStatus().InProgressCount);

                string json = JsonSerializer.Serialize(state);
                byte[] responseBytes = Encoding.UTF8.GetBytes(json + "\n");
                await server.WriteAsync(responseBytes);
                await server.FlushAsync();
            }
            else if (request.StartsWith("COMMAND:"))
            {
                string command = request["COMMAND:".Length..].Trim();

                switch (command)
                {
                    case "pause":
                        _engine.Pause();
                        break;
                    case "resume":
                        _engine.Resume();
                        break;
                    case "scan":
                        _ = _engine.FullScanAsync();
                        break;
                    case "restart":
                        // Signal Program.cs to restart (future: use event)
                        System.Diagnostics.Debug.WriteLine("[PipeServer] Restart requested");
                        break;
                }
            }

            server.Disconnect();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PipeServer] Handle error: {ex.Message}");
        }
        finally
        {
            server.Dispose();
        }
    }
}

/// <summary>
/// Data transfer object for sync state queries between tray and UI processes.
/// </summary>
public record AppStateDto(string SyncState, int PendingCount, int InProgressCount);
