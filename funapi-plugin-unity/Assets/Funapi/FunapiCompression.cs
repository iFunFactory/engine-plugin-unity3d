// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;


namespace Fun
{
public enum FunCompressionType
{
    kNone,
    kZstd,      // Zstandard
    kDeflate    // DEFLATE
}


public abstract class FunapiCompressor
{
    public FunapiCompressor ()
    {
        compression_threshold = 128;
    }

    public abstract ArraySegment<byte> Compress (ArraySegment<byte> src);
    public abstract ArraySegment<byte> Decompress (ArraySegment<byte> src, int expected_size);

    public int compression_threshold { get; set; }
}


public class FunapiZstdCompressor : FunapiCompressor
{
    public void Create (byte[] dict_buf)
    {
        if (dict_buf != null && dict_buf.Length > 0)
        {
            cdict_ = FunapiCompression.LoadCompressionDictionary(dict_buf, 1);
            ddict_ = FunapiCompression.LoadDecompressionDictionary(dict_buf);
        }
    }

    public void Destroy ()
    {
        if (cdict_ != (UIntPtr)0)
        {
            FunapiCompression.UnloadCompressionDictionary(cdict_);
            cdict_ = (UIntPtr)0;
        }

        if (ddict_ != (UIntPtr)0)
        {
            FunapiCompression.UnloadDecompressionDictionary(ddict_);
            ddict_ = (UIntPtr)0;
        }
    }

    public override ArraySegment<byte> Compress (ArraySegment<byte> src)
    {
        return FunapiCompression.CompressZstd(src, cdict_);
    }

    public override ArraySegment<byte> Decompress (ArraySegment<byte> src, int expected_size)
    {
        return FunapiCompression.DecompressZstd(src, expected_size, ddict_);
    }


    UIntPtr cdict_ = (UIntPtr)0;
    UIntPtr ddict_ = (UIntPtr)0;
}


public class FunapiDeflateCompressor : FunapiCompressor
{
    public override ArraySegment<byte> Compress (ArraySegment<byte> src)
    {
        return FunapiCompression.CompressDeflate(src);
    }

    public override ArraySegment<byte> Decompress (ArraySegment<byte> src, int expected_size)
    {
        return FunapiCompression.DecompressDeflate(src, expected_size);
    }
}


public class FunapiCompression
{
#region native wrappers

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_compressBound(UIntPtr size);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_compress(byte[] dst, UIntPtr dst_size,
            byte[] src, UIntPtr src_size, int compressionLevel);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_decompress(byte[] dst, UIntPtr dst_size,
            byte[] src, UIntPtr src_size);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr CompressForFunapi(byte[] dst, UIntPtr dst_size, IntPtr dst_offset,
            byte[] src, UIntPtr src_size, IntPtr src_offset, UIntPtr cdict);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr DecompressForFunapi(byte[] dst, UIntPtr dst_size, IntPtr dst_offset,
            byte[] src, UIntPtr src_size, IntPtr src_offset, UIntPtr ddict);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_createCDict(byte[] dict_buf, UIntPtr dict_size, int compressionLevel);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_createDDict(byte[] dict_buf, UIntPtr dict_size);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_freeCDict(UIntPtr dict);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("zstd64")]
#else
    [DllImport("zstd", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZSTD_freeDDict(UIntPtr dict);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("z64", CallingConvention = CallingConvention.Cdecl)]
#else
    [DllImport("z", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZlibCompressForFunapi(byte [] dst, UIntPtr dst_offset, UIntPtr dst_len,
        byte[] src, UIntPtr src_offset, UIntPtr src_len);

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
#elif (UNITY_64 || UNITY_EDITOR_64) && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [DllImport("z64", CallingConvention = CallingConvention.Cdecl)]
#else
    [DllImport("z", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern UIntPtr ZlibDecompressForFunapi(byte [] dst, UIntPtr dst_offset, UIntPtr dst_len,
        byte[] src, UIntPtr src_offset, UIntPtr src_len);

    internal static UIntPtr LoadCompressionDictionary(byte[] buf, int compressionLevel) {
        return ZSTD_createCDict(buf, (UIntPtr)buf.Length, compressionLevel);
    }

    internal static UIntPtr LoadDecompressionDictionary(byte[] buf) {
        return ZSTD_createDDict(buf, (UIntPtr)buf.Length);
    }

    internal static void UnloadCompressionDictionary(UIntPtr dict) {
        ZSTD_freeCDict(dict);
    }

    internal static void UnloadDecompressionDictionary(UIntPtr dict) {
        ZSTD_freeDDict(dict);
    }

    internal static ArraySegment<byte> CompressZstd(ArraySegment<byte> buffer, UIntPtr dict) {
        byte[] dst_buf = new byte[(int)ZSTD_compressBound((UIntPtr)buffer.Count)];
        int count = (int)CompressForFunapi(dst_buf, (UIntPtr)dst_buf.Length, (IntPtr)0,
                buffer.Array, (UIntPtr)buffer.Count, (IntPtr)buffer.Offset, dict);
        return new ArraySegment<byte>(dst_buf, 0, count);
    }

    internal static ArraySegment<byte> DecompressZstd(
            ArraySegment<byte> buffer, int expected_size, UIntPtr dict) {
        byte[] dst_buf = new byte[expected_size];
        int count = (int)DecompressForFunapi(dst_buf, (UIntPtr)expected_size, (IntPtr)0,
                buffer.Array, (UIntPtr)buffer.Count, (IntPtr)buffer.Offset, dict);
        return new ArraySegment<byte>(dst_buf, 0, count);
    }

    internal static ArraySegment<byte> CompressDeflate(ArraySegment<byte> src) {
        int dst_length = 18 + src.Count + 5 * (src.Count / 16383 + 1);
        byte[] dst = new byte[dst_length];
        int count = (int)ZlibCompressForFunapi(dst, (UIntPtr)0, (UIntPtr)dst_length,
                src.Array, (UIntPtr)src.Offset, (UIntPtr)src.Count);
        return new ArraySegment<byte>(dst, 0, count);
    }

    internal static ArraySegment<byte> DecompressDeflate(ArraySegment<byte> src, int expected_size) {
        byte[] dst = new byte[expected_size];
        int count = (int)ZlibDecompressForFunapi(dst, (UIntPtr)0, (UIntPtr)expected_size,
                src.Array, (UIntPtr)src.Offset, (UIntPtr)src.Count);
        return new ArraySegment<byte>(dst, 0, count);
    }

#endregion

    public static void PerformanceTest()
    {
        string dict_str = ("N6Qw7OaV4zEcENhIgAAAAAAA2pAu8cEmbIanlFJKKU0jSZMxINMBAABCIJRW"
        + "+QIAAARAzIeVRcZN0YNRQRiFKgAAAIAAAAAAAAAAAAAAAAAAACRs5KZSRDquS4oAAAAAAAAAAAAAAA"
        + "EAAAAEAAAACAAAADksIl9tc2d0NTI1ODc4OSwiX21zZ196IjotOTAuOTAwMDAxLTIuNSwibG9va196"
        + "IjotOTBvb2tfeCI6LTIuNSwibDAzODE0Njk3MjcsImxvb2tfIjotOS4xMDAwMDAzODE0Njk5MDksIm"
        + "Rpcl96IjotOS4xMDAwMDE1MjU4Nzg5MDksImRpX3giOi0zMy45MDAwMDE1MjUyNDIxOSwiZGlyX3gi"
        + "Oi0zMy4xOTk5OTY5NDgyNDIxOSwicG9zX3oiOi03Ny4xOTk5OTYwOTI2NTEzNywicG9zX3oxMS4xOT"
        + "k5OTk4MDkyNjUxM29zX3giOi0xMS4xOTk5ImlkIjo0NDI4OCwicG9zX3hzdF9tb3ZlIn17ImlkIjo0"
        + "NHBlIjoicmVxdWVzdF9tb3ZlNDgsIl9tc2d0eXBlIjoicmUyMzcwNjA1NDgsIl9tc3oiOi0xNi43OT"
        + "k5OTkyMzEuNSwibG9va196IjotMTYuImxvb2tfeCI6NjEuNSwibG9feiI6LTMwLjUsImxvb2tfeC0z"
        + "OS41LCJkaXJfeiI6LTMwNSwiZGlyX3giOi0zOS41LCJwb3NfeiI6NTEuNSwiZGlyXzIzNzA2MDU1LC"
        + "Jwb3NfeiI6LTU0LjI5OTk5OTIzNzA2MDU0LCJwb3NfeCI6LTU0LjI5OXsiaWQiOjE0NDg0LCJwb3Nf");
        byte[] dict_buf = Convert.FromBase64String(dict_str);

        UIntPtr cdict = ZSTD_createCDict(dict_buf, (UIntPtr)dict_buf.Length, 1);
        UIntPtr ddict = ZSTD_createDDict(dict_buf, (UIntPtr)dict_buf.Length);

        string src = "{\"id\":12032,\"pos_x\":31.01,\"pos_z\":45.5293984741," +
                     "\"dir_x\":-14.199799809265137,\"dir_z\":11.899918530274," +
                     "\"look_x\":1.100000381469727,\"look_z\":11.600100381469727," +
                     "\"_msgtype\":\"request_move\"}";
        var src_buf = Encoding.UTF8.GetBytes(src);
        var dst_buf = new byte[(int)ZSTD_compressBound((UIntPtr)src_buf.Length)];

        ulong size, size2;
        long start, end;
        const int N = 100000;  // 알고리즘 반복 시행 횟수
        size = 0;
        size2 = 0;
        start = DateTime.Now.Ticks / 10000;
        for (int i = 0; i < N; ++i) {
            size += (ulong)CompressForFunapi(dst_buf, (UIntPtr)dst_buf.Length, (IntPtr)0,
                                      src_buf, (UIntPtr)src_buf.Length, (IntPtr)0, (UIntPtr)0);
            size2 += (ulong)DecompressForFunapi(src_buf, (UIntPtr)src_buf.Length, (IntPtr)0,
                    dst_buf, (UIntPtr)dst_buf.Length, (IntPtr)0, (UIntPtr)0);
        }
        end = DateTime.Now.Ticks / 10000;
        FunDebug.Log("Zstd (w/o dict) {0} ms, {1} ms/(enc+dec)", end - start, (end - start) * 1.0 / N);
        FunDebug.Log("String length={0}, compressed={1}", src_buf.Length, size / N);

        size = 0;
        size2 = 0;
        start = DateTime.Now.Ticks / 10000;
        for (int i = 0; i < N; ++i) {
            size += (ulong)CompressForFunapi(dst_buf, (UIntPtr)dst_buf.Length, (IntPtr)0,
                                      src_buf, (UIntPtr)src_buf.Length, (IntPtr)0, cdict);
            size2 += (ulong)DecompressForFunapi(src_buf, (UIntPtr)src_buf.Length, (IntPtr)0,
                    dst_buf, (UIntPtr)dst_buf.Length, (IntPtr)0, ddict);
        }
        end = DateTime.Now.Ticks / 10000;
        FunDebug.Log("Zstd (w/ dict) {0} ms, {1} ms/(enc+dec)", end - start, (end - start) * 1.0 / N);
        FunDebug.Log("String length={0}, compressed={1}", src_buf.Length, size / N);

        ZSTD_freeCDict(cdict);
        ZSTD_freeDDict(ddict);

        size = 0;
        size2 = 0;
        dst_buf = new byte[2 * src_buf.Length];
        start = DateTime.Now.Ticks / 10000;
        for (int i = 0; i < N; ++i) {
            size += (ulong)ZlibCompressForFunapi(dst_buf, (UIntPtr)0, (UIntPtr)dst_buf.Length,
                                      src_buf, (UIntPtr)0, (UIntPtr)src_buf.Length);
            size2 += (ulong)ZlibDecompressForFunapi(src_buf, (UIntPtr)0, (UIntPtr)src_buf.Length,
                    dst_buf, (UIntPtr)0, (UIntPtr)dst_buf.Length);
        }
        end = DateTime.Now.Ticks / 10000;
        FunDebug.Log("Deflate {0} ms, {1} ms/(enc+dec)", end - start, (end - start) * 1.0 / N);
        FunDebug.Log("String length={0}, compressed={1}", src_buf.Length, size / N);

        FunapiCompressor comp = new FunapiZstdCompressor();
        ArraySegment<byte> intermediate = comp.Compress(new ArraySegment<byte>(src_buf));
        ArraySegment<byte> comp_result = comp.Decompress(intermediate, src_buf.Length);

        var test_target = Encoding.UTF8.GetString(comp_result.Array);
        FunDebug.Assert(test_target == src);

        comp = new FunapiDeflateCompressor();
        intermediate = comp.Compress(new ArraySegment<byte>(src_buf));
        comp_result = comp.Decompress(intermediate, src_buf.Length);

        test_target = Encoding.UTF8.GetString(comp_result.Array);
        FunDebug.Assert(test_target == src);
    }
}

}  // namespace Fun