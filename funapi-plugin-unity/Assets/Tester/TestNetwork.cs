// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
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

        network.OnSessionInitiated += onSessionInitiated;
        network.OnSessionClosed += onSessionClosed;
        network.MaintenanceCallback += onMaintenanceMessage;
        network.StoppedAllTransportCallback += onStoppedAllTransport;
        network.TransportConnectFailedCallback += onTransportConnectFailed;
        network.TransportDisconnectedCallback += onTransportDisconnected;

        network.RegisterHandler("echo", this.onEcho);
        network.RegisterHandler("pbuf_echo", this.onEchoWithProtobuf);

        //network.SetMessageProtocol(TransportProtocol.kTcp, "echo");
        //network.SetMessageProtocol(TransportProtocol.kUdp, "pbuf_echo");

        network_ = network;

        return network;
    }

    public FunapiTransport AddTransport (TransportProtocol protocol,
                                         string ip, FunEncoding encoding)
    {
        FunapiTransport transport = null;
        ushort port = getPort(protocol, encoding);

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

        transport.StartedCallback += onTransportStarted;
        transport.StoppedCallback += onTransportClosed;
        transport.FailureCallback += onTransportFailure;

        // Connect timeout.
        transport.ConnectTimeoutCallback += onConnectTimeout;
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
                FunDebug.Log("You should attach the transport first.");
                return;
            }

            if (encoding == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = "hello proto";
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
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

    ushort getPort (TransportProtocol protocol, FunEncoding encoding)
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

    void onSessionInitiated (string session_id)
    {
        FunDebug.Log("Session initiated. Session id:{0}", session_id);
    }

    void onSessionClosed ()
    {
        FunDebug.Log("Session closed.");
        //network = null;
    }

    void onConnectTimeout (TransportProtocol protocol)
    {
        FunDebug.Log("{0} Transport Connection timed out.", protocol);
    }

    void onTransportStarted (TransportProtocol protocol)
    {
        FunDebug.Log("{0} Transport started.", protocol);
    }

    void onTransportClosed (TransportProtocol protocol)
    {
        FunDebug.Log("{0} Transport closed.", protocol);
    }

    void onEcho (string msg_type, object body)
    {
        FunDebug.Assert(body is Dictionary<string, object>);
        string strJson = Json.Serialize(body as Dictionary<string, object>);
        FunDebug.Log("Received an echo message: {0}", strJson);
    }

    void onEchoWithProtobuf (string msg_type, object body)
    {
        FunDebug.Assert(body is FunMessage);
        FunMessage msg = body as FunMessage;
        object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_echo);
        if (obj == null)
            return;

        PbufEchoMessage echo = obj as PbufEchoMessage;
        FunDebug.Log("Received an echo message: {0}", echo.msg);
    }

    void onMaintenanceMessage (string msg_type, object body)
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
            object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_maintenance);
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

    void onStoppedAllTransport()
    {
        FunDebug.Log("OnStoppedAllTransport called.");
    }

    void onTransportConnectFailed (TransportProtocol protocol)
    {
        FunDebug.Log("OnTransportConnectFailed called.");

        // If you want to try to reconnect, call 'Connect' or 'Reconnect' function.
        // Be careful to avoid falling into an infinite loop.

        //network.Connect(protocol, new HostHttp("127.0.0.1", 8018));
        //network.Reconnect(protocol);
    }

    void onTransportDisconnected (TransportProtocol protocol)
    {
        FunDebug.Log("OnTransportDisconnected called.");
    }

    void onTransportFailure (TransportProtocol protocol)
    {
        FunDebug.Log("OnTransportFailure({0}) - {1}", protocol, network_.LastErrorCode(protocol));
    }


    // Member variables.
    FunapiNetwork network_ = null;
}
