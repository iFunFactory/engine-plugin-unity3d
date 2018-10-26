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
    public IEnumerator Download_All()
    {
        yield return new TestImpl ();
    }

    [UnityTest]
    public IEnumerator Download_Game()
    {
        yield return new TestImpl ("game.json");
    }

    [UnityTest]
    public IEnumerator Download_Images()
    {
        yield return new TestImpl ("images.json");
    }

    [UnityTest]
    public IEnumerator Download_Sounds()
    {
        yield return new TestImpl ("sounds.json");
    }

    [UnityTest]
    public IEnumerator Download_Prefix_Images()
    {
        yield return new TestPrefixImpl ("images");
    }

    [UnityTest]
    public IEnumerator Download_Prefix_Sounds()
    {
        yield return new TestPrefixImpl ("sounds");
    }


    class TestImpl : TestBase
    {
        public TestImpl ()
        {
            FunapiHttpDownloader downloader = createDownloader();

            string url = string.Format("http://{0}:{1}/", TestInfo.ServerIp, 8020);
            downloader.GetDownloadList(url, FunapiUtils.GetLocalDataPath);
        }


        public TestImpl (string filename)
        {
            FunapiHttpDownloader downloader = createDownloader();

            string url = string.Format("http://{0}:{1}/", TestInfo.ServerIp, 8020);
            downloader.GetDownloadList(url, FunapiUtils.GetLocalDataPath, filename);
        }


        FunapiHttpDownloader createDownloader ()
        {
            FunapiHttpDownloader downloader = new FunapiHttpDownloader();

            downloader.VerifyCallback += delegate (string path)
            {
                FunDebug.LogDebug("Check file - {0}", path);
            };

            downloader.ReadyCallback += delegate (int total_count, UInt64 total_size)
            {
                downloader.StartDownload();
            };

            downloader.UpdateCallback += delegate (string path, long bytes_received, long total_bytes, int percentage)
            {
                FunDebug.LogDebug("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
                                  path, bytes_received, total_bytes, percentage);
            };

            downloader.FinishedCallback += delegate (DownloadResult code)
            {
                isFinished = true;
            };

            return downloader;
        }
    }


    class TestPrefixImpl : TestBase
    {
        public TestPrefixImpl (string prefix)
        {
            FunapiHttpDownloader downloader = new FunapiHttpDownloader();

            downloader.ReadyCallback += delegate (int total_count, UInt64 total_size)
            {
                downloader.StartDownload(prefix);
            };

            downloader.UpdateCallback += delegate (string path, long bytes_received, long total_bytes, int percentage)
            {
                FunDebug.LogDebug("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
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
