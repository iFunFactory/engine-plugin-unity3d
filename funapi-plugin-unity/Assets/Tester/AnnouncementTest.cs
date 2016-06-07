// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class AnnouncementTest : MonoBehaviour
{
    void Awake ()
    {
        GameObject.Find("ServerIP").GetComponent<Text>().text =
            string.Format("Server - {0}:{1}", kAnnouncementIp, kAnnouncementPort);
    }

    public void OnStartRequest ()
    {
        if (announcement_ == null)
        {
            announcement_ = new FunapiAnnouncement();
            announcement_.ResultCallback += OnAnnouncementResult;

            string url = string.Format("http://{0}:{1}", kAnnouncementIp, kAnnouncementPort);
            announcement_.Init(url);
        }

        announcement_.UpdateList(5);
    }

    void OnAnnouncementResult (AnnounceResult result)
    {
        FunDebug.Log("OnAnnouncementResult - result: {0}", result);
        if (result != AnnounceResult.kSuccess)
            return;

        if (announcement_.ListCount > 0)
        {
            for (int i = 0; i < announcement_.ListCount; ++i)
            {
                Dictionary<string, object> list = announcement_.GetAnnouncement(i);
                string buffer = "";

                foreach (var item in list)
                {
                    buffer += string.Format("{0}: {1}\n", item.Key, item.Value);
                }

                FunDebug.Log("announcement ({0}) >> {1}", i + 1, buffer);

                if (list.ContainsKey("image_url"))
                    FunDebug.Log("image path > {0}", announcement_.GetImagePath(i));
            }
        }
    }


    // Please change this address and port to your server.
    private const string kAnnouncementIp = "127.0.0.1";
    private const UInt16 kAnnouncementPort = 8080;

    private FunapiAnnouncement announcement_ = null;
}
