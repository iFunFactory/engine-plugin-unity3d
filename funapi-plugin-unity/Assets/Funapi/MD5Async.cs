// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System.Collections;
using System.IO;
using System.Security.Cryptography;
#if !NO_UNITY
using UnityEngine;
#else
using System.Threading;
#endif


namespace Fun
{
    public class MD5Async
    {
        public static void Compute (MonoBehaviour mono, ref string path, ref DownloadFileInfo file, OnResult on_result)
        {
            if (!File.Exists(path))
            {
                FunDebug.LogWarning("MD5Async.Compute - Can't find a file.\npath: {0}", path);

                if (on_result != null)
                    on_result(path, file, false);

                return;
            }

#if !NO_UNITY
            mono.StartCoroutine(asyncCompute(path, file, on_result));
#else
            string path_ = path;
            DownloadFileInfo file_ = file;
            mono.StartCoroutine(delegate { asyncCompute(path_, file_, on_result); });
#endif
        }

#if !NO_UNITY
        static IEnumerator asyncCompute (string path, DownloadFileInfo file, OnResult on_result)
#else
        static void asyncCompute (string path, DownloadFileInfo file, OnResult on_result)
#endif
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

                    md5hash = FunapiUtils.BytesToHex(md5.Hash);
                    if (md5hash != file.hash_front || length == stream.Length)
                    {
                        stream.Close();

                        if (on_result != null)
                            on_result(path, file, md5hash == file.hash_front && md5hash == file.hash);

#if !NO_UNITY
                        yield break;
#else
                        return;
#endif
                    }

                    md5.Clear();
                    md5 = MD5.Create();
                    stream.Position = 0;

#if !NO_UNITY
                    yield return new WaitForEndOfFrame();
#endif
                }

                int sleep_count = 0;
                while (stream.Position < stream.Length)
                {
                    length = kBlockSize;
                    if (stream.Position + length > stream.Length)
                        length = (int)(stream.Length - stream.Position);

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
                    if (sleep_count >= kSleepCountMax)
                    {
                        sleep_count = 0;
#if !NO_UNITY
                        yield return new WaitForEndOfFrame();
#else
                        Thread.Sleep(30);
#endif
                    }
                }
            }
            else
            {
                md5.TransformFinalBlock(buffer, 0, 0);
            }

            stream.Close();

            md5hash = FunapiUtils.BytesToHex(md5.Hash);
            if (on_result != null)
                on_result(path, file, md5hash == file.hash);
        }


        // Buffer-related constants.
        const int kBlockSize = 1024 * 1024;
        const int kSleepCountMax = 5;

        // Event handler delegate
        public delegate void OnResult (string path, DownloadFileInfo file, bool is_match);
    }
}
