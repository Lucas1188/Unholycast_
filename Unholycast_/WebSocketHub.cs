using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Unholycast_;

public sealed class WebSocketHub
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task AddAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        try
        {
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _clients.TryRemove(id, out _);
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch
            {
            }
        }
    }

    public async Task BroadcastAsync<T>(T payload)
    {
        if (_clients.IsEmpty) return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync();
        try
        {
            var dead = new List<Guid>();
            foreach (var client in _clients)
            {
                if (client.Value.State != WebSocketState.Open)
                {
                    dead.Add(client.Key);
                    continue;
                }

                try
                {
                    await client.Value.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None);
                }
                catch
                {
                    dead.Add(client.Key);
                }
            }

            foreach (var id in dead)
                _clients.TryRemove(id, out _);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
