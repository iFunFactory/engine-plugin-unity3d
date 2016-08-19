﻿// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System.Collections.Generic;

// protobuf
using funapi.network.fun_message;
using funapi.management.maintenance_message;
using plugin_messages;


public class MessageHelper
{
    public MessageHelper (FunapiSession session, FunEncoding encoding)
    {
        session_ = session;
        encoding_ = encoding;

        session_.ReceivedMessageCallback += onReceivedMessage;
        session_.ResponseTimeoutCallback += onResponseTimedOut;

        message_handler_["echo"] = onEcho;
        message_handler_["pbuf_echo"] = onEchoWithProtobuf;
        message_handler_["_maintenance"] = onMaintenanceMessage;
    }

    public void SendEchoMessage ()
    {
        if (!session_.Connected && !session_.ReliableSession)
        {
            FunDebug.Log("You should connect first.");
            return;
        }

        if (encoding_ == FunEncoding.kJson)
        {
            // In this example, we are using Dictionary<string, object>.
            // But you can use your preferred Json implementation (e.g., Json.net) instead of Dictionary,
            // by changing FunapiMessage.JsonHelper property.
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["message"] = "hello world";
            session_.SendMessage("echo", message);
        }
        else if (encoding_ == FunEncoding.kProtobuf)
        {
            PbufEchoMessage echo = new PbufEchoMessage();
            echo.msg = "hello proto";
            FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
            session_.SendMessage(MessageType.pbuf_echo, message);
        }
    }


    void onReceivedMessage (string type, object message)
    {
        if (!message_handler_.ContainsKey(type))
        {
            FunDebug.LogWarning("No handler for message '{0}'. Ignoring.", type);
            return;
        }

        message_handler_[type](message);
    }

    void onResponseTimedOut (string type)
    {
        FunDebug.Log("Response timed out. type: {0}", type);
    }

    void onEcho (object message)
    {
        FunDebug.Assert(message is Dictionary<string, object>);
        string strJson = Json.Serialize(message);
        FunDebug.Log("Received an echo message: {0}", strJson);
    }

    void onEchoWithProtobuf (object message)
    {
        FunDebug.Assert(message is FunMessage);
        FunMessage msg = message as FunMessage;
        object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_echo);
        if (obj == null)
            return;

        PbufEchoMessage echo = obj as PbufEchoMessage;
        FunDebug.Log("Received an echo message: {0}", echo.msg);
    }

    void onMaintenanceMessage (object message)
    {
        if (encoding_ == FunEncoding.kJson)
        {
            JsonAccessor json_helper = FunapiMessage.JsonHelper;
            FunDebug.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                         json_helper.GetStringField(message, "date_start"),
                         json_helper.GetStringField(message, "date_end"),
                         json_helper.GetStringField(message, "messages"));
        }
        else if (encoding_ == FunEncoding.kProtobuf)
        {
            FunMessage msg = message as FunMessage;
            object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_maintenance);
            if (obj == null)
                return;

            MaintenanceMessage maintenance = obj as MaintenanceMessage;
            FunDebug.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                         maintenance.date_start, maintenance.date_end, maintenance.messages);
        }
    }


    delegate void MessageHandler (object message);

    // Member variables.
    FunapiSession session_ = null;
    FunEncoding encoding_ = FunEncoding.kJson;
    Dictionary<string, MessageHandler> message_handler_ = new Dictionary<string, MessageHandler>();
}
