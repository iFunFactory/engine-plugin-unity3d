// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using ProtoBuf;
using System;
using System.Collections.Generic;
using UnityEngine;

// Protobuf
using funapi.network.fun_message;
using pbuf_echo;


public class FunapiNetworkTester : MonoBehaviour
{
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
                Connect(new FunapiTcpTransport(kServerIp, 8012));
            }
            Invoke("CheckConnection", 3f);
        }
        if (GUI.Button(new Rect(30, 90, 240, 40), "Connect (UDP)"))
        {
            FunapiUdpTransport transport = new FunapiUdpTransport(kServerIp, 8013);

            // Please set the same encryption type as the encryption type of server.
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);

            Connect(transport);
            Invoke("CheckConnection", 3f);
        }
        if (GUI.Button(new Rect(30, 150, 240, 40), "Connect (HTTP)"))
        {
            FunapiHttpTransport transport = new FunapiHttpTransport(kServerIp, 8018, false);

            // Please set the same encryption type as the encryption type of server.
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);

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

        GUI.enabled = downloader_ == null;
        if (GUI.Button(new Rect(280, 30, 340, 40), "File Download (HTTP)"))
        {
            downloader_ = new FunapiHttpDownloader(GetLocalResourcePath(), OnDownloadUpdate, OnDownloadFinished);
            downloader_.StartDownload(kServerIp, 8020, "list", false);
            message_ = " start downloading..";
            Invoke("CheckDownloadConnection", 3f);
        }

        GUI.enabled = true;
        GUI.TextField(new Rect(280, 71, 480, 24), message_);
    }

    private void Connect (FunapiTransport transport)
    {
        Debug.Log("Creating a network instance.");

        // You should pass an instance of FunapiTransport.
        network_ = new FunapiNetwork(transport, FunMsgType.kJson, false, this.OnSessionInitiated, this.OnSessionClosed);
        transport.StoppedCallback += new StoppedEventHandler(OnTransportClosed);
        transport_ = transport;

        // If you prefer use specific Json implementation other than Dictionary,
        // you need to register json accessors to handle the Json implementation before FunapiNetwork::Start().
        // E.g., transport.JsonHelper = new YourJsonAccessorClass

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
            if (transport_ != null)
                transport_.Stop();
        }
        else
        {
            network_.Stop();
            network_ = null;
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
            Debug.Log("Failed to make a connection. Stopping the network module.");
            Debug.Log("Maybe the server is down? Otherwise check out the encryption type.");

            network_.Stop();
            network_ = null;
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
                Dictionary<string, object> example = new Dictionary<string, object>();
                example["message"] = "hello world";
                network_.SendMessage("echo", example);
            }
            else if (network_.MsgType == FunMsgType.kProtobuf)
            {
                FunMessage example = new FunMessage();
                example.msgtype = "pbuf_echo";

                PbufEchoMessage echo = new PbufEchoMessage();
                echo.message = "hello proto";
                Extensible.AppendValue<PbufEchoMessage>(example, 16, echo);

                network_.SendMessage(example);
            }
        }
    }

    private void OnSessionInitiated(string session_id)
    {
        Debug.Log("Session initiated. Session id:" + session_id);
    }

    private void OnSessionClosed()
    {
        Debug.Log("Session closed");
    }

    private void OnTransportClosed()
    {
        Debug.Log("Transport closed");

    }

    private void OnEcho(string msg_type, object body)
    {
        DebugUtils.Assert(body is Dictionary<string, object>);
        string strJson = Json.Serialize(body as Dictionary<string, object>);
        Debug.Log("Received an echo message: " + strJson);
    }

    private void OnEchoWithProtobuf(string msg_type, object body)
    {
        DebugUtils.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        PbufEchoMessage echo = Extensible.GetValue<PbufEchoMessage>(msg, 16);
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

    // Get a personal path.
    private string GetLocalResourcePath()
    {
        if ((Application.platform == RuntimePlatform.Android) ||
            (Application.platform == RuntimePlatform.IPhonePlayer))
        {
            return Application.persistentDataPath;
        }
        else
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";

    // member variables.
    private FunapiNetwork network_ = null;
    private FunapiTransport transport_ = null;
    private FunapiHttpDownloader downloader_ = null;
    private string message_ = "";

    // Another Funapi-specific features will go here...
}
