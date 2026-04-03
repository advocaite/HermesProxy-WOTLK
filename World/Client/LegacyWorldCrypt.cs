using Framework.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HermesProxy.World.Client
{
    public interface LegacyWorldCrypt
    {
        public void Initialize(byte[] sessionKey);
        public void Decrypt(byte[] data, int len);
        public void Encrypt(byte[] data, int len);

    }
    public class VanillaWorldCrypt : LegacyWorldCrypt
    {
        public const uint CRYPTED_SEND_LEN = 6;
        public const uint CRYPTED_RECV_LEN = 4;

        public void Initialize(byte[] sessionKey)
        {
            SetKey(sessionKey);
            m_send_i = m_send_j = m_recv_i = m_recv_j = 0;
            m_isInitialized = true;
        }

        public void Decrypt(byte[] data, int len)
        {
            if (len < CRYPTED_RECV_LEN)
                return;

            for (byte t = 0; t < CRYPTED_RECV_LEN; t++)
            {
                m_recv_i %= (byte)m_key.Count();
                byte x = (byte)((data[t] - m_recv_j) ^ m_key[m_recv_i]);
                ++m_recv_i;
                m_recv_j = data[t];
                data[t] = x;
            }
        }

        public void Encrypt(byte[] data, int len)
        {
            if (!m_isInitialized)
                return;

            if (len < CRYPTED_SEND_LEN)
                return;

            for (byte t = 0; t < CRYPTED_SEND_LEN; t++)
            {
                m_send_i %= (byte)m_key.Count();
                byte x = (byte)((data[t] ^ m_key[m_send_i]) + m_send_j);
                ++m_send_i;
                data[t] = m_send_j = x;
            }
        }

        public void SetKey(byte[] key)
        {
            System.Diagnostics.Trace.Assert(key.Length != 0);

            m_key = key.ToArray();
        }

        byte[] m_key;
        byte m_send_i, m_send_j, m_recv_i, m_recv_j;
        bool m_isInitialized;
    }

    public class WotlkWorldCrypt : LegacyWorldCrypt
    {
        public const uint CRYPTED_SEND_LEN = 6;
        public const uint CRYPTED_RECV_LEN = 4;

        public void Initialize(byte[] sessionKey)
        {
            // WotLK 3.3.5a uses HMAC-SHA1 derived keys for RC4
            byte[] encSeed = new byte[16] { 0xC2, 0xB3, 0x72, 0x3C, 0xC6, 0xAE, 0xD9, 0xB5, 0x34, 0x3C, 0x53, 0xEE, 0x2F, 0x43, 0x67, 0xCE };
            byte[] decSeed = new byte[16] { 0xCC, 0x98, 0xAE, 0x04, 0xE8, 0x97, 0xEA, 0xCA, 0x12, 0xDD, 0xC0, 0x93, 0x42, 0x91, 0x53, 0x57 };

            HmacHash encHash = new HmacHash(encSeed);
            encHash.Finish(sessionKey, sessionKey.Length);
            m_sendKey = encHash.Digest.ToArray();

            HmacHash decHash = new HmacHash(decSeed);
            decHash.Finish(sessionKey, sessionKey.Length);
            m_recvKey = decHash.Digest.ToArray();

            // Initialize RC4 state and drop first 1024 bytes
            m_sendState = InitRC4(m_sendKey);
            m_recvState = InitRC4(m_recvKey);

            m_isInitialized = true;
        }

        private byte[] InitRC4(byte[] key)
        {
            byte[] s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 255;
                (s[i], s[j]) = (s[j], s[i]);
            }
            // Drop first 1024 bytes
            byte[] state = new byte[258]; // [0..255] = S, [256] = i, [257] = j
            Buffer.BlockCopy(s, 0, state, 0, 256);
            state[256] = 0; state[257] = 0;
            byte[] drop = new byte[1024];
            RC4Process(state, drop, 1024);
            return state;
        }

        private static void RC4Process(byte[] state, byte[] data, int len)
        {
            int x = state[256];
            int y = state[257];
            for (int k = 0; k < len; k++)
            {
                x = (x + 1) & 255;
                y = (y + state[x]) & 255;
                (state[x], state[y]) = (state[y], state[x]);
                data[k] ^= state[(state[x] + state[y]) & 255];
            }
            state[256] = (byte)x;
            state[257] = (byte)y;
        }

        public void Decrypt(byte[] data, int len)
        {
            if (!m_isInitialized || len < CRYPTED_RECV_LEN)
                return;
            RC4Process(m_recvState, data, (int)CRYPTED_RECV_LEN);
        }

        public void Encrypt(byte[] data, int len)
        {
            if (!m_isInitialized || len < CRYPTED_SEND_LEN)
                return;
            RC4Process(m_sendState, data, (int)CRYPTED_SEND_LEN);
        }

        byte[] m_sendKey;
        byte[] m_recvKey;
        byte[] m_sendState;
        byte[] m_recvState;
        bool m_isInitialized;
    }

    public class TbcWorldCrypt : LegacyWorldCrypt
    {
        public const uint CRYPTED_SEND_LEN = 6;
        public const uint CRYPTED_RECV_LEN = 4;

        public void Initialize(byte[] sessionKey)
        {
            byte[] recvSeed = new byte[16] { 0x38, 0xA7, 0x83, 0x15, 0xF8, 0x92, 0x25, 0x30, 0x71, 0x98, 0x67, 0xB1, 0x8C, 0x4, 0xE2, 0xAA };
            HmacHash recvHash = new HmacHash(recvSeed);
            recvHash.Finish(sessionKey, sessionKey.Count());
            m_key = recvHash.Digest.ToArray();

            m_send_i = m_send_j = m_recv_i = m_recv_j = 0;
            m_isInitialized = true;
        }

        public void Decrypt(byte[] data, int len)
        {
            if (len < CRYPTED_RECV_LEN)
                return;

            for (byte t = 0; t < CRYPTED_RECV_LEN; t++)
            {
                m_recv_i %= (byte)m_key.Count();
                byte x = (byte)((data[t] - m_recv_j) ^ m_key[m_recv_i]);
                ++m_recv_i;
                m_recv_j = data[t];
                data[t] = x;
            }
        }

        public void Encrypt(byte[] data, int len)
        {
            if (!m_isInitialized)
                return;

            if (len < CRYPTED_SEND_LEN)
                return;

            for (byte t = 0; t < CRYPTED_SEND_LEN; t++)
            {
                m_send_i %= (byte)m_key.Count();
                byte x = (byte)((data[t] ^ m_key[m_send_i]) + m_send_j);
                ++m_send_i;
                data[t] = m_send_j = x;
            }
        }

        byte[] m_key;
        byte m_send_i, m_send_j, m_recv_i, m_recv_j;
        bool m_isInitialized;
    }
}
