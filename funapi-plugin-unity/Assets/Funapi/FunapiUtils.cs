// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using UnityEngine;

namespace Fun
{
    // Utility class
    public class FunapiUtils
    {
        public static string GetLocalDataPath
        {
            get
            {
                if (path_ == null)
                {
                    if (Application.platform == RuntimePlatform.IPhonePlayer)
                    {
                        string path = Application.dataPath.Substring(0, Application.dataPath.Length - 5); // Strip "/Data" from path
                        path = path.Substring(0, path.LastIndexOf('/'));
                        path_ = path + "/Documents";
                    }
                    else if (Application.platform == RuntimePlatform.Android)
                    {
                        path_ = Application.persistentDataPath;
                    }
                    else
                    {
                        string path = Application.dataPath;
                        path_ = path.Substring(0, path.LastIndexOf('/'));
                    }
                }

                return path_;
            }
        }

        private static string path_ = null;
    }
}
