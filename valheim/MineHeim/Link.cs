using System;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>The one connection to the Minecraft mod (127.0.0.1). Everything here is safe to call from the main thread.</summary>
    internal static class Link
    {
        private static WsClient _ws;

        public static bool Connected => _ws != null && _ws.Connected;

        public static void Start(string host, int port, Action<string> log)
        {
            if (_ws != null)
            {
                return;
            }

            _ws = new WsClient(host, port);
            _ws.Log += log;
            _ws.Start();
        }

        public static void Stop()
        {
            _ws?.Flush(300); // Valheim is closing: the last messages (Minecraft's "quit") still go out
            _ws?.Dispose();
            _ws = null;
        }

        public static void Send(string message) => _ws?.Send(message);

        public static void SendLatest(string message) => _ws?.SendLatest(message);

        public static bool TryDequeue(out string message)
        {
            message = null;
            return _ws != null && _ws.TryDequeue(out message);
        }
    }
}
