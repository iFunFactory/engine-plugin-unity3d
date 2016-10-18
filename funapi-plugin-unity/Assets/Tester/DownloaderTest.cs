// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using UnityEngine;
using UnityEngine.UI;


public class DownloaderTest : MonoBehaviour
{
    void Awake ()
    {
        GameObject.Find("ServerIP").GetComponent<Text>().text =
            string.Format("Server - {0}:{1}", kServerIp, kServerPort);

        button_start_ = GameObject.Find("ButtonStart").GetComponent<Button>();
        button_start_.interactable = true;
    }

    public void OnStartDownloading ()
    {
        if (downloader_ != null)
            return;

        button_start_.interactable = false;

        downloader_ = new FunapiHttpDownloader();
        downloader_.VerifyCallback += onDownloadVerify;
        downloader_.ReadyCallback += onDownloadReady;
        downloader_.UpdateCallback += onDownloadUpdate;
        downloader_.FinishedCallback += onDownloadFinished;

        string url = string.Format("http://{0}:{1}", kServerIp, kServerPort);
        downloader_.GetDownloadList(url, FunapiUtils.GetLocalDataPath);
    }


    void onDownloadVerify (string path)
    {
        FunDebug.DebugLog("Check file - {0}", path);
    }

    void onDownloadReady (int total_count, UInt64 total_size)
    {
        downloader_.StartDownload();
    }

    void onDownloadUpdate (string path, long bytes_received, long total_bytes, int percentage)
    {
        FunDebug.DebugLog("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
                            path, bytes_received, total_bytes, percentage);
    }

    void onDownloadFinished (DownloadResult code)
    {
        button_start_.interactable = true;

        // If the code is DownloadResult.PAUSED, you can continue to download
        // by calling the 'FunapiHttpDownloader.ContinueDownload' function.
        if (code != DownloadResult.PAUSED)
            downloader_ = null;
    }


    // Please change this address to your server.
    const string kServerIp = "127.0.0.1";
    const UInt16 kServerPort = 8020;

    // Member variables.
    FunapiHttpDownloader downloader_ = null;

    // UI buttons
    Button button_start_;
}
