using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MineHeim.Core
{
    /// <summary>
    /// A small RFC 6455 WebSocket client for the link to the Minecraft mod (127.0.0.1 only). Text frames only.
    /// Everything that can block (connect, read, write, reconnect back-off) runs on two background threads: the game's
    /// main thread only calls Send / SendLatest / TryDequeue, which never block.
    /// </summary>
    public sealed class WsClient : IDisposable
    {
        private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly string _host;
        private readonly int _port;
        private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly object _frameLock = new object();
        private readonly Random _rng = new Random();
        private Thread _reader;
        private Thread _writer;
        private volatile bool _stop;
        private volatile string _latest;
        private volatile TcpClient _tcp;
        private volatile NetworkStream _stream;
        private int _outgoingCount;

        /// <summary>True between a completed handshake and the connection dropping.</summary>
        public volatile bool Connected;

        /// <summary>Raised from the background threads (connect, drop, errors); the plugin forwards it to its logger.</summary>
        public event Action<string> Log;

        public WsClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Start()
        {
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "mineheim-ws-read" };
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "mineheim-ws-write" };
            _reader.Start();
            _writer.Start();
        }

        /// <summary>Queue a message (kept in order). Dropped when disconnected, or when the backlog is huge.</summary>
        public void Send(string message)
        {
            if (!Connected || Volatile.Read(ref _outgoingCount) > 4096)
            {
                return;
            }

            _outgoing.Enqueue(message);
            Interlocked.Increment(ref _outgoingCount);
            _wake.Set();
        }

        /// <summary>Send the newest value of a stream (the camera): an unsent older one is replaced, never queued up.</summary>
        public void SendLatest(string message)
        {
            if (!Connected)
            {
                return;
            }

            _latest = message;
            _wake.Set();
        }

        public bool TryDequeue(out string message)
        {
            return _incoming.TryDequeue(out message);
        }

        /// <summary>Wait (at most `milliseconds`) until every queued message has been written. For the last words before quitting only.</summary>
        public void Flush(int milliseconds)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Connected && Volatile.Read(ref _outgoingCount) > 0 && sw.ElapsedMilliseconds < milliseconds)
            {
                Thread.Sleep(5);
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            Close();
        }

        private void Close()
        {
            Connected = false;
            try { _stream?.Close(); } catch { /* already gone */ }
            try { _tcp?.Close(); } catch { /* already gone */ }
            _stream = null;
            _tcp = null;
            string dropped;
            while (_outgoing.TryDequeue(out dropped))
            {
            }

            Volatile.Write(ref _outgoingCount, 0);
            _latest = null;
        }

        private void Say(string text)
        {
            Log?.Invoke(text);
        }

        // ---------------------------------------------------------------- reader (also connects)

        private void ReadLoop()
        {
            while (!_stop)
            {
                try
                {
                    Connect();
                    Say("connected to Minecraft on " + _host + ":" + _port);
                    ReceiveFrames();
                }
                catch (Exception e)
                {
                    if (!_stop && !(e is IOException) && !(e is SocketException) && !(e is ObjectDisposedException))
                    {
                        Say("link error: " + e.Message);
                    }
                }

                bool was = Connected;
                Close();
                if (was && !_stop)
                {
                    Say("disconnected from Minecraft");
                }

                for (int i = 0; i < 10 && !_stop; i++)
                {
                    Thread.Sleep(100); // retry about once a second
                }
            }
        }

        private void Connect()
        {
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                Handshake(tcp);
            }
            catch
            {
                tcp.Close(); // a failed attempt must not leak its socket: this retries once a second
                throw;
            }
        }

        private void Handshake(TcpClient tcp)
        {
            tcp.Connect(_host, _port);
            var stream = tcp.GetStream();
            stream.WriteTimeout = 2000;

            var keyBytes = new byte[16];
            _rng.NextBytes(keyBytes);
            string key = Convert.ToBase64String(keyBytes);
            string request = "GET / HTTP/1.1\r\nHost: " + _host + ":" + _port + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                             "Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
            byte[] req = Encoding.ASCII.GetBytes(request);
            stream.Write(req, 0, req.Length);

            // read exactly up to the blank line: whatever follows is already the first frame
            var head = new StringBuilder();
            var one = new byte[1];
            while (!(head.Length >= 4 && head[head.Length - 4] == '\r' && head[head.Length - 3] == '\n' && head[head.Length - 2] == '\r' && head[head.Length - 1] == '\n'))
            {
                if (stream.Read(one, 0, 1) != 1)
                {
                    throw new IOException("closed during handshake");
                }

                head.Append((char)one[0]);
                if (head.Length > 8192)
                {
                    throw new IOException("handshake response too long");
                }
            }

            string response = head.ToString();
            if (!response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
            {
                throw new IOException("not a WebSocket server: " + response.Split('\r')[0]);
            }

            string expected;
            using (var sha1 = SHA1.Create())
            {
                expected = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + Guid)));
            }

            if (response.IndexOf("Sec-WebSocket-Accept: " + expected, StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new IOException("bad Sec-WebSocket-Accept");
            }

            stream.ReadTimeout = Timeout.Infinite;
            _tcp = tcp;
            _stream = stream;
            Connected = true;
        }

        private void ReceiveFrames()
        {
            var stream = _stream;
            var message = new MemoryStream();
            int messageOpcode = 0;
            while (!_stop)
            {
                int b0 = ReadByte(stream), b1 = ReadByte(stream);
                bool fin = (b0 & 0x80) != 0;
                int opcode = b0 & 0x0F;
                bool masked = (b1 & 0x80) != 0;
                long len = b1 & 0x7F;
                if (len == 126)
                {
                    len = (ReadByte(stream) << 8) | ReadByte(stream);
                }
                else if (len == 127)
                {
                    len = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        len = (len << 8) | (uint)ReadByte(stream);
                    }
                }

                if (len > 64 * 1024 * 1024)
                {
                    throw new IOException("frame too large");
                }

                var mask = new byte[4];
                if (masked)
                {
                    ReadExact(stream, mask, 4);
                }

                var payload = new byte[len];
                ReadExact(stream, payload, (int)len);
                if (masked)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] ^= mask[i & 3];
                    }
                }

                switch (opcode)
                {
                    case 0x8: // close
                        WriteFrame(0x8, payload);
                        return;
                    case 0x9: // ping
                        WriteFrame(0xA, payload);
                        continue;
                    case 0xA: // pong
                        continue;
                    case 0x1:
                    case 0x2:
                        message.SetLength(0);
                        messageOpcode = opcode;
                        message.Write(payload, 0, payload.Length);
                        break;
                    case 0x0: // continuation
                        message.Write(payload, 0, payload.Length);
                        break;
                    default:
                        continue;
                }

                if (fin)
                {
                    if (messageOpcode == 0x1)
                    {
                        _incoming.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    }

                    message.SetLength(0);
                }
            }
        }

        private static int ReadByte(Stream s)
        {
            int b = s.ReadByte();
            if (b < 0)
            {
                throw new IOException("connection closed");
            }

            return b;
        }

        private static void ReadExact(Stream s, byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buffer, got, count - got);
                if (n <= 0)
                {
                    throw new IOException("connection closed");
                }

                got += n;
            }
        }

        // ---------------------------------------------------------------- writer

        private void WriteLoop()
        {
            while (!_stop)
            {
                _wake.WaitOne(250);
                if (!Connected)
                {
                    continue;
                }

                try
                {
                    string m;
                    while (_outgoing.TryDequeue(out m))
                    {
                        Interlocked.Decrement(ref _outgoingCount);
                        WriteFrame(0x1, Encoding.UTF8.GetBytes(m));
                    }

                    string latest = Interlocked.Exchange(ref _latest, null);
                    if (latest != null)
                    {
                        WriteFrame(0x1, Encoding.UTF8.GetBytes(latest));
                    }
                }
                catch (Exception e)
                {
                    Say("send failed: " + e.Message);
                    try { _tcp?.Close(); } catch { /* the reader notices and reconnects */ }
                }
            }
        }

        private void WriteFrame(int opcode, byte[] payload)
        {
            var stream = _stream;
            if (stream == null)
            {
                return;
            }

            // client frames are masked (RFC 6455 section 5.3)
            var mask = new byte[4];
            lock (_frameLock)
            {
                _rng.NextBytes(mask);
                int headerLen = payload.Length < 126 ? 2 : payload.Length <= 0xFFFF ? 4 : 10;
                var frame = new byte[headerLen + 4 + payload.Length];
                frame[0] = (byte)(0x80 | opcode);
                if (payload.Length < 126)
                {
                    frame[1] = (byte)(0x80 | payload.Length);
                }
                else if (payload.Length <= 0xFFFF)
                {
                    frame[1] = 0x80 | 126;
                    frame[2] = (byte)(payload.Length >> 8);
                    frame[3] = (byte)payload.Length;
                }
                else
                {
                    frame[1] = 0x80 | 127;
                    long n = payload.Length;
                    for (int i = 0; i < 8; i++)
                    {
                        frame[9 - i] = (byte)(n >> (8 * i));
                    }
                }

                Buffer.BlockCopy(mask, 0, frame, headerLen, 4);
                for (int i = 0; i < payload.Length; i++)
                {
                    frame[headerLen + 4 + i] = (byte)(payload[i] ^ mask[i & 3]);
                }

                stream.Write(frame, 0, frame.Length);
            }
        }
    }
}
