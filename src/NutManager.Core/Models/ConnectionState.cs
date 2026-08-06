namespace NutManager.Core.Models;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    ConnectionFailed
}
