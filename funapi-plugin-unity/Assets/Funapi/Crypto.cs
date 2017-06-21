// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;


public class Sodium
{
    static RNGCryptoServiceProvider random = new RNGCryptoServiceProvider();

    // 라이브러리 초기화를 위해 프로그램 시작 시점에 한 번 호출해줘야 한다.
    public static void Init ()
    {
        sodium_init();
    }

    public static byte[] RandomBytes (uint size)
    {
        byte[] buf = new byte[size];
        random.GetBytes(buf);
        return buf;
    }

    public static void Increment (byte[] n)
    {
        sodium_increment(n, (UIntPtr)n.Length);
    }

    public static byte[] ScalarMultBase (byte[] n)
    {
        byte[] q = new byte[32];
        crypto_scalarmult_base(q, n);
        return q;
    }

    public static byte[] ScalarMult (byte[] n, byte[] p)
    {
        byte[] q = new byte[32];
        crypto_scalarmult(q, n, p);
        return q;
    }

    // inplace xor
    public static bool StreamChacha20XorIc (byte[] m, byte[] nonce, byte[] key, ulong ic)
    {
        return 0 == crypto_stream_chacha20_xor_ic(m, m, (ulong)m.Length, nonce, ic, key);
    }

    public static bool StreamChacha20XorIc (ArraySegment<byte> c, ArraySegment<byte> m,
                                            byte[] nonce, byte[] key, ulong ic)
    {
        if (c.Count > 0 && c.Count == m.Count && nonce.Length == 8 && key.Length == 32) {
            return 0 == crypto_stream_chacha20_xor_ic_offset(
                    c.Array, (ulong)c.Offset, m.Array, (ulong)m.Offset, (ulong)m.Count, nonce, ic, key);
        } else {
            return false;
        }
    }

    public static byte[] StreamChacha20XorIcBuf (byte[] m, byte[] nonce, byte[] key, ulong ic)
    {
        byte[] c = new byte[m.Length];
        if (0 == crypto_stream_chacha20_xor_ic(c, m, (ulong)m.Length, nonce, ic, key))
            return c;
        else
            return null;
    }

    public static byte[] BuildAes128Table (byte [] key)
    {
        byte[] tbl = new byte[1408];
        crypto_stream_aes128ctr_beforenm(tbl, key);
        return tbl;
    }

    // table 기반으로 xor (inplace)
    public static bool StreamAes128XorTable (byte [] m, byte[] nonce, byte[] table)
    {
        return 0 == crypto_stream_aes128ctr_xor_afternm(m, m, (ulong)m.Length, nonce, table);
    }

    public static bool StreamAes128XorTable (ArraySegment<byte> c,ArraySegment<byte> m,
                                             byte[] nonce, byte[] table)
    {
        return 0 == crypto_stream_aes128ctr_xor_afternm_offset(
            c.Array, (ulong)c.Offset, m.Array, (ulong)m.Offset, (ulong)m.Count, nonce, table);
    }

    public static byte[] StreamAes128XorTableBuf (byte []m, byte[] nonce, byte[] table)
    {
        byte[] c = new byte[m.Length];
        if (0 == crypto_stream_aes128ctr_xor_afternm(c, m, (ulong)m.Length, nonce, table))
            return c;
        else
            return null;
    }

    public static bool StreamAes128Xor (byte[] m, byte[] nonce, byte[] key)
    {
        return 0 == crypto_stream_aes128ctr_xor(m, m, (ulong)m.Length, nonce, key);
    }

    public static byte[] StreamAes128XorBuf (byte[] m, byte[] nonce, byte[] key)
    {
        byte[] c = new byte[m.Length];
        if (0 == crypto_stream_aes128ctr_xor(c, m, (ulong)m.Length, nonce, key))
            return c;
        else
            return null;
    }

    public static byte[] GenerateSharedSecret (byte[] server_pub_key, out byte[] client_pub_key)
    {
        byte[] client_sec_key = RandomBytes(32);
        client_pub_key = ScalarMultBase(client_sec_key);

        byte [] q = ScalarMult(client_sec_key, server_pub_key);

        var h = new SHA512Managed();
        h.TransformBlock(q, 0, q.Length, q, 0);
        h.TransformBlock(client_pub_key, 0, client_pub_key.Length, client_pub_key, 0);
        h.TransformBlock(server_pub_key, 0, server_pub_key.Length, server_pub_key, 0);
        h.TransformFinalBlock(q, 0, 0);
        return h.Hash;
    }

    public static bool GenerateAes128Secrets (byte[] server_pub_key,
                                              out byte[] client_pub_key,
                                              out byte[] enc_table,
                                              out byte[] enc_nonce,
                                              out byte[] dec_nonce)
    {
        byte[] secret = GenerateSharedSecret(server_pub_key, out client_pub_key);
        byte[] enc_key = new byte[16];
        enc_nonce = new byte[16];
        dec_nonce = new byte[16];

        Array.Copy(secret, 0, enc_key, 0, 16);
        Array.Copy(secret, 16, dec_nonce, 0, 16);
        Array.Copy(secret, 32, enc_nonce, 0, 16);

        enc_table = BuildAes128Table(enc_key);
        return true;
    }

    public static bool GenerateChacha20Secrets (byte[] server_pub_key,
                                                out byte[] client_pub_key,
                                                out byte[] enc_key,
                                                out byte[] enc_nonce,
                                                out byte[] dec_nonce)
    {
        byte[] secret = GenerateSharedSecret(server_pub_key, out client_pub_key);
        enc_key = new byte[32];
        enc_nonce = new byte[8];
        dec_nonce = new byte[8];

        Array.Copy(secret, 0, enc_key, 0, 32);
        Array.Copy(secret, 32, dec_nonce, 0, 8);
        Array.Copy(secret, 40, enc_nonce, 0, 8);
        return true;
    }

#region libsodium wrappers
#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int sodium_init();

    // NOTE(jinuk): size_t 는 ARM (32), iOS (32/64) 에서 크기가 다르다
    // void sodium_increment (unsigned char *n, const size_t nlen);
#if UNITY_IOS
    [DllImport("__Internal")]
    private static extern void sodium_increment (byte[] n, UIntPtr nlen);
#else
#if (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern void sodium_increment (byte[] n, UIntPtr nlen);
#endif

#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_scalarmult_base (byte[] q, byte[] n);

#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_scalarmult (byte[] q, byte[] n, byte[] p);

    //int crypto_stream_chacha20_xor_ic (unsigned char *c, const unsigned char *m,
    //                                   unsigned long long mlen,
    //                                   const unsigned char *n, uint64_t ic,
    //                                   const unsigned char *k);
#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_stream_chacha20_xor_ic (
        byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_stream_chacha20_xor_ic_offset (
        byte[] c, ulong c_offset, byte[] m, ulong m_offset, ulong mlen, byte[] n, ulong ic, byte[] k);

    //int crypto_stream_aes128ctr_xor (unsigned char *out, const unsigned char *in,
    //                                 unsigned long long inlen, const unsigned char *n,
    //                                 const unsigned char *k);
#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_stream_aes128ctr_xor (
        byte[] c, byte[] m, ulong inlen, byte[] n, byte[] k);

    //int crypto_stream_aes128ctr_beforenm (unsigned char *c, const unsigned char *k);
#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_stream_aes128ctr_beforenm (byte[] tbl, byte[] k);

    //int crypto_stream_aes128ctr_xor_afternm (unsigned char *out, const unsigned char *in,
    //                                         unsigned long long len,
    //                                         const unsigned char *nonce,
    //                                         const unsigned char *c);
#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_stream_aes128ctr_xor_afternm (
        byte[] c, byte[] m, ulong len, byte[] nonce, byte[] tbl);

    //int crypto_stream_aes128ctr_xor_afternm_offset (unsigned char *out, unsigned long long out_offset,
    //                                                const unsigned char *in, unsigned long long in_offset,
    //                                                unsigned long long len,
    //                                                const unsigned char *nonce,
    //                                                const unsigned char *c);
#if UNITY_IOS
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("sodium64")]
#else
    [DllImport("sodium", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern int crypto_stream_aes128ctr_xor_afternm_offset (
        byte[] c, ulong c_offset, byte[] m, ulong m_offset, ulong len, byte[] nonce, byte[] tbl);
#endregion

    public static string Hexify (byte[] b)
    {
        return BitConverter.ToString(b).Replace("-", "").ToLower();
    }

    public static string Hexify (byte[] b, int offset, int length)
    {
        return BitConverter.ToString(b, offset, length).Replace("-", "").ToLower();
    }

    public static byte[] Unhexify (string s)
    {
        var chars = s.ToCharArray();
        int len = s.Length / 2;
        var bytes = new byte[len];

        for (int i = 0; i < len; ++i)
        {
            var chunk = new string(chars, i * 2, 2);
            bytes[i] = byte.Parse(chunk, NumberStyles.AllowHexSpecifier);
        }

        return bytes;
    }

    public static void PerformanceTest()
    {
        Init();

        const int N = 100000;  // 알고리즘 반복 시행 횟수
        byte[] msg = RandomBytes(64);  // 여기가 메시지 길이
        byte[] k16 = RandomBytes(16);
        byte[] k32 = RandomBytes(32);
        byte[] n16 = RandomBytes(16);
        byte[] n8 = RandomBytes(16);
        byte[] tbl = BuildAes128Table(k16);

        FunDebug.Log("Performance test; msg-size={0}, N={1}", msg.Length, N);

        bool x = true;
        long start = DateTime.Now.Ticks / 10000;
        for (int i = 0; i < N; ++i)
        {
            x = x && StreamAes128Xor(msg, n16, k16);
            Increment(n16);
        }
        long end = DateTime.Now.Ticks / 10000;
        FunDebug.Log("AES 128: {0} ms", end - start);

        x = true;
        start = DateTime.Now.Ticks / 10000;
        for (int i = 0; i < N; ++i)
        {
            x = x && StreamAes128XorTable(msg, n16, tbl);
            Increment(n16);
        }
        end = DateTime.Now.Ticks / 10000;
        FunDebug.Log("AES 128 (table): {0} ms", end - start);

        x = true;
        ulong ic = 0;
        start = DateTime.Now.Ticks / 10000;
        for (int i = 0; i < N; ++i)
        {
            x = x && StreamChacha20XorIc(msg, n8, k32, ic);
            ic += (uint)msg.Length;
        }
        end = DateTime.Now.Ticks / 10000;
        FunDebug.Log("ChaCha20: {0} ms", end - start);
    }
}
