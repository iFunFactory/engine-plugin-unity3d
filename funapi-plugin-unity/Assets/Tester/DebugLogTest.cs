// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

// If you want to run this sample, you SHOULD define ENABLE_SAVE_LOG.
// Definition of ENABLE_SAVE_LOG is in DebugUtils.cs file.

using Fun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class DebugLogTest : MonoBehaviour
{
    void Awake ()
    {
        FunDebug.RemoveAllLogFiles();

        GameObject.Find("ServerIP").GetComponent<Text>().text = kServerIp;

        buttons_["connect"] = GameObject.Find("ButtonConnect").GetComponent<Button>();
        buttons_["disconnect"] = GameObject.Find("ButtonDisconnect").GetComponent<Button>();
        buttons_["send"] = GameObject.Find("ButtonSendMessage").GetComponent<Button>();
        buttons_["getlogs"] = GameObject.Find("ButtonGetLogs").GetComponent<Button>();
        buttons_["savelogs"] = GameObject.Find("ButtonSaveLogs").GetComponent<Button>();

        updateButtonState();
    }

    void FixedUpdate ()
    {
        bool enable = FunDebug.GetLogLength() > 0;
        buttons_["getlogs"].interactable = enable;
        buttons_["savelogs"].interactable = enable;
    }

    void updateButtonState ()
    {
        bool enable = network_ == null || !network_.Started;
        buttons_["connect"].interactable = enable;

        enable = network_ != null && network_.Connected;
        buttons_["disconnect"].interactable = enable;
        buttons_["send"].interactable = enable;
    }

    public void OnConnect ()
    {
        if (network_ == null)
        {
            handler_ = new TestNetwork();
            network_ = handler_.CreateNetwork(false);
            network_.StoppedAllTransportCallback += onStoppedAllTransport;

            FunapiTransport transport = handler_.AddTransport(TransportProtocol.kHttp, kServerIp, FunEncoding.kJson);
            transport.StartedCallback += onTransportStarted;
        }

        network_.Start();

        updateButtonState();
    }

    public void OnDisconnect ()
    {
        handler_.Disconnect();
    }

    public void OnSendMessage ()
    {
        handler_.SendEchoMessage();
    }

    public void OnGetLogs ()
    {
        Debug.Log(FunDebug.GetLogString());
    }

    public void OnSaveLogs ()
    {
        FunDebug.SaveLogs();
    }


    void onTransportStarted (TransportProtocol protocol)
    {
        updateButtonState();
    }

    void onStoppedAllTransport()
    {
        updateButtonState();
    }


    // Please change this address to your server.
    const string kServerIp = "127.0.0.1";

    // Member variables.
    TestNetwork handler_ = null;
    FunapiNetwork network_ = null;

    // UI buttons
    Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
