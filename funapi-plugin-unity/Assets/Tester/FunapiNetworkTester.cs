// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
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

// Protobuf (engine)
using funapi.network.fun_message;
using funapi.management.maintenance_message;
using funapi.service.multicast_message;

// Protobuf (user defined)
using test_messages;


public class FunapiNetworkTester : MonoBehaviour
{
    void Start()
    {
        //FunapiConfig.Load("Config.json");
    }

    void Update()
    {
        if (network_ != null)
            network_.Update();
    }

    void OnApplicationQuit()
    {
        if (network_ != null)
            network_.Stop();

        if (downloader_ != null)
            downloader_.Stop();
    }

    public void OnGUI()
    {
        //----------------------------------------------------------------------------
        // FunapiNetwork test
        //----------------------------------------------------------------------------
        with_session_reliability_ = GUI.Toggle(new Rect(30, 5, 130, 20), with_session_reliability_, " session reliability");
        with_protobuf_ = GUI.Toggle(new Rect(180, 5, 150, 20), with_protobuf_, " google protocol buffer");

        GUI.Label(new Rect(30, 40, 300, 20), "[FunapiNetwork] - " + kServerIp);
        GUI.enabled = (network_ == null || !network_.Started);
        if (GUI.Button(new Rect(30, 60, 240, 40), "Connect (TCP)"))
        {
            Connect(TransportProtocol.kTcp);
        }
        if (GUI.Button(new Rect(30, 105, 240, 40), "Connect (UDP)"))
        {
            Connect(TransportProtocol.kUdp);
        }
        if (GUI.Button(new Rect(30, 150, 240, 40), "Connect (HTTP)"))
        {
            Connect(TransportProtocol.kHttp);
        }

        GUI.enabled = (network_ != null && network_.Connected);
        if (GUI.Button(new Rect(30, 195, 240, 40), "Disconnect"))
        {
            Disconnect();
        }

        if (GUI.Button(new Rect(30, 240, 240, 40), "Send a message"))
        {
            SendEchoMessage();
        }

        //----------------------------------------------------------------------------
        // Announcements test
        //----------------------------------------------------------------------------
        GUI.enabled = true;
        GUI.Label(new Rect(30, 300, 300, 20), string.Format("[Announcer] - {0}:{1}", kAnnouncementIp, kAnnouncementPort));
        if (GUI.Button(new Rect(30, 320, 240, 40), "Update announcements"))
        {
            if (announcement_ == null)
            {
                announcement_ = new FunapiAnnouncement();
                announcement_.ResultCallback += new FunapiAnnouncement.EventHandler(OnAnnouncementResult);

                string url = "";
                if (FunapiConfig.IsValid)
                    url = FunapiConfig.AnnouncementUrl;

                if (url.Length <= 0)
                    url = string.Format("http://{0}:{1}", kAnnouncementIp, kAnnouncementPort);

                if (url.Length <= 0)
                    return;

                announcement_.Init(url);
            }

            announcement_.UpdateList(5);
        }

        //----------------------------------------------------------------------------
        // Resource download test
        //----------------------------------------------------------------------------
        GUI.enabled = downloader_ == null;
        GUI.Label(new Rect(30, 380, 300, 20), string.Format("[Downloader] - {0}:{1}", kDownloadServerIp, kDownloadServerPort));
        if (GUI.Button(new Rect(30, 400, 240, 40), "Resource downloader (HTTP)"))
        {
            string download_url = "";

            if (FunapiConfig.IsValid) {
                FunapiConfig.GetDownloaderUrl(out download_url);
            }

            if (download_url == "") {
                download_url = string.Format("http://{0}:{1}", kDownloadServerIp, kDownloadServerPort);
            }

            downloader_ = new FunapiHttpDownloader();
            downloader_.VerifyCallback += new FunapiHttpDownloader.VerifyEventHandler(OnDownloadVerify);
            downloader_.ReadyCallback += new FunapiHttpDownloader.ReadyEventHandler(OnDownloadReady);
            downloader_.UpdateCallback += new FunapiHttpDownloader.UpdateEventHandler(OnDownloadUpdate);
            downloader_.FinishedCallback += new FunapiHttpDownloader.FinishEventHandler(OnDownloadFinished);
            downloader_.GetDownloadList(download_url, FunapiUtils.GetLocalDataPath);
        }

        //----------------------------------------------------------------------------
        // FunapiMulticasting test
        //----------------------------------------------------------------------------
        GUI.enabled = (multicast_ == null);
        GUI.Label(new Rect(280, 40, 300, 20), "[Muticasting]");
        string multicast_title = "Create 'multicast'";
        if (GUI.Button(new Rect(280, 60, 240, 40), multicast_title))
        {
            FunapiTransport transport = null;
            if (network_ == null || (transport = network_.GetTransport(TransportProtocol.kTcp)) == null) {
                DebugUtils.LogWarning("You should connect to tcp transport first.");
            }
            else {
                multicast_ = new FunapiMulticastClient(network_, transport.Encoding);
                multicast_encoding_ = transport.Encoding;
            }
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected && !multicast_.InChannel(kMulticastTestChannel));
        multicast_title = "Join a channel";
        if (GUI.Button(new Rect(280, 105, 240, 40), multicast_title))
        {
            multicast_.JoinChannel(kMulticastTestChannel, OnMulticastChannelSignalled);
            DebugUtils.Log("Joining the multicast channel '{0}'", kMulticastTestChannel);
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected && multicast_.InChannel(kMulticastTestChannel));
        multicast_title = "Send a message";
        if (GUI.Button(new Rect(280, 150, 240, 40), multicast_title))
        {
            if (multicast_encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg["_channel"] = kMulticastTestChannel;
                mcast_msg["_bounce"] = true;
                mcast_msg["message"] = "multicast test message";

                multicast_.SendToChannel(mcast_msg);
            }
            else
            {
                PbufHelloMessage hello_msg = new PbufHelloMessage();
                hello_msg.message = "multicast test message";

                FunMulticastMessage mcast_msg = new FunMulticastMessage();
                mcast_msg.channel = kMulticastTestChannel;
                mcast_msg.bounce = true;
                Extensible.AppendValue(mcast_msg, (int)MulticastMessageType.pbuf_hello, hello_msg);

                multicast_.SendToChannel(mcast_msg);
            }

            DebugUtils.Log("Sending a message to the multicast channel '{0}'", kMulticastTestChannel);
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected && multicast_.InChannel(kMulticastTestChannel));
        multicast_title = "Leave a channel";
        if (GUI.Button(new Rect(280, 195, 240, 40), multicast_title))
        {
            multicast_.LeaveChannel(kMulticastTestChannel);
            DebugUtils.Log("Leaving the multicast channel '{0}'", kMulticastTestChannel);
        }

        GUI.Label(new Rect(280, 250, 300, 20), "[Multicast Chat]");
        GUI.enabled = (chat_ == null);
        string chat_title = "Create 'chat'";
        if (GUI.Button(new Rect(280, 270, 240, 40), chat_title))
        {
            FunapiTransport transport = null;
            if (network_ == null || (transport = network_.GetTransport(TransportProtocol.kTcp)) == null) {
                DebugUtils.LogWarning("You should connect to tcp transport first.");
            }
            else {
                chat_ = new FunapiChatClient(network_, transport.Encoding);
            }
        }

        GUI.enabled = (chat_ != null && chat_.Connected && !chat_.InChannel(kChatTestChannel));
        chat_title = "Join a channel";
        if (GUI.Button(new Rect(280, 315, 240, 40), chat_title))
        {
            chat_.JoinChannel(kChatTestChannel, kChatUserName, OnChatChannelReceived);
            DebugUtils.Log("Joining the chat channel '{0}'", kChatTestChannel);
        }

        GUI.enabled = (chat_ != null && chat_.Connected && chat_.InChannel(kChatTestChannel));
        chat_title = "Send a message";
        if (GUI.Button(new Rect(280, 360, 240, 40), chat_title))
        {
            chat_.SendText(kChatTestChannel, "hello world");

            DebugUtils.Log("Sending a message to the chat channel '{0}'", kChatTestChannel);
        }

        GUI.enabled = (chat_ != null && chat_.Connected && chat_.InChannel(kChatTestChannel));
        chat_title = "Leave a channel";
        if (GUI.Button(new Rect(280, 405, 240, 40), chat_title))
        {
            chat_.LeaveChannel(kChatTestChannel);
            DebugUtils.Log("Leaving the chat channel '{0}'", kChatTestChannel);
        }
    }


    private FunapiTransport GetNewTransport (TransportProtocol protocol)
    {
        FunapiTransport transport = null;
        FunEncoding encoding = with_protobuf_ ? FunEncoding.kProtobuf : FunEncoding.kJson;

        if (FunapiConfig.IsValid)
        {
            transport = FunapiConfig.CreateTransport(protocol, encoding);
        }

        if (transport == null)
        {
            if (protocol == TransportProtocol.kTcp)
            {
                transport = new FunapiTcpTransport(kServerIp, (ushort)(with_protobuf_ ? 8022 : 8012), encoding);
                transport.AutoReconnect = true;
                //transport.EnablePing = true;
                //transport.DisableNagle = true;

                //((FunapiTcpTransport)transport).SetEncryption(EncryptionType.kIFunEngine2Encryption);
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                transport = new FunapiUdpTransport(kServerIp, (ushort)(with_protobuf_ ? 8023 : 8013), encoding);
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                transport = new FunapiHttpTransport(kServerIp, (ushort)(with_protobuf_ ? 8028 : 8018), false, encoding);

                // Send messages using WWW class
                //((FunapiHttpTransport)transport).UseWWW = true;
            }
        }

        if (transport != null)
        {
            transport.StartedCallback += new TransportEventHandler(OnTransportStarted);
            transport.StoppedCallback += new TransportEventHandler(OnTransportClosed);
            transport.FailureCallback += new TransportEventHandler(OnTransportFailure);

            // Connect timeout.
            transport.ConnectTimeoutCallback += new TransportEventHandler(OnConnectTimeout);
            transport.ConnectTimeout = 10f;

            // If you prefer use specific Json implementation other than Dictionary,
            // you need to register json accessors to handle the Json implementation before FunapiNetwork::Start().
            // E.g., transport.JsonHelper = new YourJsonAccessorClass

            // Adds extra server list
            // Use HostHttp for http transport.
            //transport.AddServerList(new List<HostAddr>{
            //    new HostAddr("127.0.0.1", 8012), new HostAddr("127.0.0.1", 8012),
            //    new HostAddr("127.0.0.1", 8013), new HostAddr("127.0.0.1", 8018)
            //});
        }

        return transport;
    }

    private void Connect (TransportProtocol protocol)
    {
        DebugUtils.Log("-------- Connect --------\n" + DateTime.Now);

        if (network_ == null || !network_.SessionReliability)
        {
            network_ = new FunapiNetwork(with_session_reliability_);
            network_.OnSessionInitiated += new FunapiNetwork.SessionInitHandler(OnSessionInitiated);
            network_.OnSessionClosed += new FunapiNetwork.SessionCloseHandler(OnSessionClosed);
            network_.MaintenanceCallback += new FunapiNetwork.MessageEventHandler(OnMaintenanceMessage);
            network_.StoppedAllTransportCallback += new FunapiNetwork.NotifyHandler(OnStoppedAllTransport);
            network_.TransportConnectFailedCallback += new TransportEventHandler(OnTransportConnectFailed);
            network_.TransportDisconnectedCallback += new TransportEventHandler(OnTransportDisconnected);

            network_.RegisterHandler("echo", this.OnEcho);
            network_.RegisterHandler("pbuf_echo", this.OnEchoWithProtobuf);

            //network_.SetMessageProtocol(TransportProtocol.kTcp, "echo");
            //network_.SetMessageProtocol(TransportProtocol.kUdp, "pbuf_echo");

            FunapiTransport transport = GetNewTransport(protocol);
            network_.AttachTransport(transport);
        }
        else
        {
            if (!network_.HasTransport(protocol))
            {
                FunapiTransport transport = GetNewTransport(protocol);
                network_.AttachTransport(transport);
            }

            network_.SetDefaultProtocol(protocol);
        }

        network_.Start();
    }

    private void Disconnect ()
    {
        CancelInvoke();

        if (network_.Started == false)
        {
            DebugUtils.Log("You should connect first.");
        }
        else if (network_.SessionReliability)
        {
            network_.Stop(false);
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
            DebugUtils.Log("Failed to make a connection. Network instance was not generated.");
        }
        else if (!network_.Connected)
        {
            DebugUtils.Log("Failed to make a connection. Stopping the network module.");
            DebugUtils.Log("Maybe the server is down? Otherwise check out the encryption type.");

            network_.Stop();
        }
        else
        {
            DebugUtils.Log("Seems network succeeded to make a connection to a server.");
        }
    }

    private void SendEchoMessage ()
    {
        if (network_.Started == false && !network_.SessionReliability)
        {
            DebugUtils.Log("You should connect first.");
        }
        else
        {
            FunEncoding encoding = network_.GetEncoding(network_.GetDefaultProtocol());
            if (encoding == FunEncoding.kNone)
            {
                DebugUtils.Log("You should attach transport first.");
                return;
            }

            if (encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = "hello proto";
                FunMessage message = network_.CreateFunMessage(echo, MessageType.pbuf_echo);
                network_.SendMessage(MessageType.pbuf_echo, message);
            }
            if (encoding == FunEncoding.kJson)
            {
                // In this example, we are using Dictionary<string, object>.
                // But you can use your preferred Json implementation (e.g., Json.net) instead of Dictionary,
                // by changing JsonHelper member in FunapiTransport.
                // Please refer to comments inside Connect() function.
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = "hello world";
                network_.SendMessage("echo", message);
            }
        }
    }

    private void OnSessionInitiated (string session_id)
    {
        DebugUtils.Log("Session initiated. Session id:" + session_id);
    }

    private void OnSessionClosed ()
    {
        DebugUtils.Log("Session closed.");

        network_ = null;
        multicast_ = null;
        chat_ = null;
    }

    private void OnConnectTimeout (TransportProtocol protocol)
    {
        DebugUtils.Log("{0} Transport Connection timed out.", protocol);
    }

    private void OnTransportStarted (TransportProtocol protocol)
    {
        DebugUtils.Log("{0} Transport started.", protocol);
    }

    private void OnTransportClosed (TransportProtocol protocol)
    {
        DebugUtils.Log("{0} Transport closed.", protocol);
    }

    private void OnEcho (string msg_type, object body)
    {
        DebugUtils.Assert(body is Dictionary<string, object>);
        string strJson = Json.Serialize(body as Dictionary<string, object>);
        DebugUtils.Log("Received an echo message: " + strJson);
    }

    private void OnEchoWithProtobuf (string msg_type, object body)
    {
        DebugUtils.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        object obj = network_.GetMessage(msg, MessageType.pbuf_echo);
        if (obj == null)
            return;

        PbufEchoMessage echo = obj as PbufEchoMessage;
        DebugUtils.Log("Received an echo message: " + echo.msg);
    }

    private void OnDownloadVerify (string path)
    {
        DebugUtils.DebugLog("Check file - " + path);
    }

    private void OnDownloadReady (int total_count, UInt64 total_size)
    {
        downloader_.StartDownload();
    }

    private void OnDownloadUpdate (string path, long bytes_received, long total_bytes, int percentage)
    {
        DebugUtils.DebugLog("Downloading - path:{0} / received:{1} / total:{2} / {3}%",
                            path, bytes_received, total_bytes, percentage);
    }

    private void OnDownloadFinished (DownloadResult code)
    {
        downloader_ = null;
    }

    private void OnAnnouncementResult (AnnounceResult result)
    {
        DebugUtils.Log("OnAnnouncementResult - result: " + result);
        if (result != AnnounceResult.kSuccess)
            return;

        if (announcement_.ListCount > 0)
        {
            for (int i = 0; i < announcement_.ListCount; ++i)
            {
                Dictionary<string, object> list = announcement_.GetAnnouncement(i);
                string buffer = "";

                foreach (var item in list)
                {
                    buffer += string.Format("{0}: {1}\n", item.Key, item.Value);
                }

                DebugUtils.Log("announcement ({0}) >> {1}", i + 1, buffer);

                if (list.ContainsKey("image_url"))
                    DebugUtils.Log("image path > " + announcement_.GetImagePath(i));
            }
        }
    }

    private void OnMaintenanceMessage (string msg_type, object body)
    {
        FunEncoding encoding = network_.GetEncoding(network_.GetDefaultProtocol());
        if (encoding == FunEncoding.kNone)
        {
            DebugUtils.Log("Can't find a FunEncoding type for maintenance message.");
            return;
        }

        if (encoding == FunEncoding.kProtobuf)
        {
            FunMessage msg = body as FunMessage;
            object obj = network_.GetMessage(msg, MessageType.pbuf_maintenance);
            if (obj == null)
                return;

            MaintenanceMessage maintenance = obj as MaintenanceMessage;
            DebugUtils.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                           maintenance.date_start, maintenance.date_end, maintenance.messages);
        }
        else if (encoding == FunEncoding.kJson)
        {
            DebugUtils.Assert(body is Dictionary<string, object>);
            Dictionary<string, object> msg = body as Dictionary<string, object>;
            DebugUtils.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                           msg["date_start"], msg["date_end"], msg["messages"]);
        }
    }

    private void OnStoppedAllTransport()
    {
        DebugUtils.Log("OnStoppedAllTransport called.");
    }

    private void OnTransportConnectFailed (TransportProtocol protocol)
    {
        DebugUtils.Log("OnTransportConnectFailed called.");

        // If you want to try to reconnect, call 'Connect' or 'Reconnect' function.
        // Be careful to avoid falling into an infinite loop.

        //network_.Connect(protocol, new HostHttp("127.0.0.1", 8018));
        //network_.Reconnect(protocol);
    }

    private void OnTransportDisconnected (TransportProtocol protocol)
    {
        DebugUtils.Log("OnTransportDisconnected called.");
    }

    private void OnTransportFailure (TransportProtocol protocol)
    {
        DebugUtils.Log("OnTransportFailure({0}) - {1}", protocol, network_.LastErrorCode(protocol));
    }

    private void OnMulticastChannelSignalled(string channel_id, object body)
    {
        if (multicast_encoding_ == FunEncoding.kJson)
        {
            DebugUtils.Assert(body is Dictionary<string, object>);
            Dictionary<string, object> mcast_msg = body as Dictionary<string, object>;
            DebugUtils.Assert (channel_id == (mcast_msg["_channel"] as string));

            DebugUtils.Log("Received a multicast message from a channel '{0}'\nMessage: {1}",
                           channel_id, mcast_msg["message"]);
        }
        else
        {
            DebugUtils.Assert (body is FunMulticastMessage);
            FunMulticastMessage mcast_msg = body as FunMulticastMessage;
            DebugUtils.Assert (channel_id == mcast_msg.channel);
            PbufHelloMessage hello_msg = Extensible.GetValue<PbufHelloMessage>(mcast_msg, (int)MulticastMessageType.pbuf_hello);
            if (hello_msg == null)
                return;

            DebugUtils.Log("Received a multicast message from a channel '{0}'\nMessage: {1}",
                           channel_id, hello_msg.message);
        }
    }

    private void OnChatChannelReceived(string chat_channel, string sender, string text)
    {
        DebugUtils.Log("Received a chat channel message.\nChannel={0}, sender={1}, text={2}",
                       chat_channel, sender, text);
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";
    private const string kAnnouncementIp = "127.0.0.1";
    private const UInt16 kAnnouncementPort = 8080;
    private const string kDownloadServerIp = "127.0.0.1";
    private const UInt16 kDownloadServerPort = 8020;
    private const string kMulticastTestChannel = "test_channel";
    private const string kChatTestChannel = "chat_channel";
    private const string kChatUserName = "my_name";

    // member variables.
    private bool with_protobuf_ = false;
    private bool with_session_reliability_ = false;

    private FunapiNetwork network_ = null;
    private FunapiHttpDownloader downloader_ = null;
    private FunapiAnnouncement announcement_ = null;

    private FunapiMulticastClient multicast_ = null;
    private FunapiChatClient chat_ = null;
    private FunEncoding multicast_encoding_;
}
