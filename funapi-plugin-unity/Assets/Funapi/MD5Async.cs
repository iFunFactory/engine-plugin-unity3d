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


public class MD5Async
{
    public static void Get (string path, OnFinish on_finish)
    {
        if (!File.Exists(path))
        {
            Debug.Log(string.Format("MD5Async.Compute - Can't find a file.\npath: {0}", path));
            return;
        }

        Fun.FunapiManager.instance.StartCoroutine(Compute(path, on_finish));
    }

    static IEnumerator Compute (string path, OnFinish on_finish)
    {
        FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[kBlockSize];
        MD5 md5 = MD5.Create();
        int length, read_bytes;

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
                stream.Close();
                break;
            }

            yield return new WaitForEndOfFrame();
        }

        string md5hash = "";
        foreach (byte n in md5.Hash)
            md5hash += n.ToString("x2");

        Fun.DebugUtils.Log(String.Format("MD5 >> {0} > {1}", stream.Name, md5hash));

        if (on_finish != null)
            on_finish(md5hash);
    }


    // Buffer-related constants.
    private static readonly int kBlockSize = 1024 * 1024;

    // Event handler delegate
    public delegate void OnFinish (string md5hash);
}
