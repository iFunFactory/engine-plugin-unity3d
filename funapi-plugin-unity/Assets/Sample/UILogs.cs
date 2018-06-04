// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using UnityEngine;
using System.Collections;


public class UILogs : MonoBehaviour
{
    void Awake ()
    {
        content_ = transform.GetComponentInChildren<UILogContent>();

#if ENABLE_OUTPUT
        Fun.FunDebug.OutputCallback += OnOutput;
#endif
    }

    void Start ()
    {
#if !ENABLE_OUTPUT
        content_.AddLog("If you want to see logs at this screen,\n" +
                        "you should define 'ENABLE_OUTPUT' symbol.");
#endif
    }

    void OnOutput (string type, string message)
    {
        content_.AddLog(message);
    }

    public void Clear ()
    {
        content_.ClearAll();
    }


    UILogContent content_;
}
