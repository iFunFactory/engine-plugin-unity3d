// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using UnityEngine;
using UnityEngine.UI;


public partial class Tester
{
    public class Download
    {
        public Download ()
        {
            button_ = GameObject.Find("ButtonDownloadTest").GetComponent<Button>();
        }

        public void Start (string url)
        {
            button_.interactable = false;

            downloader_ = new FunapiHttpDownloader();
            downloader_.VerifyCallback += onDownloadVerify;
            downloader_.ReadyCallback += onDownloadReady;
            downloader_.UpdateCallback += onDownloadUpdate;
            downloader_.FinishedCallback += onDownloadFinished;

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
            button_.interactable = true;

            // If the code is DownloadResult.PAUSED, you can continue to download
            // by calling the 'FunapiHttpDownloader.ContinueDownload' function.
            if (code != DownloadResult.PAUSED)
                downloader_ = null;

            if (FinishedCallback != null)
                FinishedCallback();
        }

        public event Action FinishedCallback;

        // Member variables.
        FunapiHttpDownloader downloader_ = null;
        Button button_;
    }
}
