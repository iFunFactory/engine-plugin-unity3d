// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using ProtoBuf;
using SimpleJSON;
using System;
using UnityEngine;
using funapi.network.fun_message;
using pbuf_echo;


public class FunapiNetworkTester : MonoBehaviour
{
    public void OnGUI()
    {
        // For debugging
        GUI.enabled = network_ == null;
        if (GUI.Button(new Rect(30, 30, 240, 40), "Connect (TCP)"))
        {
            Connect(new FunapiTcpTransport(kServerIp, 8012));
            SendEchoMessage();
            Invoke("CheckConnection", 3f);
        }
        if (GUI.Button(new Rect(30, 90, 240, 40), "Connect (UDP)"))
        {
            Connect(new FunapiUdpTransport(kServerIp, 8013));
            SendEchoMessage();
            Invoke("CheckConnection", 3f);
        }
        if (GUI.Button(new Rect(30, 150, 240, 40), "Connect (HTTP)"))
        {
            Connect(new FunapiHttpTransport(kServerIp, 8018));
            SendEchoMessage();
            Invoke("CheckConnection", 3f);
        }

        GUI.enabled = downloader_ == null;
        if (GUI.Button(new Rect(280, 30, 340, 40), "File Download (HTTP)"))
        {
            downloader_ = new FunapiHttpDownloader(GetLocalResourcePath(), OnDownloadUpdate, OnDownloadFinished);
            downloader_.StartDownload(kResourceServerIp, 8000, "resources");
            message_ = " start downloading..";
        }

        GUI.enabled = true;
        GUI.TextField(new Rect(280, 71, 480, 24), message_);

        GUI.enabled = network_ != null;
        if (GUI.Button(new Rect(30, 210, 240, 40), "Disconnect"))
        {
            DisConnect();
        }
        if (GUI.Button(new Rect(30, 270, 240, 40), "Send 'Hello World'"))
        {
            SendEchoMessage();
        }
    }

    private void Connect (FunapiTransport transport)
    {
        UnityEngine.Debug.Log("Creating a network instance.");
        // You should pass an instance of FunapiTransport.
        network_ = new FunapiNetwork(transport, FunMsgType.kProtobuf, this.OnSessionInitiated, this.OnSessionClosed);

        network_.RegisterHandler("echo", this.OnEcho);
        network_.RegisterHandler("pbuf_echo", this.OnEchoWithProtobuf);
        network_.Start();
    }

    private void DisConnect ()
    {
        CancelInvoke();

        if (network_.Started == false)
        {
            UnityEngine.Debug.Log("You should connect first.");
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
            UnityEngine.Debug.LogWarning("Failed to make a connection. Network instance was not generated.");
        }
        else if (!network_.Connected || session_id_.Length <= 0)
        {
            UnityEngine.Debug.LogWarning("Maybe the server is down? Stopping the network module.");

            network_.Stop();
            network_ = null;
        }
        else
        {
            UnityEngine.Debug.Log("Seems network succeeded to make a connection to a server.");
        }
    }

    private void SendEchoMessage ()
    {
        if (network_.Started == false)
        {
            UnityEngine.Debug.Log("You should connect first.");
        }
        else
        {
            if (network_.MsgType == FunMsgType.kJson)
            {
                JSONClass example = new JSONClass();
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
        session_id_ = session_id;
        UnityEngine.Debug.Log("Session initiated. Session id:" + session_id);
    }

    private void OnSessionClosed()
    {
        UnityEngine.Debug.Log("Session closed");
    }

    private void OnEcho(string msg_type, object body)
    {
        DebugUtils.Assert(body is JSONClass);
        JSONClass json = body as JSONClass;
        UnityEngine.Debug.Log("Received an echo message: " + json.ToString());
    }

    private void OnEchoWithProtobuf(string msg_type, object body)
    {
        DebugUtils.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        PbufEchoMessage echo = Extensible.GetValue<PbufEchoMessage>(msg, 16);
        UnityEngine.Debug.Log("Received an echo message: " + echo.message);
    }

    private void OnDownloadUpdate (string path, long bytes_received, long total_bytes, int percentage)
    {
        message_ = " downloading - path:" + path + " / received:" + bytes_received + " / total:" + total_bytes + " / " + percentage + "%";
        UnityEngine.Debug.Log(message_);
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
    private const string kServerIp = "192.168.35.130";
    private const string kResourceServerIp = "127.0.0.1";

    // member variables.
    private FunapiNetwork network_ = null;
    private FunapiHttpDownloader downloader_ = null;
    private string session_id_ = "";
    private string message_ = "";

    // Another Funapi-specific features will go here...
}
