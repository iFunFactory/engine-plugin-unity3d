// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;


public class MD5Async
{
    public static void Compute (string path, OnFinish on_finish)
    {
        if (!File.Exists(path))
        {
            Debug.Log("MD5Async.Compute - Can't find a file. path: " + path);
            return;
        }

        FileStream stream = File.OpenRead(path);

        MD5Request request = new MD5Request();
        request.md5 = MD5.Create();
        request.stream = stream;
        request.on_finish = on_finish;

        int length = (stream.Length < kBlockSize) ? (int)stream.Length : kBlockSize;
        stream.BeginRead(request.buffer, 0, length, new AsyncCallback(ReadCb), request);
    }

    static void ReadCb (IAsyncResult ar)
    {
        MD5Request request = (MD5Request)ar.AsyncState;
        FileStream stream = request.stream;

        int length = stream.EndRead(ar);

        if (stream.Position >= stream.Length)
        {
            request.md5.TransformFinalBlock(request.buffer, 0, length);

            string md5hash = "";
            foreach (byte n in request.md5.Hash)
                md5hash += n.ToString("x2");

            Fun.DebugUtils.Log(String.Format("MD5 >> {0} > {1}", stream.Name, md5hash));

            if (request.on_finish != null)
                request.on_finish(md5hash);
        }
        else
        {
            request.md5.TransformBlock(request.buffer, 0, length, request.buffer, 0);

            length = (stream.Position + kBlockSize > stream.Length) ? (int)(stream.Length - stream.Position) : kBlockSize;
            stream.BeginRead(request.buffer, 0, length, new AsyncCallback(ReadCb), request);
        }
    }


    // Buffer-related constants.
    private static readonly int kBlockSize = 65536;

    // Event handler delegate
    public delegate void OnFinish (string md5hash);

    class MD5Request
    {
        public MD5 md5 = null;
        public FileStream stream = null;
        public byte[] buffer = new byte[kBlockSize];
        public OnFinish on_finish = null;
    }
}
