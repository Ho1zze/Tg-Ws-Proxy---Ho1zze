using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TgProxyHoizze {
    public class WsConnectionPool : IDisposable {
        private readonly ProxySettings _settings;
        private readonly Func<int, string, Task<RawWebSocket>> _connector;
        private readonly Action<string, string> _logger;
        private readonly ConcurrentDictionary<int, ConcurrentQueue<RawWebSocket>> _pools;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _isDisposed;

        public WsConnectionPool(ProxySettings settings, Func<int, string, Task<RawWebSocket>> connector, Action<string, string> logger) {
            _settings = settings;
            _connector = connector;
            _logger = logger;
            _pools = new ConcurrentDictionary<int, ConcurrentQueue<RawWebSocket>>();

            // Start background pool maintainer
            Task.Run(() => MaintainLoopAsync(_cts.Token));
        }

        public async Task<RawWebSocket> GetConnectionAsync(int dcId, string ep) {
            ConcurrentQueue<RawWebSocket> q;
            if (_pools.TryGetValue(dcId, out q)) {
                RawWebSocket ws;
                while (q.TryDequeue(out ws)) {
                    if (ws != null && ws.IsConnected && ws.IsSocketAlive() && (DateTime.Now - ws.CreatedAt).TotalSeconds < 25) {
                        _logger("POOL", string.Format("[{0}] Подключение из пула (DC {1})", ep, dcId));
                        TriggerRefill(dcId);
                        return ws;
                    }
                    if (ws != null) {
                        try { ws.Dispose(); } catch { }
                    }
                }
            }

            // Pool empty or stale: connect directly
            var directWs = await _connector(dcId, ep);
            TriggerRefill(dcId);
            return directWs;
        }

        public void TriggerRefill(int dcId) {
            if (_isDisposed) return;
            Task.Run(async () => {
                try {
                    await RefillPoolAsync(dcId, _cts.Token);
                } catch { }
            });
        }

        private async Task RefillPoolAsync(int dcId, CancellationToken ct) {
            int targetSize = Math.Max(1, Math.Min(_settings.PoolSize, 2));
            var q = _pools.GetOrAdd(dcId, _ => new ConcurrentQueue<RawWebSocket>());

            if (q.Count >= targetSize || ct.IsCancellationRequested || _isDisposed) return;

            try {
                var ws = await _connector(dcId, "PoolRefill");
                if (ws != null && ws.IsConnected && ws.IsSocketAlive()) {
                    q.Enqueue(ws);
                    _logger("POOL", string.Format("Пул готов: DC {0} ({1}/{2} соед.)", dcId, q.Count, targetSize));
                }
            } catch { }
        }

        private async Task MaintainLoopAsync(CancellationToken ct) {
            // Initial warm-up for primary DC 4
            await Task.Delay(1000, ct);
            TriggerRefill(4);

            while (!ct.IsCancellationRequested && !_isDisposed) {
                try {
                    await Task.Delay(15000, ct); // Check every 15 seconds

                    int[] primaryDcs = new int[] { 2, 4 };
                    foreach (int dc in primaryDcs) {
                        ConcurrentQueue<RawWebSocket> q;
                        if (_pools.TryGetValue(dc, out q)) {
                            int count = q.Count;
                            for (int i = 0; i < count; i++) {
                                RawWebSocket ws;
                                if (q.TryDequeue(out ws)) {
                                    // If alive and under 25 seconds old, ping it and keep it; otherwise discard
                                    if (ws != null && ws.IsConnected && ws.IsSocketAlive() && (DateTime.Now - ws.CreatedAt).TotalSeconds < 25) {
                                        try { await ws.SendPingAsync(); } catch { }
                                        q.Enqueue(ws);
                                    } else {
                                        try { if (ws != null) ws.Dispose(); } catch { }
                                    }
                                }
                            }
                        }

                        // Maintain at least 1 warm connection for DC 4
                        if (dc == 4 && (q == null || q.Count < 1) && !ct.IsCancellationRequested) {
                            TriggerRefill(dc);
                        }
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch { }
            }
        }

        public void Dispose() {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _cts.Cancel(); _cts.Dispose(); } catch { }
            foreach (var kvp in _pools) {
                RawWebSocket ws;
                while (kvp.Value.TryDequeue(out ws)) {
                    try { if (ws != null) ws.Dispose(); } catch { }
                }
            }
            _pools.Clear();
        }
    }
}
