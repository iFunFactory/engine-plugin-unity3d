// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.TestTools;


public class TestAnnounce
{
    [UnityTest]
    public IEnumerator GetList()
    {
        yield return new TestImpl ();
    }


    class TestImpl : TestBase
    {
        public TestImpl ()
        {
            FunapiAnnouncement announce = new FunapiAnnouncement();

            announce.ResultCallback += delegate (AnnounceResult result)
            {
                if (result == AnnounceResult.kSucceeded && announce.ListCount > 0)
                {
                    for (int i = 0; i < announce.ListCount; ++i)
                    {
                        Dictionary<string, object> item = announce.GetAnnouncement(i);

                        string text = string.Format("{0} ({1})\n{2}", item["subject"], item["date"], item["message"]);
                        if (item.ContainsKey("link_url"))
                            text += string.Format("\nurl : {0}", item["link_url"]);
                        if (item.ContainsKey("image_url"))
                            text += string.Format("\nimage path : {0}", announce.GetImagePath(i));

                        FunDebug.Log(text);
                    }
                }

                isFinished = true;
            };

            setTestTimeout(2f);

            string url = string.Format("http://{0}:{1}", TestInfo.ServerIp, 8080);
            announce.Init(url);

            announce.UpdateList(5);
        }
    }
}
