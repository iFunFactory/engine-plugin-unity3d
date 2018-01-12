// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections;
using UnityEngine.TestTools;


public class TestDownloader
{
    [UnityTest]
    public IEnumerator DownloadAll()
    {
        yield return new TestImpl ();
    }


    class TestImpl : TestBase
    {
        public TestImpl ()
        {
            FunapiHttpDownloader downloader = new FunapiHttpDownloader();

            downloader.VerifyCallback += delegate (string path)
            {
                FunDebug.DebugLog2("Check file - {0}", path);
            };

            downloader.ReadyCallback += delegate (int total_count, UInt64 total_size)
            {
                downloader.StartDownload();
            };

            downloader.UpdateCallback += delegate (string path, long bytes_received, long total_bytes, int percentage)
            {
                FunDebug.DebugLog2("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
                                   path, bytes_received, total_bytes, percentage);
            };

            downloader.FinishedCallback += delegate (DownloadResult code)
            {
                isFinished = true;
            };

            string url = string.Format("http://{0}:{1}/", TestInfo.ServerIp, 8020);
            downloader.GetDownloadList(url, FunapiUtils.GetLocalDataPath);
        }
    }
}
