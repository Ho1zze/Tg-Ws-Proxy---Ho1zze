#pragma warning disable 4014
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TgProxyHoizze {
    public class ProxyServer {
        private readonly ProxySettings _settings;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private WsConnectionPool _pool;
        private static string _activeCfDomain = null;

        private int _activePort;
        private int _activeConnections;
        private long _totalConnections;
        private long _bytesReceived;
        private long _bytesSent;

        public event Action<string, string> LogMessage;
        public event Action<bool> StatusChanged;

        public int ActivePort {
            get { return _activePort; }
            private set { _activePort = value; }
        }

        public int ActiveConnections {
            get { return Thread.VolatileRead(ref _activeConnections); }
        }

        public long TotalConnections {
            get { return Interlocked.Read(ref _totalConnections); }
        }

        public long BytesReceived {
            get { return Interlocked.Read(ref _bytesReceived); }
        }

        public long BytesSent {
            get { return Interlocked.Read(ref _bytesSent); }
        }

        public DateTime StartTime { get; private set; }

        public bool IsRunning {
            get { return _isRunning; }
        }

        public ProxyServer(ProxySettings settings) {
            _settings = settings;
            ActivePort = settings.Port;
        }

        private void Log(string level, string msg) {
            var handler = LogMessage;
            if (handler != null) {
                handler(level, msg);
            }
        }

        public void Start() {
            if (_isRunning) return;

            int preferredPort = _settings.Port;
            bool bound = false;
            Exception lastEx = null;

            int[] portsToTry = new int[] { preferredPort, 8443, 11443, 8080, 1080 };
            for (int i = 0; i < portsToTry.Length; i++) {
                int p = portsToTry[i];
                try {
                    _listener = new TcpListener(IPAddress.Loopback, p);
                    _listener.Start();
                    bound = true;
                    ActivePort = p;
                    if (p != preferredPort) {
                        Log("WARN", string.Format("Порт {0} занят. Выбран свободный порт {1}.", preferredPort, p));
                    }
                    break;
                } catch (Exception ex) {
                    lastEx = ex;
                    try { if (_listener != null) _listener.Stop(); } catch { }
                    _listener = null;
                }
            }

            if (!bound) {
                _isRunning = false;
                string errMsg = lastEx != null ? lastEx.Message : "Все порты заняты";
                Log("ERROR", "Не удалось запустить прокси: " + errMsg);
                if (StatusChanged != null) StatusChanged(false);
                throw lastEx ?? new InvalidOperationException("Failed to bind port");
            }

            try {
                _cts = new CancellationTokenSource();
                _isRunning = true;
                StartTime = DateTime.Now;

                Log("OK", string.Format("TG PROXY Ho1zze запущен на 127.0.0.1:{0}", ActivePort));
                Log("INFO", string.Format("Целевой DC: DC 4 -> 149.154.167.220:443 (WebSocket: wss://kws4.web.telegram.org/apiws)"));
                Log("INFO", string.Format("MTProto Secret: {0}", _settings.Secret));

                if (StatusChanged != null) StatusChanged(true);

                _pool = new WsConnectionPool(_settings, ConnectDirectWsAsync, Log);
                Task.Run(() => AcceptLoopAsync(_cts.Token));
            } catch (Exception ex) {
                _isRunning = false;
                Log("ERROR", "Ошибка запуска: " + ex.Message);
                if (StatusChanged != null) StatusChanged(false);
                throw;
            }
        }

        public void Stop() {
            if (!_isRunning) return;
            _isRunning = false;

            try {
                if (_pool != null) {
                    _pool.Dispose();
                    _pool = null;
                }
                if (_cts != null) {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }
                if (_listener != null) {
                    _listener.Stop();
                    _listener = null;
                }
            } catch { }

            Log("INFO", "Прокси сервер остановлен");
            if (StatusChanged != null) StatusChanged(false);
        }

        private async Task AcceptLoopAsync(CancellationToken ct) {
            while (_isRunning && !ct.IsCancellationRequested) {
                try {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    RawWebSocket.ConfigureSocket(client);
                    Interlocked.Increment(ref _totalConnections);
                    Interlocked.Increment(ref _activeConnections);

                    Task.Run(() => HandleClientAsync(client, ct));
                } catch (ObjectDisposedException) {
                    break;
                } catch (Exception ex) {
                    if (_isRunning) {
                        Log("WARN", "Ошибка при приеме подключения: " + ex.Message);
                    }
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct) {
            string ep = client.Client != null && client.Client.RemoteEndPoint != null
                ? client.Client.RemoteEndPoint.ToString()
                : "Unknown";

            try {
                NetworkStream stream = client.GetStream();

                using (var hsCts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    hsCts.CancelAfter(10000); // 10 second timeout for handshake

                    byte[] initBuf = new byte[64];
                    int firstRead = await stream.ReadAsync(initBuf, 0, 1, hsCts.Token);
                    if (firstRead <= 0) return;

                    if (initBuf[0] == 0x05) {
                        // SOCKS5 protocol
                        await HandleSocks5ClientAsync(client, stream, ep, ct);
                    } else {
                        // MTProto Obfuscated Handshake (64 bytes)
                        await ReadExactAsync(stream, initBuf, 1, 63, hsCts.Token);
                        await HandleMtprotoClientAsync(client, stream, initBuf, ep, ct);
                    }
                }
            } catch (Exception ex) {
                Log("DEBUG", string.Format("[{0}] Завершение сессии: {1}", ep, ex.Message));
            } finally {
                Interlocked.Decrement(ref _activeConnections);
                try { client.Close(); } catch { }
            }
        }

        private async Task HandleSocks5ClientAsync(TcpClient client, NetworkStream stream, string ep, CancellationToken ct) {
            Log("CONN", string.Format("[{0}] SOCKS5 клиент подключен", ep));

            // SOCKS5 greeting
            byte[] nmethodsBuf = new byte[1];
            await stream.ReadAsync(nmethodsBuf, 0, 1, ct);
            int nmethods = nmethodsBuf[0];
            byte[] methods = new byte[nmethods];
            await ReadExactAsync(stream, methods, 0, nmethods, ct);

            // SOCKS5 response: NO AUTH
            byte[] authResp = new byte[] { 0x05, 0x00 };
            await stream.WriteAsync(authResp, 0, authResp.Length, ct);
            await stream.FlushAsync(ct);

            // Read connect request
            byte[] reqHead = new byte[4];
            await ReadExactAsync(stream, reqHead, 0, 4, ct);
            if (reqHead[1] != 0x01) return; // Only CONNECT supported

            int addrType = reqHead[3];
            string targetHost = "kws4.web.telegram.org";
            if (addrType == 0x01) {
                byte[] ipBuf = new byte[4];
                await ReadExactAsync(stream, ipBuf, 0, 4, ct);
            } else if (addrType == 0x03) {
                byte[] lenBuf = new byte[1];
                await ReadExactAsync(stream, lenBuf, 0, 1, ct);
                byte[] domBuf = new byte[lenBuf[0]];
                await ReadExactAsync(stream, domBuf, 0, domBuf.Length, ct);
                targetHost = System.Text.Encoding.ASCII.GetString(domBuf);
            }
            byte[] portBuf = new byte[2];
            await ReadExactAsync(stream, portBuf, 0, 2, ct);

            // Send SOCKS5 success: 05 00 00 01 7F 00 00 01 05 A3
            byte[] succResp = new byte[] { 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0x05, 0xA3 };
            await stream.WriteAsync(succResp, 0, succResp.Length, ct);
            await stream.FlushAsync(ct);

            try {
                using (var ws = await _pool.GetConnectionAsync(4, ep)) {
                    Log("OK", string.Format("[{0}] SOCKS5 туннель активен", ep));

                    using (var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                        var t1 = PipeRawStreamToWsAsync(stream, ws, ep, sessionCts.Token);
                        var t2 = PipeWsToRawStreamAsync(ws, stream, ep, sessionCts.Token);
                        var t3 = HeartbeatLoopAsync(ws, sessionCts.Token);
                        await Task.WhenAny(t1, t2);
                        sessionCts.Cancel();
                    }
                }
            } catch (Exception ex) {
                Log("WARN", string.Format("[{0}] Ошибка SOCKS5 туннеля: {1}", ep, ex.Message));
            }
        }

        private async Task<RawWebSocket> ConnectDirectWsAsync(int dcId, string ep) {
            string targetIp = _settings.GetParsedIp(dcId);
            string directHost = string.Format("kws{0}.web.telegram.org", dcId);

            // 1. Попытка прямого подключения к IP DC (таймаут 2500мс)
            try {
                return await RawWebSocket.ConnectAsync(targetIp, directHost, 443, "/apiws", 2500);
            } catch (Exception) {
                if (!_settings.CfProxy) {
                    throw;
                }
            }

            // 2. Cloudflare fallback (Flowseal method)
            var domains = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(_activeCfDomain)) {
                domains.Add(_activeCfDomain);
            }
            foreach (var cfDom in RawWebSocket.CloudflareFallbackDomains) {
                if (!domains.Contains(cfDom)) domains.Add(cfDom);
            }

            foreach (var cfDom in domains) {
                string cfHost = string.Format("kws{0}.{1}", dcId, cfDom);

                // Попытка 1: HTTPS (порт 443, TLS)
                try {
                    var ws = await RawWebSocket.ConnectByHostAsync(cfHost, 443, "/apiws", true, 3000);
                    _activeCfDomain = cfDom;
                    return ws;
                } catch { }

                // Попытка 2: HTTP (порт 80, без TLS - обход блокировок SNI)
                try {
                    var ws = await RawWebSocket.ConnectByHostAsync(cfHost, 80, "/apiws", false, 3000);
                    _activeCfDomain = cfDom;
                    return ws;
                } catch { }
            }

            throw new TimeoutException(string.Format("Не удалось подключиться к Telegram DC {0} ни напрямую, ни через Cloudflare Proxy.", dcId));
        }

        private async Task HandleMtprotoClientAsync(TcpClient client, NetworkStream stream, byte[] handshake64, string ep, CancellationToken ct) {
            var handshake = CryptoUtils.TryParseHandshake(handshake64, _settings.Secret);
            if (handshake == null) {
                Log("WARN", string.Format("[{0}] Ошибка MTProto handshake! Неверный секретный ключ.", ep));
                return;
            }

            Log("OK", string.Format("[{0}] MTProto Handshake OK! Протокол: 0x{1:X8}, DC: {2}", ep, handshake.ProtocolTag, handshake.DcId));

            try {
                using (var ws = await _pool.GetConnectionAsync(handshake.DcId, ep)) {
                    Log("OK", string.Format("[{0}] WebSocket туннель к DC {1} успешно открыт", ep, handshake.DcId));

                    var relay = RelayHandshake.Create(handshake.RawDc, handshake.ProtocolTag);
                    await ws.SendBinaryFrameAsync(relay.RelayInitBytes, 0, 64);

                    using (var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                        var t1 = PipeClientMtprotoToWsAsync(stream, ws, ep, handshake, relay, sessionCts.Token);
                        var t2 = PipeWsToClientMtprotoAsync(ws, stream, ep, handshake, relay, sessionCts.Token);
                        var t3 = HeartbeatLoopAsync(ws, sessionCts.Token);
                        await Task.WhenAny(t1, t2);
                        sessionCts.Cancel();
                    }
                }
            } catch (Exception ex) {
                Log("WARN", string.Format("[{0}] Ошибка MTProto WebSocket туннеля (DC {1}): {2}", ep, handshake.DcId, ex.Message));
            }
        }

        private async Task HeartbeatLoopAsync(RawWebSocket ws, CancellationToken ct) {
            while (ws != null && ws.IsConnected && !ct.IsCancellationRequested) {
                try {
                    await Task.Delay(15000, ct); // Check every 15 seconds
                    if (ws != null && ws.IsConnected && (DateTime.Now - ws.LastActivity).TotalSeconds >= 15) {
                        await ws.SendPingAsync();
                    }
                } catch {
                    break;
                }
            }
        }

        private async Task PipeClientMtprotoToWsAsync(NetworkStream clientStream, RawWebSocket ws, string ep, HandshakeResult handshake, RelayHandshake relay, CancellationToken ct) {
            byte[] lenBuf = new byte[4];
            try {
                while (clientStream.CanRead && ws.IsConnected && !ct.IsCancellationRequested) {
                    // 1. Read 4-byte length prefix
                    await ReadExactAsync(clientStream, lenBuf, 0, 4, ct);
                    Interlocked.Add(ref _bytesReceived, 4);

                    // Decrypt length with client cipher
                    handshake.Decryptor.Process(lenBuf, 0, 4);

                    int length = BitConverter.ToInt32(lenBuf, 0);
                    if (length <= 0 || length > 4 * 1024 * 1024) {
                        Log("WARN", string.Format("[{0}] Недопустимый размер пакета MTProto: {1}", ep, length));
                        break;
                    }

                    // 2. Read packet payload
                    byte[] payload = new byte[length];
                    await ReadExactAsync(clientStream, payload, 0, length, ct);
                    Interlocked.Add(ref _bytesReceived, length);

                    // Decrypt payload with client cipher
                    handshake.Decryptor.Process(payload, 0, length);

                    // 3. Assemble full intermediate packet: [4 bytes length][payload]
                    byte[] fullPacket = new byte[4 + length];
                    Buffer.BlockCopy(lenBuf, 0, fullPacket, 0, 4);
                    Buffer.BlockCopy(payload, 0, fullPacket, 4, length);

                    // 4. Re-encrypt with upstream cipher for Telegram server
                    relay.UpstreamEncryptor.Process(fullPacket, 0, fullPacket.Length);

                    // 5. Send as single WebSocket binary frame
                    await ws.SendBinaryFrameAsync(fullPacket, 0, fullPacket.Length);
                }
            } catch (Exception ex) {
                Log("DEBUG", string.Format("[{0}] MTProto Client -> WS: {1}", ep, ex.Message));
            }
        }

        private async Task PipeWsToClientMtprotoAsync(RawWebSocket ws, NetworkStream clientStream, string ep, HandshakeResult handshake, RelayHandshake relay, CancellationToken ct) {
            try {
                while (clientStream.CanWrite && ws.IsConnected && !ct.IsCancellationRequested) {
                    byte[] frame = await ws.ReceiveFrameAsync(ct);
                    if (frame == null || frame.Length == 0) break;

                    Interlocked.Add(ref _bytesSent, frame.Length);

                    // 1. Decrypt frame received from Telegram server
                    relay.UpstreamDecryptor.Process(frame, 0, frame.Length);

                    // 2. Re-encrypt with client cipher
                    handshake.Encryptor.Process(frame, 0, frame.Length);

                    // 3. Send to Telegram client
                    await clientStream.WriteAsync(frame, 0, frame.Length, ct);
                    await clientStream.FlushAsync(ct);
                }
            } catch (Exception ex) {
                Log("DEBUG", string.Format("[{0}] MTProto WS -> Client: {1}", ep, ex.Message));
            }
        }

        private async Task PipeRawStreamToWsAsync(NetworkStream clientStream, RawWebSocket ws, string ep, CancellationToken ct) {
            byte[] buf = new byte[16384];
            try {
                while (clientStream.CanRead && ws.IsConnected && !ct.IsCancellationRequested) {
                    int read = await clientStream.ReadAsync(buf, 0, buf.Length, ct);
                    if (read <= 0) break;

                    Interlocked.Add(ref _bytesReceived, read);

                    await ws.SendBinaryFrameAsync(buf, 0, read);
                }
            } catch (Exception ex) {
                Log("DEBUG", string.Format("[{0}] SOCKS5 Client -> WS: {1}", ep, ex.Message));
            }
        }

        private async Task PipeWsToRawStreamAsync(RawWebSocket ws, NetworkStream clientStream, string ep, CancellationToken ct) {
            try {
                while (clientStream.CanWrite && ws.IsConnected && !ct.IsCancellationRequested) {
                    byte[] frame = await ws.ReceiveFrameAsync(ct);
                    if (frame == null || frame.Length == 0) break;

                    Interlocked.Add(ref _bytesSent, frame.Length);

                    await clientStream.WriteAsync(frame, 0, frame.Length, ct);
                    await clientStream.FlushAsync(ct);
                }
            } catch (Exception ex) {
                Log("DEBUG", string.Format("[{0}] SOCKS5 WS -> Client: {1}", ep, ex.Message));
            }
        }

        private static async Task ReadExactAsync(Stream s, byte[] buf, int offset, int count, CancellationToken ct) {
            int total = 0;
            while (total < count) {
                int read = await s.ReadAsync(buf, offset + total, count - total, ct);
                if (read <= 0) throw new EndOfStreamException("Соединение закрыто клиентом или сервером");
                total += read;
            }
        }
    }
}
