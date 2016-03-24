// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using ProtoBuf;
using System.Collections.Generic;
using UnityEngine;

// protobuf
using funapi.network.fun_message;
using funapi.management.maintenance_message;
using plugin_messages;


public class FunapiNetworkTest : MonoBehaviour
{
    public void OnGUI()
    {
        with_session_reliability_ = GUI.Toggle(new Rect(30, 38, 250, 20), with_session_reliability_, " use session reliability (only tcp)");
        with_protobuf_ = GUI.Toggle(new Rect(30, 58, 150, 20), with_protobuf_, " use protobuf-net");

        GUI.Label(new Rect(30, 8, 300, 20), "Server - " + kServerIp);
        GUI.enabled = (network_ == null || !network_.Started);
        if (GUI.Button(new Rect(30, 85, 240, 40), "Connect (TCP)"))
        {
            Connect(TransportProtocol.kTcp);
        }
        if (GUI.Button(new Rect(30, 130, 240, 40), "Connect (UDP)"))
        {
            Connect(TransportProtocol.kUdp);
        }
        if (GUI.Button(new Rect(30, 175, 240, 40), "Connect (HTTP)"))
        {
            Connect(TransportProtocol.kHttp);
        }

        GUI.enabled = (network_ != null && network_.Connected);
        if (GUI.Button(new Rect(30, 220, 240, 40), "Disconnect"))
        {
            Disconnect();
        }

        if (GUI.Button(new Rect(30, 265, 240, 40), "Send a message"))
        {
            SendEchoMessage();
        }
    }


    private FunapiTransport GetNewTransport (TransportProtocol protocol)
    {
        FunapiTransport transport = null;
        FunEncoding encoding = with_protobuf_ ? FunEncoding.kProtobuf : FunEncoding.kJson;

        if (protocol == TransportProtocol.kTcp)
        {
            transport = new FunapiTcpTransport(kServerIp, (ushort)(with_protobuf_ ? 8022 : 8012), encoding);
            transport.AutoReconnect = true;
            //transport.EnablePing = true;
            //transport.DisableNagle = true;

            // Please set the same encryption type as the encryption type of server.
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);
        }
        else if (protocol == TransportProtocol.kUdp)
        {
            transport = new FunapiUdpTransport(kServerIp, (ushort)(with_protobuf_ ? 8023 : 8013), encoding);

            // Please set the same encryption type as the encryption type of server.
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);
        }
        else if (protocol == TransportProtocol.kHttp)
        {
            transport = new FunapiHttpTransport(kServerIp, (ushort)(with_protobuf_ ? 8028 : 8018), false, encoding);

            // Send messages using WWW class
            //((FunapiHttpTransport)transport).UseWWW = true;

            // Please set the same encryption type as the encryption type of server.
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);
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
        DebugUtils.Log("-------- Connect --------");

        if (network_ == null || !network_.SessionReliability)
        {
            network_ = new FunapiNetwork(with_session_reliability_);
            //network_.ResponseTimeout = 10f;

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
        DebugUtils.Log("Session initiated. Session id:{0}", session_id);
    }

    private void OnSessionClosed ()
    {
        DebugUtils.Log("Session closed.");
        network_ = null;
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
        DebugUtils.Log("Received an echo message: {0}", strJson);
    }

    private void OnEchoWithProtobuf (string msg_type, object body)
    {
        DebugUtils.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        object obj = network_.GetMessage(msg, MessageType.pbuf_echo);
        if (obj == null)
            return;

        PbufEchoMessage echo = obj as PbufEchoMessage;
        DebugUtils.Log("Received an echo message: {0}", echo.msg);
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


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";

    // member variables.
    private bool with_protobuf_ = false;
    private bool with_session_reliability_ = false;
    private FunapiNetwork network_ = null;
}
