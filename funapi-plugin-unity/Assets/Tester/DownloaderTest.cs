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

        string download_url = string.Format("http://{0}:{1}", kServerIp, kServerPort);

        downloader_ = new FunapiHttpDownloader();
        downloader_.VerifyCallback += new FunapiHttpDownloader.VerifyEventHandler(OnDownloadVerify);
        downloader_.ReadyCallback += new FunapiHttpDownloader.ReadyEventHandler(OnDownloadReady);
        downloader_.UpdateCallback += new FunapiHttpDownloader.UpdateEventHandler(OnDownloadUpdate);
        downloader_.FinishedCallback += new FunapiHttpDownloader.FinishEventHandler(OnDownloadFinished);
        downloader_.GetDownloadList(download_url, FunapiUtils.GetLocalDataPath);

        button_start_.interactable = false;
    }

    void OnDownloadVerify (string path)
    {
        FunDebug.DebugLog("Check file - {0}", path);
    }

    void OnDownloadReady (int total_count, UInt64 total_size)
    {
        downloader_.StartDownload();
    }

    void OnDownloadUpdate (string path, long bytes_received, long total_bytes, int percentage)
    {
        FunDebug.DebugLog("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
                            path, bytes_received, total_bytes, percentage);
    }

    void OnDownloadFinished (DownloadResult code)
    {
        downloader_ = null;
        button_start_.interactable = true;
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";
    private const UInt16 kServerPort = 8020;

    private FunapiHttpDownloader downloader_ = null;
    private Button button_start_;
}
