using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace ChatUtility;

internal static class RelayChatClient
{
    private const string MessagePrefix = "CHAT\t";
    private const string NoticePrefix = "NOTICE\t";
    private const string RelayNameColor = "#FF10F0";
    private const string RelayTextColor = "#FF10F0";
    private const string OwnRelayTextColor = "#B8B8B8";
    private const string NoticeColor = "#B8B8B8";
    private const string ChatShadowColor = "#000000E6";

    private static readonly ConcurrentQueue<RelayMessage> Incoming = new();
    private static readonly ConcurrentQueue<string> Notices = new();
    private static readonly ConcurrentQueue<string> StatusMessages = new();
    private static readonly SemaphoreSlim SendLock = new(1, 1);

    private static CancellationTokenSource? _cancel;
    private static ClientWebSocket? _socket;
    private static Task? _runner;
    private static float _reconnectTimer;
    private static bool _connecting;
    private static bool _wasConnected;
    private static FieldInfo? _chatHideTimerField;
    private static TextMeshProUGUI? _shadowedOutput;
    private static bool _cachedModEnabled;
    private static bool _cachedRelayEnabled;
    private static float _cachedReconnectSeconds;

    public static void Start()
    {
        _cancel = new CancellationTokenSource();
        _reconnectTimer = 0f;
    }

    public static void Stop()
    {
        CancellationTokenSource? cancel = _cancel;
        _cancel = null;
        cancel?.Cancel();
        cancel?.Dispose();

        ClientWebSocket? socket = _socket;
        _socket = null;
        socket?.Dispose();
    }

    public static void ForceReconnect()
    {
        ClientWebSocket? socket = _socket;
        _socket = null;
        socket?.Abort();
        socket?.Dispose();

        _connecting = false;
        _wasConnected = false;
        _reconnectTimer = 0f;
        QueueStatus("Relay reconnect requested.");
    }

    public static void Update(float deltaTime)
    {
        DrainMessages();

        _cachedModEnabled = ChatUtilityPlugin.ModEnabled.Value;
        _cachedRelayEnabled = ChatUtilityPlugin.RelayEnabled.Value;

        if (!_cachedModEnabled || !_cachedRelayEnabled || _cancel == null)
        {
            return;
        }

        if (IsOpen || _connecting)
        {
            return;
        }

        _reconnectTimer -= deltaTime;
        if (_reconnectTimer > 0f)
        {
            return;
        }

        _cachedReconnectSeconds = Math.Max(1f, ChatUtilityPlugin.RelayReconnectSeconds.Value);
        _reconnectTimer = _cachedReconnectSeconds;
        _runner = Task.Run(() => RunAsync(_cancel.Token));
    }

    public static bool TryHandleOutgoing(Talker.Type type, string text)
    {
        if (!_cachedModEnabled || !_cachedRelayEnabled)
        {
            return false;
        }

        if (type != Talker.Type.Normal)
        {
            return false;
        }

        string message = text.Trim();
        if (message.Length == 0)
        {
            QueueStatus("Relay message was empty.");
            return true;
        }

        string sender = GetPlayerName();
        _ = Task.Run(() => SendChatAsync(sender, message));
        return true;
    }

    public static void SendNotice(string text)
    {
        if (!_cachedModEnabled || !_cachedRelayEnabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _ = Task.Run(() => SendFrameAsync(NoticePrefix + Encode(text.Trim())));
    }

    private static async Task RunAsync(CancellationToken token)
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        try
        {
            using ClientWebSocket socket = new();
            _socket = socket;
            Uri relayUri = new(ChatUtilityPlugin.RelayServerUrl.Value);
            QueueStatus("Connecting relay: " + relayUri);
            await socket.ConnectAsync(relayUri, token).ConfigureAwait(false);
            _wasConnected = true;
            QueueStatus("Relay connected.");
            bool disconnectLogged = await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
            if (!disconnectLogged)
            {
                QueueStatus("Relay disconnected.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            QueueStatus((_wasConnected ? "Relay disconnected: " : "Relay connection failed: ") + FormatException(ex));
        }
        finally
        {
            _wasConnected = false;
            _socket = null;
            _connecting = false;
        }
    }

    private static async Task<bool> ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            return await ReceiveLoopInternalAsync(socket, token, buffer).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<bool> ReceiveLoopInternalAsync(ClientWebSocket socket, CancellationToken token, byte[] buffer)
    {
        StringBuilder message = new();
        bool disconnectLogged = false;

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            }
            catch (WebSocketException ex)
            {
                QueueStatus("Relay disconnected: " + ex.Message);
                disconnectLogged = true;
                break;
            }
            catch (System.IO.IOException ex)
            {
                QueueStatus("Relay disconnected: " + ex.Message);
                disconnectLogged = true;
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
            message.Append(chunk);
            if (!result.EndOfMessage)
            {
                continue;
            }

            HandleFrame(message.ToString());
            message.Clear();
        }

        return disconnectLogged;
    }

    private static async Task SendChatAsync(string sender, string message)
    {
        string frame = MessagePrefix + Encode(sender) + "\t" + Encode(message);
        await SendFrameAsync(frame).ConfigureAwait(false);
    }

    private static async Task SendFrameAsync(string frame)
    {
        try
        {
            ClientWebSocket? socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                QueueStatus("Relay is not connected.");
                _socket = null;
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(frame);

            await SendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                SendLock.Release();
            }
        }
        catch (Exception ex)
        {
            QueueStatus("Relay send failed: " + ex.Message);
            _socket = null;
        }
    }

    private static void HandleFrame(string frame)
    {
        if (frame.Length < MessagePrefix.Length)
        {
            return;
        }

        if (frame.StartsWith(MessagePrefix, StringComparison.Ordinal))
        {
            int contentStart = MessagePrefix.Length;
            int tabIndex = frame.IndexOf('\t', contentStart);
            if (tabIndex == -1 || tabIndex == frame.Length - 1)
            {
                return;
            }

            string sender = Decode(frame.Substring(contentStart, tabIndex - contentStart));
            string text = Decode(frame.Substring(tabIndex + 1));
            Incoming.Enqueue(new RelayMessage(sender, text));
            return;
        }

        if (frame.StartsWith(NoticePrefix, StringComparison.Ordinal))
        {
            Notices.Enqueue(Decode(frame.Substring(NoticePrefix.Length)));
            return;
        }
    }

    private static void DrainMessages()
    {
        while (StatusMessages.TryDequeue(out string status))
        {
            AddChatLine("Relay", status, Talker.Type.Whisper);
        }

        while (Incoming.TryDequeue(out RelayMessage message))
        {
            AddRelayChatLine(message.Sender, message.Text);
        }

        while (Notices.TryDequeue(out string notice))
        {
            AddNoticeLine(notice);
        }
    }

    private static void AddRelayChatLine(string sender, string text)
    {
        string textColor = IsLocalSender(sender) ? OwnRelayTextColor : RelayTextColor;
        string line = $"<color={RelayNameColor}>{EscapeRichText(sender)}</color>: <color={textColor}>{EscapeRichText(text)}</color>";
        if (Chat.instance)
        {
            ShowChatWindow();
            EnsureChatTextShadow(Chat.instance);
            Chat.instance.AddString(line);
        }
        else
        {
            ChatUtilityPlugin.Log.LogInfo($"{sender}: {text}");
        }
    }

    private static void AddChatLine(string title, string text, Talker.Type type)
    {
        if (Chat.instance)
        {
            ShowChatWindow();
            EnsureChatTextShadow(Chat.instance);
            Chat.instance.AddString(title, text, type);
        }
        else
        {
            ChatUtilityPlugin.Log.LogInfo($"{title}: {text}");
        }
    }

    private static void AddNoticeLine(string text)
    {
        string line = FormatNoticeLine(text);
        if (Chat.instance)
        {
            ShowChatWindow();
            EnsureChatTextShadow(Chat.instance);
            Chat.instance.AddString(line);
        }
        else
        {
            ChatUtilityPlugin.Log.LogInfo(text);
        }
    }

    private static string FormatNoticeLine(string text)
    {
        const string afkSuffix = " has gone AFK.";
        const string returnedSuffix = " is no longer AFK.";

        if (text.EndsWith(afkSuffix, StringComparison.Ordinal))
        {
            return FormatNameNotice(text.Substring(0, text.Length - afkSuffix.Length), afkSuffix);
        }

        if (text.EndsWith(returnedSuffix, StringComparison.Ordinal))
        {
            return FormatNameNotice(text.Substring(0, text.Length - returnedSuffix.Length), returnedSuffix);
        }

        return $"<color={NoticeColor}>{EscapeRichText(text)}</color>";
    }

    private static string FormatNameNotice(string name, string suffix)
    {
        return $"<color={RelayNameColor}>{EscapeRichText(name)}{EscapeRichText(suffix)}</color>";
    }

    private static void QueueStatus(string status)
    {
        StatusMessages.Enqueue(status);
        ChatUtilityPlugin.Log.LogInfo(status);
    }

    private static void ShowChatWindow()
    {
        Chat chat = Chat.instance;
        if (!chat)
        {
            return;
        }

        _chatHideTimerField ??= typeof(Chat).GetField("m_hideTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        _chatHideTimerField?.SetValue(chat, 0f);
    }

    private static void EnsureChatTextShadow(Chat chat)
    {
        TextMeshProUGUI output = chat.m_output;
        if (output == null || ReferenceEquals(output, _shadowedOutput))
        {
            return;
        }

        Material material = output.fontMaterial;
        if (!material)
        {
            return;
        }

        if (ColorUtility.TryParseHtmlString(ChatShadowColor, out Color shadowColor))
        {
            material.SetColor(ShaderUtilities.ID_UnderlayColor, shadowColor);
        }

        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.9f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.9f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.12f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.2f);
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        output.UpdateMeshPadding();
        _shadowedOutput = output;
    }

    private static string GetPlayerName()
    {
        Player localPlayer = Player.m_localPlayer;
        if (localPlayer)
        {
            string name = localPlayer.GetPlayerName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "Unknown";
    }

    private static bool IsLocalSender(string sender)
    {
        return string.Equals(sender, GetPlayerName(), StringComparison.Ordinal);
    }

    private static bool IsOpen => _socket?.State == WebSocketState.Open;

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static string EscapeRichText(string value)
    {
        if (value.IndexOfAny(new[] { '<', '>' }) == -1)
        {
            return value;
        }
        return value.Replace('<', ' ').Replace('>', ' ');
    }

    private static string FormatException(Exception ex)
    {
        StringBuilder builder = new();
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (builder.Length > 0)
            {
                builder.Append(" -> ");
            }

            builder.Append(current.GetType().Name);
            builder.Append(": ");
            builder.Append(current.Message);
        }

        return builder.ToString();
    }

    private readonly struct RelayMessage
    {
        public RelayMessage(string sender, string text)
        {
            Sender = sender;
            Text = text;
        }

        public string Sender { get; }

        public string Text { get; }
    }
}
