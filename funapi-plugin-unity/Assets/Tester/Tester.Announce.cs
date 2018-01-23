// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections.Generic;
using UnityEngine;


public partial class Tester
{
    public class Announce
    {
        public void Start (string host_name)
        {
            announcement_ = new FunapiAnnouncement();
            announcement_.ResultCallback += onAnnouncementResult;

            string url = string.Format("http://{0}:{1}", host_name, 8080);
            announcement_.Init(url);

            announcement_.UpdateList(5);
        }

        void onAnnouncementResult (AnnounceResult result)
        {
            if (result == AnnounceResult.kSucceeded && announcement_.ListCount > 0)
            {
                for (int i = 0; i < announcement_.ListCount; ++i)
                {
                    Dictionary<string, object> item = announcement_.GetAnnouncement(i);

                    string text = string.Format("{0} ({1})\n{2}", item["subject"], item["date"], item["message"]);
                    if (item.ContainsKey("image_url"))
                        text += string.Format("\nimage path : {0}", announcement_.GetImagePath(i));

                    Debug.Log(text);
                }
            }

            if (FinishedCallback != null)
                FinishedCallback();
        }


        public event Action FinishedCallback;

        // Member variables.
        FunapiAnnouncement announcement_ = null;
    }
}
