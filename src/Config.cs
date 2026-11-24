using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TgProxyHoizze {
    public class ProxySettings {
        public int Port { get; set; }
        public string Host { get; set; }
        public string DcIp { get; set; }
        public string Secret { get; set; }
        public bool AutoStart { get; set; }
        public bool AutoStartProxy { get; set; }
        public bool CfProxy { get; set; }
        public bool H2 { get; set; }
        public int PoolSize { get; set; }
        public int BufKb { get; set; }
        public string Language { get; set; }
        public bool Verbose { get; set; }
        public string CfProxyWorkerDomain { get; set; }

        public ProxySettings() {
            SetDefaults();
        }

        public void SetDefaults() {
            Port = 1443;
            Host = "127.0.0.1";
            DcIp = "4:149.154.167.220";
            Secret = "cf9106855e4be8121b426307cb2dd17b";
            AutoStart = false;
            AutoStartProxy = true;
            CfProxy = true;
            H2 = true;
            PoolSize = 8;
            BufKb = 256;
            Language = "ru";
            Verbose = false;
            CfProxyWorkerDomain = "";
        }

        public string GetParsedIp(int dcId) {
            try {
                if (!string.IsNullOrEmpty(DcIp)) {
                    string[] parts = DcIp.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts) {
                        string[] kv = part.Trim().Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (kv.Length == 2) {
                            int d;
                            if (int.TryParse(kv[0], out d) && d == dcId) {
                                return kv[1].Trim();
                            }
                        } else if (kv.Length == 1 && dcId == 4) {
                            return kv[0].Trim();
                        }
                    }
                }
            } catch { }

            // Default fallback IPs for Telegram DCs
            switch (dcId) {
                case 1: return "149.154.175.50";
                case 2: return "149.154.167.51";
                case 3: return "149.154.175.100";
                case 4: return "149.154.167.220";
                case 5: return "91.108.56.165";
                case 203: return "91.105.192.100";
                default: return "149.154.167.220";
            }
        }
    }

    public static class SettingsManager {
        private const string AppRegistryKeyName = "TG PROXY Ho1zze";
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static string ConfigDirectory {
            get {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "TG-PROXY-Ho1zze");
                if (!Directory.Exists(dir)) {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
                return dir;
            }
        }

        private static string ConfigFilePath {
            get {
                return Path.Combine(ConfigDirectory, "config.json");
            }
        }

        public static string FlowsealConfigPath {
            get {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "TgWsProxy", "config.json");
            }
        }

        public static bool FlowsealConfigExists {
            get {
                try {
                    return File.Exists(FlowsealConfigPath);
                } catch {
                    return false;
                }
            }
        }

        public static bool ImportFromFlowseal(ProxySettings target) {
            try {
                if (!FlowsealConfigExists) return false;
                string json = File.ReadAllText(FlowsealConfigPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                if (dict == null) return false;

                if (dict.ContainsKey("port") && dict["port"] != null) {
                    int p;
                    if (int.TryParse(dict["port"].ToString(), out p) && p > 0 && p < 65536) target.Port = p;
                }
                if (dict.ContainsKey("secret") && dict["secret"] != null) {
                    string s = dict["secret"].ToString().Trim();
                    if (!string.IsNullOrEmpty(s)) target.Secret = s;
                }
                if (dict.ContainsKey("host") && dict["host"] != null) {
                    string h = dict["host"].ToString().Trim();
                    if (!string.IsNullOrEmpty(h)) target.Host = h;
                }
                if (dict.ContainsKey("dc_ip") && dict["dc_ip"] != null) {
                    var list = dict["dc_ip"] as System.Collections.ArrayList;
                    if (list != null && list.Count > 0) {
                        target.DcIp = list[0].ToString().Trim();
                    } else if (dict["dc_ip"] is string) {
                        target.DcIp = dict["dc_ip"].ToString().Trim();
                    }
                }
                if (dict.ContainsKey("cfproxy") && dict["cfproxy"] != null) {
                    bool cf;
                    if (bool.TryParse(dict["cfproxy"].ToString(), out cf)) target.CfProxy = cf;
                }
                if (dict.ContainsKey("h2") && dict["h2"] != null) {
                    bool h2;
                    if (bool.TryParse(dict["h2"].ToString(), out h2)) target.H2 = h2;
                }
                if (dict.ContainsKey("pool_size") && dict["pool_size"] != null) {
                    int ps;
                    if (int.TryParse(dict["pool_size"].ToString(), out ps) && ps > 0) target.PoolSize = ps;
                }
                if (dict.ContainsKey("buf_kb") && dict["buf_kb"] != null) {
                    int bk;
                    if (int.TryParse(dict["buf_kb"].ToString(), out bk) && bk > 0) target.BufKb = bk;
                }
                if (dict.ContainsKey("verbose") && dict["verbose"] != null) {
                    bool v;
                    if (bool.TryParse(dict["verbose"].ToString(), out v)) target.Verbose = v;
                }
                if (dict.ContainsKey("language") && dict["language"] != null) {
                    target.Language = dict["language"].ToString().Trim();
                }
                if (dict.ContainsKey("autostart") && dict["autostart"] != null) {
                    bool asVal;
                    if (bool.TryParse(dict["autostart"].ToString(), out asVal)) target.AutoStart = asVal;
                }
                if (dict.ContainsKey("cfproxy_worker_domain") && dict["cfproxy_worker_domain"] != null) {
                    var list = dict["cfproxy_worker_domain"] as System.Collections.ArrayList;
                    if (list != null && list.Count > 0) {
                        target.CfProxyWorkerDomain = list[0].ToString().Trim();
                    }
                }

                Save(target);
                return true;
            } catch {
                return false;
            }
        }

        public static ProxySettings Load() {
            var settings = new ProxySettings();
            try {
                string path = ConfigFilePath;
                if (!File.Exists(path) && File.Exists("config.json")) {
                    path = "config.json";
                }

                if (File.Exists(path)) {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<ProxySettings>(json);
                    if (loaded != null) {
                        if (loaded.Port > 0 && loaded.Port < 65536) settings.Port = loaded.Port;
                        if (!string.IsNullOrEmpty(loaded.Host)) settings.Host = loaded.Host;
                        if (!string.IsNullOrEmpty(loaded.DcIp)) settings.DcIp = loaded.DcIp;
                        if (!string.IsNullOrEmpty(loaded.Secret)) settings.Secret = loaded.Secret;
                        settings.AutoStart = loaded.AutoStart;
                        settings.AutoStartProxy = loaded.AutoStartProxy;
                        settings.CfProxy = loaded.CfProxy;
                        settings.H2 = loaded.H2;
                        if (loaded.PoolSize > 0) settings.PoolSize = loaded.PoolSize;
                        if (loaded.BufKb > 0) settings.BufKb = loaded.BufKb;
                        if (!string.IsNullOrEmpty(loaded.Language)) settings.Language = loaded.Language;
                        settings.Verbose = loaded.Verbose;
                        if (!string.IsNullOrEmpty(loaded.CfProxyWorkerDomain)) settings.CfProxyWorkerDomain = loaded.CfProxyWorkerDomain;
                    }
                } else if (FlowsealConfigExists) {
                    // Automatically import from Flowseal on first launch!
                    ImportFromFlowseal(settings);
                }
            } catch {
                settings.SetDefaults();
            }

            // Sync with actual registry status
            settings.AutoStart = IsAutoStartEnabled();
            return settings;
        }

        public static void Save(ProxySettings settings) {
            try {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(settings);
                File.WriteAllText(ConfigFilePath, json, Encoding.UTF8);
                try {
                    File.WriteAllText("config.json", json, Encoding.UTF8);
                } catch { }

                SetAutoStart(settings.AutoStart);
            } catch { }
        }

        public static void ResetToDefaults(ProxySettings settings) {
            settings.SetDefaults();
            Save(settings);
        }

        public static string GenerateRandomSecret() {
            byte[] bytes = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) {
                rng.GetBytes(bytes);
            }
            var sb = new StringBuilder(32);
            for (int i = 0; i < bytes.Length; i++) {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        public static bool IsAutoStartEnabled() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false)) {
                    if (key != null) {
                        object val = key.GetValue(AppRegistryKeyName);
                        return val != null;
                    }
                }
            } catch { }
            return false;
        }

        public static void SetAutoStart(bool enable) {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true)) {
                    if (key != null) {
                        if (enable) {
                            string exePath = Assembly.GetExecutingAssembly().Location;
                            key.SetValue(AppRegistryKeyName, "\"" + exePath + "\" --minimized");
                        } else {
                            key.DeleteValue(AppRegistryKeyName, false);
                        }
                    }
                }
            } catch { }
        }
    }
}
