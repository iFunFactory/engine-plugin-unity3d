// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using MiniJSON;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// protobuf
using funapi.network.fun_message;
using funapi.management.maintenance_message;
using plugin_messages;


public partial class Tester
{
    public class Session : Base
    {
        public override IEnumerator Start (params object[] param)
        {
            session_ = (FunapiSession)param[0];
            encoding_ = (FunEncoding)param[1];
            int sendingCount = (int)param[2];

            registerHandler();

            for (int i = 0; i < sendingCount; ++i)
            {
                session_.Stop();
                while (session_.Started)
                    yield return new WaitForSeconds(0.1f);

                session_.Reconnect();
                while (!session_.Connected)
                    yield return new WaitForSeconds(0.1f);

                sendMessages(sendingCount);
                yield return new WaitForSeconds(0.2f);
            }

            deregisterHandler();

            OnFinished();
        }

        void registerHandler ()
        {
            session_.ReceivedMessageCallback += onReceivedMessage;
            session_.ResponseTimeoutCallback += onResponseTimedOut;

            message_handler_["echo"] = onEcho;
            message_handler_["pbuf_echo"] = onEchoWithProtobuf;
            message_handler_["_maintenance"] = onMaintenanceMessage;
        }

        void deregisterHandler ()
        {
            session_.ReceivedMessageCallback -= onReceivedMessage;
            session_.ResponseTimeoutCallback -= onResponseTimedOut;
        }

        void sendMessages (int sendingCount)
        {
            int i;

            for (i = 0; i < sendingCount; ++i)
                sendEchoMessage(TransportProtocol.kTcp);

            for (i = 0; i < sendingCount; ++i)
                sendEchoMessage(TransportProtocol.kUdp);

            for (i = 0; i < sendingCount; ++i)
                sendEchoMessage(TransportProtocol.kHttp);
        }

        public void sendEchoMessage (TransportProtocol protocol = TransportProtocol.kDefault)
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
                message["message"] = string.Format("[{0}] hello json", protocol.ToString().Substring(1).ToLower());
                session_.SendMessage("echo", message, protocol);
            }
            else if (encoding_ == FunEncoding.kProtobuf)
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = string.Format("[{0}] hello proto", protocol.ToString().Substring(1).ToLower());
                FunMessage message = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session_.SendMessage(MessageType.pbuf_echo, message, protocol);
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
}
