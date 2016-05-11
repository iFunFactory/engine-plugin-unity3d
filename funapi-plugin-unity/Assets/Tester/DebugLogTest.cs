// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;


public class DebugLogTest : MonoBehaviour
{
    void Awake ()
    {
        FunDebug.RemoveAllLogFiles();
    }

    public void OnGUI()
    {
        GUI.Label(new Rect(30, 8, 300, 20), "Server - " + kServerIp);
        GUI.enabled = (network_ == null || !network_.Started);
        if (GUI.Button(new Rect(30, 35, 240, 40), "Connect"))
        {
            Connect(TransportProtocol.kTcp);
        }

        GUI.enabled = (network_ != null && network_.Connected);
        if (GUI.Button(new Rect(30, 80, 240, 40), "Disconnect"))
        {
            handler_.Disconnect();
        }
        if (GUI.Button(new Rect(30, 125, 240, 40), "Send a message"))
        {
            handler_.SendEchoMessage();
        }

        GUI.enabled = FunDebug.GetLogLength() > 0;
        if (GUI.Button(new Rect(30, 190, 240, 40), "Get Logs"))
        {
            Debug.Log(FunDebug.GetLogString());
        }
        if (GUI.Button(new Rect(30, 235, 240, 40), "Save Logs"))
        {
            FunDebug.SaveLogs();
        }
    }

    private void Connect (TransportProtocol protocol)
    {
        if (network_ == null)
        {
            handler_ = new TestNetwork();
            network_ = handler_.CreateNetwork(false);
            handler_.AddTransport(protocol, kServerIp, FunEncoding.kJson);
        }

        network_.Start();
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";

    // member variables.
    private TestNetwork handler_ = null;
    private FunapiNetwork network_ = null;
}
