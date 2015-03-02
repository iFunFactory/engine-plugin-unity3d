// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
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
                if ((Application.platform == RuntimePlatform.Android) ||
                    (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    return Application.persistentDataPath;
                }
                else
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }
            }
        }
    }
}
