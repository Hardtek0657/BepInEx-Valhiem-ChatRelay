using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

const string MessagePrefix = "CHAT\t";
const string NoticePrefix = "NOTICE\t";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

ConcurrentDictionary<Guid, WebSocket> clients = new();
SemaphoreSlim broadcastLock = new(1, 1);

app.UseWebSockets();

app.Map("/chat", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket endpoint. Connect with ws://host:port/chat");
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    Guid id = Guid.NewGuid();
    clients[id] = socket;
    Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Client connected: {id}. Connected clients: {clients.Count}");

    try
    {
        await ReceiveAndBroadcastAsync(id, socket, clients, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Client connection canceled: {id}");
    }
    catch (WebSocketException ex)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Client closed WebSocket abruptly: {id}. {ex.Message}");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Client connection ended: {id}. {ex.Message}");
    }
    finally
    {
        clients.TryRemove(id, out _);
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Client disconnected: {id}. Connected clients: {clients.Count}");
    }
});

app.MapGet("/", () => "ChatUtility relay is running. WebSocket endpoint: /chat");

app.Run();

static async Task ReceiveAndBroadcastAsync(
    Guid senderId,
    WebSocket sender,
    ConcurrentDictionary<Guid, WebSocket> clients,
    CancellationToken cancellationToken)
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
    List<byte> frame = new();

    try
    {
        await ReceiveLoopAsync(senderId, sender, clients, cancellationToken, buffer, frame).ConfigureAwait(false);
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

static async Task ReceiveLoopAsync(
    Guid senderId,
    WebSocket sender,
    ConcurrentDictionary<Guid, WebSocket> clients,
    CancellationToken cancellationToken,
    byte[] buffer,
    List<byte> frame)
{
    while (!cancellationToken.IsCancellationRequested && sender.State == WebSocketState.Open)
    {
        WebSocketReceiveResult result;
        try
        {
            result = await sender.ReceiveAsync(buffer, cancellationToken);
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Receive stopped for {senderId}: {ex.Message}");
            break;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Receive ended for {senderId}: {ex.Message}");
            break;
        }

        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

        for (int i = 0; i < result.Count; i++)
        {
            frame.Add(buffer[i]);
        }
        if (!result.EndOfMessage)
        {
            continue;
        }

        byte[] payload = frame.ToArray();
        frame.Clear();
        LogIncomingFrame(senderId, payload);

        List<Task> sendTasks = new(clients.Count);
        List<Guid> failedClients = new();

        foreach ((Guid clientId, WebSocket client) in clients)
        {
            if (client.State != WebSocketState.Open)
            {
                failedClients.Add(clientId);
                continue;
            }

            sendTasks.Add(SendToClientAsync(clientId, client, payload, cancellationToken, failedClients));
        }

        await Task.WhenAll(sendTasks).ConfigureAwait(false);

        foreach (Guid clientId in failedClients)
        {
            clients.TryRemove(clientId, out _);
        }
    }

    if (sender.State == WebSocketState.Open || sender.State == WebSocketState.CloseReceived)
    {
        try
        {
            await sender.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
    }
}

static async Task SendToClientAsync(Guid clientId, WebSocket client, byte[] payload, CancellationToken cancellationToken, List<Guid> failedClients)
{
    try
    {
        await client.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }
    catch
    {
        lock (failedClients)
        {
            failedClients.Add(clientId);
        }
    }
}

static void LogIncomingFrame(Guid senderId, byte[] payload)
{
    string frame = Encoding.UTF8.GetString(payload);
    string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

    if (frame.Length < MessagePrefix.Length)
    {
        Console.WriteLine($"[{timestamp}] Empty frame from {senderId}");
        return;
    }

    if (frame.StartsWith(NoticePrefix, StringComparison.Ordinal))
    {
        LogNoticeFrame(senderId, frame, timestamp);
        return;
    }

    if (!frame.StartsWith(MessagePrefix, StringComparison.Ordinal))
    {
        Console.WriteLine($"[{timestamp}] Non-chat frame from {senderId}: {frame}");
        return;
    }

    int contentStart = MessagePrefix.Length;
    int tabIndex = frame.IndexOf('\t', contentStart);
    if (tabIndex == -1 || tabIndex == frame.Length - 1)
    {
        Console.WriteLine($"[{timestamp}] Malformed chat frame from {senderId}: {frame}");
        return;
    }

    try
    {
        string sender = Decode(frame.Substring(contentStart, tabIndex - contentStart));
        string message = Decode(frame.Substring(tabIndex + 1));
        Console.WriteLine($"[{timestamp}] {sender}: {message}");
    }
    catch (FormatException ex)
    {
        Console.WriteLine($"[{timestamp}] Invalid chat encoding from {senderId}: {ex.Message}");
    }
}

static void LogNoticeFrame(Guid senderId, string frame, string timestamp)
{
    try
    {
        string notice = Decode(frame[NoticePrefix.Length..]);
        Console.WriteLine($"[{timestamp}] NOTICE: {notice}");
    }
    catch (FormatException ex)
    {
        Console.WriteLine($"[{timestamp}] Invalid notice encoding from {senderId}: {ex.Message}");
    }
}

static string Decode(string value)
{
    return Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
