// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
#if !NO_UNITY
using UnityEngine;
#endif


// Utility classes
namespace Fun
{
    // Funapi plugin version
    public class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 346;
    }


    public class FunapiUtils
    {
        public static string BytesToHex (byte[] array)
        {
            string hex = "";
            foreach (byte n in array)
                hex += n.ToString("x2");

            return hex;
        }

        public static byte[] HexToBytes (string hex)
        {
            byte[] array = new byte[hex.Length / 2];
            for (int i = 0; i < array.Length; ++i)
                array[i] = (byte)Convert.ToByte(hex.Substring(i * 2, 2), 16);

            return array;
        }

        public static bool EqualsBytes (byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; ++i)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        // Gets assets path
        public static string GetAssetsPath
        {
            get
            {
#if !NO_UNITY
                if (Application.platform == RuntimePlatform.OSXEditor ||
                    Application.platform == RuntimePlatform.WindowsEditor)
                {
                    return Application.dataPath;
                }
#endif

                return "";
            }
        }

        // Gets local path
        public static string GetLocalDataPath
        {
            get
            {
                if (path_ == null)
                {
#if !NO_UNITY
                    if (Application.platform == RuntimePlatform.Android ||
                        Application.platform == RuntimePlatform.IPhonePlayer)
                    {
                        path_ = Application.persistentDataPath;
                    }
                    else if (Application.platform == RuntimePlatform.OSXEditor ||
                             Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        string path = Application.dataPath;
                        path_ = path.Substring(0, path.LastIndexOf('/')) + "/Data";

                        if (!System.IO.Directory.Exists(path_))
                            System.IO.Directory.CreateDirectory(path_);
                    }
                    else
                    {
                        path_ = Application.dataPath;
                    }
#else
                    path_ = "";
#endif
                }

                return path_;
            }
        }

        static string path_ = null;
    }
}
