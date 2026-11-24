using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TgProxyHoizze {
    public class RawWebSocket : IDisposable {
        private TcpClient _tcp;
        private Stream _stream;
        private readonly Random _rng = new Random();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        public DateTime CreatedAt { get; private set; }
        public DateTime LastActivity { get; set; }

        public bool IsConnected {
            get { return !_isDisposed && _tcp != null && _tcp.Connected && _stream != null; }
        }

        public static readonly string[] CloudflareFallbackDomains = new string[] {
            "pclead.co.uk", "sorochka.co.uk", "noskom.co.uk", "karton.co.uk",
            "stopblock.co.uk", "noteb.co.uk", "havegrain.co.uk", "pyatdesyat.co.uk", "sadovaya.co.uk"
        };

        public RawWebSocket() {
            CreatedAt = DateTime.Now;
            LastActivity = DateTime.Now;
        }

        public static void ConfigureSocket(TcpClient tcp, int bufferSize = 256 * 1024) {
            if (tcp == null) return;
            try {
                tcp.NoDelay = true;
                tcp.ReceiveBufferSize = bufferSize;
                tcp.SendBufferSize = bufferSize;

                var s = tcp.Client;
                if (s != null) {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    // Windows SIO_KEEPALIVE_VALS: 15 seconds idle, 5 seconds interval
                    byte[] inOptionValues = new byte[12];
                    BitConverter.GetBytes(1).CopyTo(inOptionValues, 0); // on
                    BitConverter.GetBytes(15000).CopyTo(inOptionValues, 4); // idle ms
                    BitConverter.GetBytes(5000).CopyTo(inOptionValues, 8); // interval ms
                    s.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
                }
            } catch { }
        }

        public bool IsSocketAlive() {
            try {
                if (_isDisposed || _tcp == null || !_tcp.Connected || _tcp.Client == null || _stream == null) {
                    return false;
                }
                // If SelectRead is true and Available is 0, the remote end closed the connection (FIN/RST)
                if (_tcp.Client.Poll(50, SelectMode.SelectRead) && _tcp.Client.Available == 0) {
                    return false;
                }
                return true;
            } catch {
                return false;
            }
        }

        public static async Task<RawWebSocket> ConnectAsync(string ip, string host, int port = 443, string path = "/apiws", int timeoutMs = 2500) {
            var client = new RawWebSocket();
            client._tcp = new TcpClient();
            ConfigureSocket(client._tcp);

            var connectTask = client._tcp.ConnectAsync(ip, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask) {
                client.Dispose();
                throw new TimeoutException(string.Format("Connection to {0}:{1} timed out ({2}ms).", ip, port, timeoutMs));
            }
            await connectTask;

            var ssl = new SslStream(client._tcp.GetStream(), false, (s, cert, chain, err) => true);
            var sslTask = ssl.AuthenticateAsClientAsync(host);
            if (await Task.WhenAny(sslTask, Task.Delay(timeoutMs)) != sslTask) {
                ssl.Dispose();
                client.Dispose();
                throw new TimeoutException(string.Format("TLS handshake with {0} timed out.", host));
            }
            await sslTask;
            client._stream = ssl;

            await client.PerformWebSocketHandshakeAsync(host, path);
            client.LastActivity = DateTime.Now;
            return client;
        }

        public static async Task<RawWebSocket> ConnectByHostAsync(string host, int port = 443, string path = "/apiws", bool useTls = true, int timeoutMs = 3000) {
            var client = new RawWebSocket();
            client._tcp = new TcpClient();
            ConfigureSocket(client._tcp);

            var connectTask = client._tcp.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask) {
                client.Dispose();
                throw new TimeoutException(string.Format("Connection to {0}:{1} timed out.", host, port));
            }
            await connectTask;

            if (useTls) {
                var ssl = new SslStream(client._tcp.GetStream(), false, (s, cert, chain, err) => true);
                var sslTask = ssl.AuthenticateAsClientAsync(host);
                if (await Task.WhenAny(sslTask, Task.Delay(timeoutMs)) != sslTask) {
                    ssl.Dispose();
                    client.Dispose();
                    throw new TimeoutException(string.Format("TLS handshake with {0} timed out.", host));
                }
                await sslTask;
                client._stream = ssl;
            } else {
                client._stream = client._tcp.GetStream();
            }

            await client.PerformWebSocketHandshakeAsync(host, path);
            client.LastActivity = DateTime.Now;
            return client;
        }

        private async Task PerformWebSocketHandshakeAsync(string host, string path) {
            byte[] nonce = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) {
                rng.GetBytes(nonce);
            }
            string secKey = Convert.ToBase64String(nonce);

            string req = string.Format(
                "GET {0} HTTP/1.1\r\n" +
                "Host: {1}\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Key: {2}\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                "Sec-WebSocket-Protocol: binary\r\n" +
                "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\r\n\r\n",
                path, host, secKey
            );

            byte[] reqBytes = Encoding.ASCII.GetBytes(req);
            await _stream.WriteAsync(reqBytes, 0, reqBytes.Length);
            await _stream.FlushAsync();

            string respLine = await ReadLineAsync(_stream);
            if (string.IsNullOrEmpty(respLine) || !respLine.Contains("101")) {
                Dispose();
                throw new InvalidOperationException("WebSocket handshake rejected: " + respLine);
            }

            while (true) {
                string h = await ReadLineAsync(_stream);
                if (string.IsNullOrEmpty(h) || h == "\r" || h == "") break;
            }
        }

        public async Task SendBinaryFrameAsync(byte[] payload, int offset, int count) {
            if (!IsConnected) throw new IOException("WebSocket is not connected");

            byte[] mask = new byte[4];
            lock (_rng) {
                _rng.NextBytes(mask);
            }

            int headerLen = 2 + (count <= 125 ? 0 : (count <= 65535 ? 2 : 8)) + 4;
            byte[] frame = new byte[headerLen + count];

            frame[0] = 0x82; // FIN = 1, Opcode = 2 (Binary)
            int pos = 1;

            if (count <= 125) {
                frame[pos++] = (byte)(0x80 | count);
            } else if (count <= 65535) {
                frame[pos++] = (byte)(0x80 | 126);
                frame[pos++] = (byte)((count >> 8) & 0xFF);
                frame[pos++] = (byte)(count & 0xFF);
            } else {
                frame[pos++] = (byte)(0x80 | 127);
                for (int i = 7; i >= 0; i--) {
                    frame[pos++] = (byte)(((long)count >> (8 * i)) & 0xFF);
                }
            }

            frame[pos++] = mask[0];
            frame[pos++] = mask[1];
            frame[pos++] = mask[2];
            frame[pos++] = mask[3];

            for (int i = 0; i < count; i++) {
                frame[pos + i] = (byte)(payload[offset + i] ^ mask[i % 4]);
            }

            await _writeLock.WaitAsync();
            try {
                if (!IsConnected) throw new IOException("WebSocket disconnected during write");
                await _stream.WriteAsync(frame, 0, frame.Length);
                await _stream.FlushAsync();
                LastActivity = DateTime.Now;
            } finally {
                _writeLock.Release();
            }
        }

        public async Task SendPingAsync() {
            if (!IsConnected) return;
            try {
                byte[] mask = new byte[4];
                lock (_rng) {
                    _rng.NextBytes(mask);
                }

                byte[] frame = new byte[] {
                    0x89, // FIN = 1, Opcode = 9 (Ping)
                    0x80, // Masked, Length = 0
                    mask[0], mask[1], mask[2], mask[3]
                };

                await _writeLock.WaitAsync();
                try {
                    if (IsConnected) {
                        await _stream.WriteAsync(frame, 0, frame.Length);
                        await _stream.FlushAsync();
                        LastActivity = DateTime.Now;
                    }
                } finally {
                    _writeLock.Release();
                }
            } catch { }
        }

        public async Task SendPongAsync(byte[] pingPayload) {
            if (!IsConnected) return;
            try {
                byte[] mask = new byte[4];
                lock (_rng) {
                    _rng.NextBytes(mask);
                }

                int count = pingPayload != null ? pingPayload.Length : 0;
                if (count > 125) count = 125; // Control frame payload limit is 125 bytes

                byte[] frame = new byte[6 + count];
                frame[0] = 0x8A; // FIN = 1, Opcode = 10 (Pong)
                frame[1] = (byte)(0x80 | count);
                frame[2] = mask[0]; frame[3] = mask[1]; frame[4] = mask[2]; frame[5] = mask[3];
                for (int i = 0; i < count; i++) {
                    frame[6 + i] = (byte)(pingPayload[i] ^ mask[i % 4]);
                }

                await _writeLock.WaitAsync();
                try {
                    if (IsConnected) {
                        await _stream.WriteAsync(frame, 0, frame.Length);
                        await _stream.FlushAsync();
                        LastActivity = DateTime.Now;
                    }
                } finally {
                    _writeLock.Release();
                }
            } catch { }
        }

        public async Task<byte[]> ReceiveFrameAsync(CancellationToken ct = default(CancellationToken)) {
            while (IsConnected) {
                byte[] header = new byte[2];
                await ReadExactAsync(_stream, header, 0, 2, ct);
                LastActivity = DateTime.Now;

                byte b0 = header[0];
                byte b1 = header[1];

                int opcode = b0 & 0x0F;
                bool isMasked = (b1 & 0x80) != 0;
                long len = b1 & 0x7F;

                if (len == 126) {
                    byte[] ext = new byte[2];
                    await ReadExactAsync(_stream, ext, 0, 2, ct);
                    len = (ext[0] << 8) | ext[1];
                } else if (len == 127) {
                    byte[] ext = new byte[8];
                    await ReadExactAsync(_stream, ext, 0, 8, ct);
                    len = 0;
                    for (int i = 0; i < 8; i++) {
                        len = (len << 8) | ext[i];
                    }
                }

                byte[] mask = null;
                if (isMasked) {
                    mask = new byte[4];
                    await ReadExactAsync(_stream, mask, 0, 4, ct);
                }

                if (len > 10 * 1024 * 1024) {
                    throw new InvalidOperationException("WebSocket frame payload too large: " + len);
                }

                byte[] payload = new byte[(int)len];
                if (len > 0) {
                    await ReadExactAsync(_stream, payload, 0, (int)len, ct);
                }

                if (isMasked && mask != null) {
                    for (int i = 0; i < payload.Length; i++) {
                        payload[i] ^= mask[i % 4];
                    }
                }

                if (opcode == 0x09) {
                    // Ping from server -> reply with Pong
                    await SendPongAsync(payload);
                    continue;
                } else if (opcode == 0x0A) {
                    // Pong from server -> Keep-alive heartbeat acknowledged!
                    continue;
                } else if (opcode == 0x08) {
                    // Close from server
                    Dispose();
                    return null;
                } else if (opcode == 0x02 || opcode == 0x01) {
                    // Binary or Text MTProto frame
                    return payload;
                }
            }
            return null;
        }

        private static async Task<string> ReadLineAsync(Stream stream) {
            var ms = new MemoryStream();
            byte[] buf = new byte[1];
            while (true) {
                int read = await stream.ReadAsync(buf, 0, 1);
                if (read <= 0) break;
                if (buf[0] == '\n') break;
                if (buf[0] != '\r') ms.WriteByte(buf[0]);
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        private static async Task ReadExactAsync(Stream s, byte[] buf, int offset, int count, CancellationToken ct) {
            int total = 0;
            while (total < count) {
                int read = await s.ReadAsync(buf, offset + total, count - total, ct);
                if (read <= 0) throw new EndOfStreamException("End of stream reached before full frame read");
                total += read;
            }
        }

        public void Dispose() {
            if (_isDisposed) return;
            _isDisposed = true;
            try { if (_stream != null) _stream.Close(); } catch { }
            try { if (_tcp != null) _tcp.Close(); } catch { }
            try { _writeLock.Dispose(); } catch { }
            _stream = null;
            _tcp = null;
        }
    }
}
