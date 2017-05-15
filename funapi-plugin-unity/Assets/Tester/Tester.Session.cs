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
        public override IEnumerator Start (FunapiSession session)
        {
            session_ = session;

            registerHandler();

            //session_.Stop();
            //while (session_.Started)
            //    yield return new WaitForSeconds(0.1f);

            //session_.Reconnect();
            //while (!session_.Connected)
            //    yield return new WaitForSeconds(0.1f);

            sendMessages(sendingCount);
            yield return new WaitForSeconds(0.2f);

            deregisterHandler();

            OnFinished();
        }

        void registerHandler ()
        {
            session_.ReceivedMessageCallback += onReceivedMessage;
            session_.ResponseTimeoutCallback += onResponseTimedOut;
            session_.MaintenanceCallback += onMaintenanceMessage;

            message_handler_["echo"] = onEcho;
            message_handler_["pbuf_echo"] = onEchoWithProtobuf;
        }

        void deregisterHandler ()
        {
            session_.ReceivedMessageCallback -= onReceivedMessage;
            session_.ResponseTimeoutCallback -= onResponseTimedOut;
        }

        void sendMessages (int sendingCount)
        {
            int i;

            if (session_.HasTransport(TransportProtocol.kTcp))
            {
                for (i = 0; i < sendingCount; ++i)
                    sendEchoMessage(TransportProtocol.kTcp);
            }

            if (session_.HasTransport(TransportProtocol.kUdp))
            {
                for (i = 0; i < sendingCount; ++i)
                    sendEchoMessage(TransportProtocol.kUdp);
            }

            if (session_.HasTransport(TransportProtocol.kHttp))
            {
                for (i = 0; i < sendingCount; ++i)
                    sendEchoMessage(TransportProtocol.kHttp);
            }
        }

        public void sendEchoMessage (TransportProtocol protocol)
        {
            if (!session_.Connected && !session_.ReliableSession)
            {
                FunDebug.Log("You should connect first.");
                return;
            }

            FunapiSession.Transport transport = session_.GetTransport(protocol);
            if (transport == null)
                return;

            if (transport.encoding == FunEncoding.kJson)
            {
                // In this example, we are using Dictionary<string, object>.
                // But you can use your preferred Json implementation (e.g., Json.net) instead of Dictionary,
                // by changing FunapiMessage.JsonHelper property.
                Dictionary<string, object> message = new Dictionary<string, object>();
                message["message"] = string.Format("[{0}] hello json", protocol.ToString().Substring(1).ToLower());
                session_.SendMessage("echo", message, protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
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
            Dictionary<string, object> json = message as Dictionary<string, object>;
            FunDebug.Log("Received an echo message: {0}", json["message"]);
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

        void onMaintenanceMessage (FunEncoding encoding, object message)
        {
            if (encoding == FunEncoding.kJson)
            {
                JsonAccessor json_helper = FunapiMessage.JsonHelper;
                FunDebug.Log("Maintenance message\nstart: {0}\nend: {1}\nmessage: {2}",
                             json_helper.GetStringField(message, "date_start"),
                             json_helper.GetStringField(message, "date_end"),
                             json_helper.GetStringField(message, "messages"));
            }
            else if (encoding == FunEncoding.kProtobuf)
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
        FunapiSession session_;
        Dictionary<string, MessageHandler> message_handler_ = new Dictionary<string, MessageHandler>();
    }
}
