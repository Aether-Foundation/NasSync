using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NasSync.UI;

/// <summary>
/// Named pipe client that communicates with the tray process to query sync state
/// and send commands (pause, resume, scan, restart).
/// </summary>
internal sealed class AppStateClient : IDisposable
{
    private const string PIPE_NAME = "NasSync-AppState";

    /// <summary>
    /// Queries the current sync state from the tray process.
    /// Returns null if the tray process is not running.
    /// </summary>
    public async Task<AppStateDto?> GetStateAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);
            await client.ConnectAsync(1000);

            // Send request
            byte[] requestBytes = Encoding.UTF8.GetBytes("GET_STATE\n");
            await client.WriteAsync(requestBytes);
            await client.FlushAsync();

            // Read response
            using var reader = new StreamReader(client, Encoding.UTF8);
            string? response = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(response))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AppStateDto>(response);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sends a command to the tray process (pause, resume, scan, restart).
    /// </summary>
    public async Task SendCommandAsync(string command)
    {
        using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);
        await client.ConnectAsync(2000);

        byte[] commandBytes = Encoding.UTF8.GetBytes($"COMMAND:{command}\n");
        await client.WriteAsync(commandBytes);
        await client.FlushAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose — connections are created per-request
    }
}

/// <summary>
/// Data transfer object for sync state queries between UI and tray processes.
/// </summary>
/// <param name="SyncState">Current sync state string (e.g., "Connected", "Syncing").</param>
/// <param name="PendingCount">Number of pending file changes to upload.</param>
/// <param name="InProgressCount">Number of operations currently in progress.</param>
public record AppStateDto(string SyncState, int PendingCount, int InProgressCount);
