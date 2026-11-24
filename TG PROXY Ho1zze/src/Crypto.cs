using System;
using System.Security.Cryptography;

namespace TgProxyHoizze {
    public class AesCtr {
        private readonly ICryptoTransform _encryptor;
        private readonly byte[] _counter = new byte[16];
        private const int BatchBlocks = 256;
        private const int BatchBytes = BatchBlocks * 16; // 4096 bytes batch
        private readonly byte[] _counterBatch = new byte[BatchBytes];
        private readonly byte[] _keystream = new byte[BatchBytes];
        private int _keystreamPos = BatchBytes;

        public AesCtr(byte[] key, byte[] iv) {
            var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            _encryptor = aes.CreateEncryptor(key, new byte[16]);
            Array.Copy(iv, _counter, 16);
        }

        public void Process(byte[] data, int offset, int count) {
            int cur = 0;
            // 1. Drain available buffered keystream
            while (_keystreamPos < BatchBytes && cur < count) {
                data[offset + cur++] ^= _keystream[_keystreamPos++];
            }

            // 2. Process large 4KB batches directly with single native transform
            while (count - cur >= BatchBytes) {
                FillKeystream();
                FastXor(data, offset + cur, _keystream, 0, BatchBytes);
                cur += BatchBytes;
                _keystreamPos = BatchBytes;
            }

            // 3. Process remaining tail
            if (cur < count) {
                FillKeystream();
                _keystreamPos = 0;
                while (cur < count) {
                    data[offset + cur++] ^= _keystream[_keystreamPos++];
                }
            }
        }

        private void FillKeystream() {
            for (int b = 0; b < BatchBlocks; b++) {
                Buffer.BlockCopy(_counter, 0, _counterBatch, b * 16, 16);
                IncrementCounter();
            }
            _encryptor.TransformBlock(_counterBatch, 0, BatchBytes, _keystream, 0);
        }

        private static void FastXor(byte[] dest, int destOffset, byte[] src, int srcOffset, int count) {
            int i = 0;
            int limit = count - 7;
            while (i < limit) {
                int d = destOffset + i;
                int s = srcOffset + i;
                dest[d] ^= src[s];
                dest[d + 1] ^= src[s + 1];
                dest[d + 2] ^= src[s + 2];
                dest[d + 3] ^= src[s + 3];
                dest[d + 4] ^= src[s + 4];
                dest[d + 5] ^= src[s + 5];
                dest[d + 6] ^= src[s + 6];
                dest[d + 7] ^= src[s + 7];
                i += 8;
            }
            while (i < count) {
                dest[destOffset + i] ^= src[srcOffset + i];
                i++;
            }
        }

        private void IncrementCounter() {
            for (int i = 15; i >= 0; i--) {
                if (++_counter[i] != 0) break;
            }
        }
    }

    public class HandshakeResult {
        public uint ProtocolTag { get; set; }
        public int DcId { get; set; }
        public short RawDc { get; set; }
        public AesCtr Decryptor { get; set; }
        public AesCtr Encryptor { get; set; }
        public bool IsIntermediate {
            get { return ProtocolTag == 0xeeeeeeee; }
        }
        public bool IsAbridged {
            get { return ProtocolTag == 0xefefefef; }
        }
        public bool IsPadded {
            get { return ProtocolTag == 0xdddddddd; }
        }
    }

    public class RelayHandshake {
        public byte[] RelayInitBytes { get; private set; }
        public AesCtr UpstreamEncryptor { get; private set; }
        public AesCtr UpstreamDecryptor { get; private set; }

        public static RelayHandshake Create(short dcRaw, uint protoTag) {
            var rng = new Random();
            byte[] b = new byte[64];

            while (true) {
                rng.NextBytes(b);
                if (b[0] == 0xef) continue;
                uint val = BitConverter.ToUInt32(b, 0);
                if (val == 0x44414548 || val == 0x54534f50 || val == 0x20544547 || val == 0x4954504f || val == 0xeeeeeeee || val == 0xdddddddd) continue;
                uint val2 = BitConverter.ToUInt32(b, 4);
                if (val2 == 0) continue;
                break;
            }

            byte[] encKey = new byte[32];
            Array.Copy(b, 8, encKey, 0, 32);
            byte[] encIv = new byte[16];
            Array.Copy(b, 40, encIv, 0, 16);

            byte[] bReversed = CryptoUtils.Reverse(b);
            byte[] decKey = new byte[32];
            Array.Copy(bReversed, 8, decKey, 0, 32);
            byte[] decIv = new byte[16];
            Array.Copy(bReversed, 40, decIv, 0, 16);

            // Put protocol tag and DC ID
            byte[] protoBytes = BitConverter.GetBytes(protoTag);
            Array.Copy(protoBytes, 0, b, 56, 4);

            byte[] dcBytes = BitConverter.GetBytes(dcRaw);
            Array.Copy(dcBytes, 0, b, 60, 2);

            // Encrypt bytes 56..64 with encKey / encIv
            var encCtr = new AesCtr(encKey, encIv);
            byte[] tempEnc = new byte[64];
            Array.Copy(b, 0, tempEnc, 0, 64);
            encCtr.Process(tempEnc, 0, 64);

            // Substitute bytes 56..64 with encrypted values
            Array.Copy(tempEnc, 56, b, 56, 8);

            // Telegram DC decryptor: starts at offset 0 (Server does NOT send 64-byte handshake back!)
            var decCtr = new AesCtr(decKey, decIv);

            return new RelayHandshake {
                RelayInitBytes = b,
                UpstreamEncryptor = encCtr,
                UpstreamDecryptor = decCtr
            };
        }
    }

    public static class CryptoUtils {
        public static byte[] HexToBytes(string hex) {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            hex = hex.Trim().ToLowerInvariant();
            if (hex.Length % 2 != 0) hex = "0" + hex;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        public static byte[] Reverse(byte[] b) {
            if (b == null) return null;
            byte[] rev = new byte[b.Length];
            for (int i = 0; i < b.Length; i++) {
                rev[i] = b[b.Length - 1 - i];
            }
            return rev;
        }

        public static byte[] Reverse(byte[] b, int offset, int count) {
            byte[] rev = new byte[count];
            for (int i = 0; i < count; i++) {
                rev[i] = b[offset + count - 1 - i];
            }
            return rev;
        }

        public static byte[] Sha256(byte[] data1, byte[] data2) {
            using (var sha = SHA256.Create()) {
                byte[] combined = new byte[data1.Length + data2.Length];
                Buffer.BlockCopy(data1, 0, combined, 0, data1.Length);
                Buffer.BlockCopy(data2, 0, combined, data1.Length, data2.Length);
                return sha.ComputeHash(combined);
            }
        }

        public static HandshakeResult TryParseHandshake(byte[] handshake64, string secretHex) {
            if (handshake64 == null || handshake64.Length < 64) return null;

            byte[] secretBytes = HexToBytes(secretHex);
            if (secretBytes.Length == 17 && (secretBytes[0] == 0xee || secretBytes[0] == 0xdd)) {
                byte[] s16 = new byte[16];
                Array.Copy(secretBytes, 1, s16, 0, 16);
                secretBytes = s16;
            }

            // Client decryption key & IV (for traffic from client to proxy)
            byte[] decPrekey = new byte[32];
            Array.Copy(handshake64, 8, decPrekey, 0, 32);
            byte[] decIv = new byte[16];
            Array.Copy(handshake64, 40, decIv, 0, 16);
            byte[] decKey = Sha256(decPrekey, secretBytes);

            // Client encryption key & IV (for traffic from proxy to client)
            // According to MTProto Obfuscated2 specification: reversed header provides secondary keys
            byte[] reversed64 = Reverse(handshake64);
            byte[] encPrekey = new byte[32];
            Array.Copy(reversed64, 8, encPrekey, 0, 32);
            byte[] encIv = new byte[16];
            Array.Copy(reversed64, 40, encIv, 0, 16);
            byte[] encKey = Sha256(encPrekey, secretBytes);

            var decCtr = new AesCtr(decKey, decIv);
            byte[] decrypted64 = new byte[64];
            Array.Copy(handshake64, decrypted64, 64);
            decCtr.Process(decrypted64, 0, 64);

            uint tag = BitConverter.ToUInt32(decrypted64, 56);
            short dcRaw = BitConverter.ToInt16(decrypted64, 60);

            // Check if valid MTProto protocol tag (intermediate 0xeeeeeeee, padded 0xdddddddd, abridged 0xefefefef)
            if (tag == 0xeeeeeeee || tag == 0xefefefef || tag == 0xdddddddd) {
                int dcId = Math.Abs((int)dcRaw);
                if (dcId == 0) dcId = 4;
                return new HandshakeResult {
                    ProtocolTag = tag,
                    DcId = dcId,
                    RawDc = dcRaw,
                    Decryptor = decCtr, // Already advanced 64 bytes
                    Encryptor = new AesCtr(encKey, encIv) // Starts at offset 0
                };
            }

            return null;
        }
    }
}
