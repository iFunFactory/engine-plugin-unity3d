// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

using Fun;


public class MD5Async
{
    public static void Compute (ref string path, ref DownloadFileInfo file, OnResult on_result)
    {
        if (File.Exists(path))
        {
            FunapiManager.instance.StartCoroutine(AsyncCompute(path, file, on_result));
            return;
        }

        DebugUtils.Log("MD5Async.Compute - Can't find a file.\npath: {0}", path);
        if (on_result != null)
            on_result(path, file, false);
    }

    static IEnumerator AsyncCompute (string path, DownloadFileInfo file, OnResult on_result)
    {
        MD5 md5 = MD5.Create();
        int length, read_bytes;
        byte[] buffer = new byte[kBlockSize];
        string md5hash = "";

        FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 0)
        {
            if (file.hash_front.Length > 0)
            {
                length = (stream.Length < kBlockSize) ? (int)stream.Length : kBlockSize;
                read_bytes = stream.Read(buffer, 0, length);
                md5.TransformFinalBlock(buffer, 0, read_bytes);

                md5hash = MakeHashString(md5.Hash);
                if (md5hash != file.hash_front || length == stream.Length)
                {
                    stream.Close();
                    if (on_result != null)
                        on_result(path, file, md5hash == file.hash_front && md5hash == file.hash);

                    yield break;
                }

                md5.Clear();
                md5 = MD5.Create();
                stream.Position = 0;

                yield return new WaitForEndOfFrame();
            }

            int sleep_count = 0;
            while (stream.Position < stream.Length)
            {
                length = (stream.Position + kBlockSize > stream.Length) ? (int)(stream.Length - stream.Position) : kBlockSize;
                read_bytes = stream.Read(buffer, 0, length);

                if (stream.Position < stream.Length)
                {
                    md5.TransformBlock(buffer, 0, read_bytes, buffer, 0);
                }
                else
                {
                    md5.TransformFinalBlock(buffer, 0, read_bytes);
                    break;
                }

                ++sleep_count;
                if (sleep_count % kMaxSleepCount == 0)
                    yield return new WaitForEndOfFrame();
            }
        }
        else
        {
            md5.TransformFinalBlock(buffer, 0, 0);
        }

        stream.Close();

        md5hash = MakeHashString(md5.Hash);
        if (on_result != null)
            on_result(path, file, md5hash == file.hash);
    }

    static string MakeHashString (byte[] hash)
    {
        string md5hash = "";
        foreach (byte n in hash)
            md5hash += n.ToString("x2");

        return md5hash;
    }


    // Buffer-related constants.
    private static readonly int kBlockSize = 1024 * 1024;
    private static readonly int kMaxSleepCount = 5;

    // Event handler delegate
    public delegate void OnResult (string path, DownloadFileInfo file, bool is_match);
}
