// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using UnityEngine;


namespace Fun
{
    public class FunapiManager : MonoBehaviour
    {
        public static FunapiManager instance
        {
            get
            {
                if (instance_ == null)
                {
                    GameObject obj = GameObject.Find(kInstanceName);
                    if (obj == null)
                        obj = new GameObject(kInstanceName);

                    instance_ = obj.AddComponent(typeof(FunapiManager)) as FunapiManager;
                }

                return instance_;
            }
        }


        private static readonly string kInstanceName = "Funapi Manager";
        private static FunapiManager instance_ = null;
    }

}