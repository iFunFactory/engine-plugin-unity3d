﻿// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System;
using System.Collections.Generic;
using UnityEngine;

// Protobuf
using funapi.network.fun_message;
using funapi.network.maintenance;
using pbuf_echo;


public class FunapiNetworkTester : MonoBehaviour
{
    public void Start()
    {
        string url = string.Format("http://{0}:8080", kServerIp);
        announcement_.Init(url);
        announcement_.ResultCallback += new FunapiAnnouncement.EventHandler(OnAnnouncementResult);
    }

    public void Update()
    {
        if (network_ != null)
            network_.Update();
    }

    public void OnGUI()
    {
        // For debugging
        GUI.enabled = (network_ == null ||  !network_.Connected);
        if (GUI.Button(new Rect(30, 30, 240, 40), "Connect (TCP)"))
        {
            if (network_ != null && network_.SessionReliability)
            {
                network_.Start();
            }
            else
            {
                FunapiTcpTransport transport = new FunapiTcpTransport(kServerIp, 8012);
                //transport.DisableNagle = true;

                Connect(transport);
            }

            Invoke("CheckConnection", 3f);
        }
        if (GUI.Button(new Rect(30, 90, 240, 40), "Connect (UDP)"))
        {
            Connect(new FunapiUdpTransport(kServerIp, 8013));
            Invoke("CheckConnection", 3f);
        }
        if (GUI.Button(new Rect(30, 150, 240, 40), "Connect (HTTP)"))
        {
            FunapiHttpTransport transport = new FunapiHttpTransport(kServerIp, 8018, false);
            transport.RequestFailureCallback += new FunapiHttpTransport.OnRequestFailure(OnHttpRequestFailure);

            Connect(transport);
            Invoke("CheckConnection", 3f);
        }

        GUI.enabled = (network_ != null && network_.Connected);
        if (GUI.Button(new Rect(30, 270, 240, 40), "Send 'Hello World'"))
        {
            SendEchoMessage();
        }

        if (GUI.Button(new Rect(30, 210, 240, 40), "Disconnect"))
        {
            DisConnect();
        }

        GUI.enabled = announcement_ != null;
        if (GUI.Button(new Rect(280, 30, 240, 40), "Update Announcements"))
        {
            announcement_.UpdateList();
        }

        GUI.enabled = downloader_ == null;
        if (GUI.Button(new Rect(280, 90, 240, 40), "File Download (HTTP)"))
        {
            downloader_ = new FunapiHttpDownloader(FunapiUtils.GetLocalDataPath, OnDownloadUpdate, OnDownloadFinished);
            downloader_.StartDownload(kServerIp, 8020, "list", false);
            message_ = " start downloading..";
            Invoke("CheckDownloadConnection", 3f);
        }

        GUI.enabled = true;
        GUI.TextField(new Rect(280, 131, 480, 24), message_);
    }

    private void Connect (FunapiTransport transport)
    {
        Debug.Log("Creating a network instance.");

        // You should pass an instance of FunapiTransport.
        network_ = new FunapiNetwork(transport, FunMsgType.kJson, false, this.OnSessionInitiated, this.OnSessionClosed);
        network_.MaintenanceCallback += new FunapiNetwork.OnMessageHandler(OnMaintenanceMessage);

        transport.StoppedCallback += new StoppedEventHandler(OnTransportClosed);

        // Timeout method only works with Tcp protocol.
        transport.ConnectTimeoutCallback += new ConnectTimeoutHandler(OnConnectTimeout);
        transport.ConnectTimeout = 3f;

        // If you prefer use specific Json implementation other than Dictionary,
        // you need to register json accessors to handle the Json implementation before FunapiNetwork::Start().
        // E.g., transport.JsonHelper = new YourJsonAccessorClass

        // Test for multi-transport
        //network_.AttachTransport(new FunapiTcpTransport(kServerIp, 8012));
        //network_.AttachTransport(new FunapiUdpTransport(kServerIp, 8013));
        //network_.AttachTransport(new FunapiHttpTransport(kServerIp, 8018, false));
        //network_.SetProtocol(TransportProtocol.kTcp, "echo");
        //network_.SetProtocol(TransportProtocol.kUdp, "pbuf_echo");

        network_.RegisterHandler("echo", this.OnEcho);
        network_.RegisterHandler("pbuf_echo", this.OnEchoWithProtobuf);

        network_.Start();
    }

    private void DisConnect ()
    {
        CancelInvoke();

        if (network_.Started == false)
        {
            Debug.Log("You should connect first.");
        }
        else if (network_.SessionReliability)
        {
            network_.StopTransport();
        }
        else
        {
            network_.Stop();
        }
    }

    private void CheckConnection ()
    {
        if (network_ == null)
        {
            Debug.Log("Failed to make a connection. Network instance was not generated.");
        }
        else if (!network_.Connected)
        {
            Debug.LogWarning("Maybe the server is down? Stopping the network module.");

            network_.Stop();
        }
        else
        {
            Debug.Log("Seems network succeeded to make a connection to a server.");
        }
    }

    private void CheckDownloadConnection ()
    {
        if (downloader_ != null && !downloader_.Connected)
        {
            Debug.Log("Maybe the server is down? Stopping Download.");

            downloader_.Stop();
            downloader_ = null;
        }
    }

    private void SendEchoMessage ()
    {
        if (network_.Started == false && !network_.SessionReliability)
        {
            Debug.Log("You should connect first.");
        }
        else
        {
            if (network_.MsgType == FunMsgType.kJson)
            {
                // In this example, we are using Dictionary<string, object>.
                // But you can use your preferred Json implementation (e.g., Json.net) instead of Dictionary,
                // by changing JsonHelper member in FunapiTransport.
                // Please refer to comments inside Connect() function.
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = "hello world";
                network_.SendMessage("echo", message);
            }
            else if (network_.MsgType == FunMsgType.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.message = "hello proto";
                FunMessage message = network_.CreateFunMessage(echo, 16);
                network_.SendMessage("pbuf_echo", message);
            }
        }
    }

    private void OnSessionInitiated (string session_id)
    {
        Debug.Log("Session initiated. Session id:" + session_id);
    }

    private void OnSessionClosed ()
    {
        Debug.Log("Session closed.");
    }

    private void OnConnectTimeout (TransportProtocol protocol)
    {
        Debug.Log(protocol + " Transport Connection timed out.");
    }

    private void OnTransportClosed (TransportProtocol protocol)
    {
        Debug.Log(protocol + " Transport closed.");
    }

    private void OnEcho (string msg_type, object body)
    {
        DebugUtils.Assert(body is Dictionary<string, object>);
        string strJson = Json.Serialize(body as Dictionary<string, object>);
        Debug.Log("Received an echo message: " + strJson);
    }

    private void OnEchoWithProtobuf (string msg_type, object body)
    {
        DebugUtils.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        object obj = network_.GetMessage(msg, typeof(PbufEchoMessage), 16);
        if (obj == null)
            return;

        PbufEchoMessage echo = obj as PbufEchoMessage;
        Debug.Log("Received an echo message: " + echo.message);
    }

    private void OnDownloadUpdate (string path, long bytes_received, long total_bytes, int percentage)
    {
        message_ = " downloading - path:" + path + " / received:" + bytes_received + " / total:" + total_bytes + " / " + percentage + "%";
        Debug.Log(message_);
    }

    private void OnDownloadFinished (DownloadResult code)
    {
        downloader_ = null;
        message_ = " download completed. result:" + code;
    }

    private void OnAnnouncementResult (AnnounceResult result)
    {
        Debug.Log("OnAnnouncementResult - result: " + result);
        if (result != AnnounceResult.kSuccess)
            return;

        if (announcement_.ListCount > 0)
        {
            for (int i = 0; i < announcement_.ListCount; ++i)
            {
                Dictionary<string, object> list = announcement_.GetAnnouncement(i);
                string buffer = "";

                foreach (var item in list)
                    buffer += item.Key + ": " + item.Value + "\n";

                Debug.Log("announcement >> " + buffer);
            }
        }
    }

    private void OnMaintenanceMessage (object body)
    {
        if (network_.MsgType == FunMsgType.kJson)
        {
            DebugUtils.Assert(body is Dictionary<string, object>);
            Dictionary<string, object> msg = body as Dictionary<string, object>;
            Debug.Log(String.Format("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                                    msg["date_start"], msg["date_end"], msg["messages"]));
        }
        else if (network_.MsgType == FunMsgType.kProtobuf)
        {
            FunMessage msg = body as FunMessage;
            object obj = network_.GetMessage(msg, typeof(MaintenanceMessage), 15);
            if (obj == null)
                return;

            MaintenanceMessage maintenance = obj as MaintenanceMessage;
            Debug.Log(String.Format("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                                    maintenance.date_start, maintenance.date_end, maintenance.messages));
        }
        else
        {
            DebugUtils.Assert(false);
        }
    }

    private void OnHttpRequestFailure (string msg_type)
    {
        Debug.Log("OnHttpRequestFailure - msg_type: " + msg_type);
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";

    // member variables.
    private FunapiNetwork network_ = null;
    private FunapiHttpDownloader downloader_ = null;
    private FunapiAnnouncement announcement_ = new FunapiAnnouncement();
    private string message_ = "";

    // Another Funapi-specific features will go here...
}
