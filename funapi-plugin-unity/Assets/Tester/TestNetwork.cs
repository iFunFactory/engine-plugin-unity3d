// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
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


public class TestNetwork
{
    public FunapiNetwork CreateNetwork (bool session_reliability)
    {
        FunapiNetwork network = new FunapiNetwork(session_reliability);

        network.OnSessionInitiated += OnSessionInitiated;
        network.OnSessionClosed += OnSessionClosed;
        network.MaintenanceCallback += OnMaintenanceMessage;
        network.StoppedAllTransportCallback += OnStoppedAllTransport;
        network.TransportConnectFailedCallback += OnTransportConnectFailed;
        network.TransportDisconnectedCallback += OnTransportDisconnected;

        network.RegisterHandler("echo", this.OnEcho);
        network.RegisterHandler("pbuf_echo", this.OnEchoWithProtobuf);

        //network.SetMessageProtocol(TransportProtocol.kTcp, "echo");
        //network.SetMessageProtocol(TransportProtocol.kUdp, "pbuf_echo");

        network_ = network;

        return network;
    }

    public FunapiTransport AddTransport (TransportProtocol protocol,
                                         string ip, FunEncoding encoding)
    {
        FunapiTransport transport = null;
        ushort port = GetPort(protocol, encoding);

        if (protocol == TransportProtocol.kTcp)
        {
            transport = new FunapiTcpTransport(ip, port, encoding);
            transport.AutoReconnect = true;
        }
        else if (protocol == TransportProtocol.kUdp)
        {
            transport = new FunapiUdpTransport(ip, port, encoding);
        }
        else if (protocol == TransportProtocol.kHttp)
        {
            transport = new FunapiHttpTransport(ip, port, false, encoding);
        }

        if (transport == null)
            return null;

        transport.StartedCallback += new TransportEventHandler(OnTransportStarted);
        transport.StoppedCallback += new TransportEventHandler(OnTransportClosed);
        transport.FailureCallback += new TransportEventHandler(OnTransportFailure);

        // Connect timeout.
        transport.ConnectTimeoutCallback += new TransportEventHandler(OnConnectTimeout);
        transport.ConnectTimeout = 10f;

        network_.AttachTransport(transport);

        return transport;
    }

    public void Disconnect ()
    {
        if (network_.Started == false)
        {
            FunDebug.Log("You should connect first.");
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

    public void SendEchoMessage ()
    {
        if (network_.Started == false && !network_.SessionReliability)
        {
            FunDebug.Log("You should connect first.");
        }
        else
        {
            FunEncoding encoding = network_.GetEncoding(network_.GetDefaultProtocol());
            if (encoding == FunEncoding.kNone)
            {
                FunDebug.Log("You should attach transport first.");
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
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = "hello world";
                network_.SendMessage("echo", message);
            }
        }
    }

    private ushort GetPort (TransportProtocol protocol, FunEncoding encoding)
    {
        ushort port = 0;
        if (protocol == TransportProtocol.kTcp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8012 : 8022);
        else if (protocol == TransportProtocol.kUdp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8013 : 8023);
        else if (protocol == TransportProtocol.kHttp)
            port = (ushort)(encoding == FunEncoding.kJson ? 8018 : 8028);

        return port;
    }

    private void OnSessionInitiated (string session_id)
    {
        FunDebug.Log("Session initiated. Session id:{0}", session_id);
    }

    private void OnSessionClosed ()
    {
        FunDebug.Log("Session closed.");
        //network = null;
    }

    private void OnConnectTimeout (TransportProtocol protocol)
    {
        FunDebug.Log("{0} Transport Connection timed out.", protocol);
    }

    private void OnTransportStarted (TransportProtocol protocol)
    {
        FunDebug.Log("{0} Transport started.", protocol);
    }

    private void OnTransportClosed (TransportProtocol protocol)
    {
        FunDebug.Log("{0} Transport closed.", protocol);
    }

    private void OnEcho (string msg_type, object body)
    {
        FunDebug.Assert(body is Dictionary<string, object>);
        string strJson = Json.Serialize(body as Dictionary<string, object>);
        FunDebug.Log("Received an echo message: {0}", strJson);
    }

    private void OnEchoWithProtobuf (string msg_type, object body)
    {
        FunDebug.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        object obj = network_.GetMessage(msg, MessageType.pbuf_echo);
        if (obj == null)
            return;

        PbufEchoMessage echo = obj as PbufEchoMessage;
        FunDebug.Log("Received an echo message: {0}", echo.msg);
    }

    private void OnMaintenanceMessage (string msg_type, object body)
    {
        FunEncoding encoding = network_.GetEncoding(network_.GetDefaultProtocol());
        if (encoding == FunEncoding.kNone)
        {
            FunDebug.Log("Can't find a FunEncoding type for maintenance message.");
            return;
        }

        if (encoding == FunEncoding.kProtobuf)
        {
            FunMessage msg = body as FunMessage;
            object obj = network_.GetMessage(msg, MessageType.pbuf_maintenance);
            if (obj == null)
                return;

            MaintenanceMessage maintenance = obj as MaintenanceMessage;
            FunDebug.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                         maintenance.date_start, maintenance.date_end, maintenance.messages);
        }
        else if (encoding == FunEncoding.kJson)
        {
            FunDebug.Assert(body is Dictionary<string, object>);
            Dictionary<string, object> msg = body as Dictionary<string, object>;
            FunDebug.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                         msg["date_start"], msg["date_end"], msg["messages"]);
        }
    }

    private void OnStoppedAllTransport()
    {
        FunDebug.Log("OnStoppedAllTransport called.");
    }

    private void OnTransportConnectFailed (TransportProtocol protocol)
    {
        FunDebug.Log("OnTransportConnectFailed called.");

        // If you want to try to reconnect, call 'Connect' or 'Reconnect' function.
        // Be careful to avoid falling into an infinite loop.

        //network.Connect(protocol, new HostHttp("127.0.0.1", 8018));
        //network.Reconnect(protocol);
    }

    private void OnTransportDisconnected (TransportProtocol protocol)
    {
        FunDebug.Log("OnTransportDisconnected called.");
    }

    private void OnTransportFailure (TransportProtocol protocol)
    {
        FunDebug.Log("OnTransportFailure({0}) - {1}", protocol, network_.LastErrorCode(protocol));
    }


    private FunapiNetwork network_ = null;
}
