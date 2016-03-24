// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using UnityEngine;


public class DownloaderTest : MonoBehaviour
{
    public void OnGUI()
    {
        GUI.enabled = downloader_ == null;
        GUI.Label(new Rect(30, 8, 300, 20), string.Format("Server - {0}:{1}", kDownloadServerIp, kDownloadServerPort));
        if (GUI.Button(new Rect(30, 35, 240, 40), "Start downloading"))
        {
            string download_url = string.Format("http://{0}:{1}", kDownloadServerIp, kDownloadServerPort);

            downloader_ = new FunapiHttpDownloader();
            downloader_.VerifyCallback += new FunapiHttpDownloader.VerifyEventHandler(OnDownloadVerify);
            downloader_.ReadyCallback += new FunapiHttpDownloader.ReadyEventHandler(OnDownloadReady);
            downloader_.UpdateCallback += new FunapiHttpDownloader.UpdateEventHandler(OnDownloadUpdate);
            downloader_.FinishedCallback += new FunapiHttpDownloader.FinishEventHandler(OnDownloadFinished);
            downloader_.GetDownloadList(download_url, FunapiUtils.GetLocalDataPath);
        }
    }

    private void OnDownloadVerify (string path)
    {
        DebugUtils.DebugLog("Check file - {0}", path);
    }

    private void OnDownloadReady (int total_count, UInt64 total_size)
    {
        downloader_.StartDownload();
    }

    private void OnDownloadUpdate (string path, long bytes_received, long total_bytes, int percentage)
    {
        DebugUtils.DebugLog("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
                            path, bytes_received, total_bytes, percentage);
    }

    private void OnDownloadFinished (DownloadResult code)
    {
        downloader_ = null;
    }


    // Please change this address for test.
    private const string kDownloadServerIp = "127.0.0.1";
    private const UInt16 kDownloadServerPort = 8020;

    private FunapiHttpDownloader downloader_ = null;
}
