// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using UnityEngine;
using SimpleJSON;
using System;
using Fun;

public class FunapiNetworkTester : MonoBehaviour
{
    // Update is called once per frame
    public void Update()
    {
        if (start_time_ != 0.0f)
        {
            if (start_time_ + 5.0f < Time.time)
            {
                if (network_ == null)
                {
                    UnityEngine.Debug.Log("Failed to make a connection. Network instance was not generated.");
                }
                else if (network_.Connected == false)
                {
                    UnityEngine.Debug.Log("Failed to make a connection. Maybe the server is down? Stopping the network module.");
                    network_.Stop();
                    network_ = null;
                }
                else
                {
                    UnityEngine.Debug.Log("Seems network succeeded to make a connection to a server.");
                }

                start_time_ = 0.0f;
            }
        }
    }

    public void OnGUI()
    {
        // For debugging
        GUI.enabled = network_ == null;
        if (GUI.Button(new Rect(30, 30, 240, 40), "Connect (TCP)"))
        {
            Connect(new FunapiTcpTransport(kServerIp, 8012));
            start_time_ = Time.time;
        }
        if (GUI.Button(new Rect(30, 90, 240, 40), "Connect (UDP)"))
        {
            FunapiUdpTransport transport = new FunapiUdpTransport(kServerIp, 8013);

            // Please set the same encryption type as the encryption type of server.
            transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);

            Connect(transport);
            SendEchoMessage();
        }
        if (GUI.Button(new Rect(30, 150, 240, 40), "Connect (HTTP)"))
        {
            Connect(new FunapiHttpTransport(kServerIp, 8018));
            SendEchoMessage();
        }

        GUI.enabled = downloader_ == null;
        if (GUI.Button(new Rect(280, 30, 340, 40), "File Download (HTTP)"))
        {
            downloader_ = new FunapiHttpDownloader(GetLocalResourcePath(), OnDownloadUpdate, OnDownloadFinished);
            downloader_.StartDownload(kResourceServerIp, 8000, "resources");
            downloader_.StartDownload(kResourceServerIp, 8000, "sounds");
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
        network_ = new FunapiNetwork(transport, this.OnSessionInitiated, this.OnSessionClosed);

        network_.RegisterHandler("echo", this.OnEcho);
        network_.Start();
    }

    private void DisConnect ()
    {
        start_time_ = 0.0f;

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

    private void SendEchoMessage ()
    {
        if (network_.Started == false)
        {
            UnityEngine.Debug.Log("You should connect first.");
        }
        else
        {
            JSONClass example = new JSONClass();
            example["message"] = "hello world";
            network_.SendMessage("echo", example);
        }
    }

    private void OnSessionInitiated(string session_id)
    {
        UnityEngine.Debug.Log("Session initiated. Session id:" + session_id);
    }

    private void OnSessionClosed()
    {
        UnityEngine.Debug.Log("Session closed");
    }

    private void OnEcho(string msg_type, JSONClass body)
    {
        UnityEngine.Debug.Log("Received an echo message: " + body.ToString());
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
    private float start_time_ = 0.0f;
    private string message_ = "";

    // Another Funapi-specific features will go here...
}
