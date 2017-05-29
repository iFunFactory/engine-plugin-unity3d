// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using ProtoBuf;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// protobuf
using funapi.service.multicast_message;
using plugin_messages;


public partial class Tester
{
    public class Multicast : Base
    {
        public Multicast (FunapiSession session)
        {
            FunapiSession.Transport transport = session.GetTransport(TransportProtocol.kTcp);
            if (transport == null)
            {
                FunDebug.Log("Can't find TCP transport.");
                return;
            }

            // MulticastClient
            FunEncoding encoding = transport.encoding;
            multicast_ = new FunapiMulticastClient(session, encoding);
            multicast_.sender = "player_" + UnityEngine.Random.Range(1, 100);

            multicast_.ChannelListCallback += delegate (object channel_list) {
                onMulticastChannelList(encoding, channel_list);
            };
            multicast_.JoinedCallback += delegate (string channel_id, string sender) {
                FunDebug.DebugLog("JoinedCallback called. player:{0}", sender);
            };
            multicast_.LeftCallback += delegate (string channel_id, string sender) {
                FunDebug.DebugLog("LeftCallback called. player:{0}", sender);
            };
            multicast_.ErrorCallback += onMulticastError;
        }

        public override IEnumerator Start ()
        {
            // Getting channel list
            multicast_.RequestChannelList();
            yield return new WaitForSeconds(0.1f);

            // Join the channel
            multicast_.JoinChannel(kChannelName, onMulticastChannelReceived);
            yield return new WaitForSeconds(0.1f);

            // Send messages
            for (int i = 0; i < sendingCount; ++i)
            {
                sendMulticastMessage();
                yield return new WaitForSeconds(0.1f);
            }

            // Getting channel list
            multicast_.RequestChannelList();
            yield return new WaitForSeconds(0.1f);

            // Leave the channel
            multicast_.LeaveChannel(kChannelName);
            yield return new WaitForSeconds(0.2f);

            multicast_.Clear();
            multicast_ = null;

            OnFinished();
        }

        public void sendMulticastMessage ()
        {
            if (multicast_.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg["_channel"] = kChannelName;
                mcast_msg["_bounce"] = true;
                mcast_msg["_message"] = "multicast test message";

                multicast_.SendToChannel(mcast_msg);
            }
            else
            {
                PbufHelloMessage hello_msg = new PbufHelloMessage();
                hello_msg.message = "multicast test message";

                FunMulticastMessage mcast_msg = FunapiMessage.CreateMulticastMessage(hello_msg, MulticastMessageType.pbuf_hello);
                mcast_msg.channel = kChannelName;
                mcast_msg.bounce = true;

                multicast_.SendToChannel(mcast_msg);
            }
        }

        void onMulticastChannelReceived (string channel_id, string sender, object body)
        {
            if (multicast_.encoding == FunEncoding.kJson)
            {
                string channel = FunapiMessage.JsonHelper.GetStringField(body, "_channel");
                FunDebug.Assert(channel != null && channel == channel_id);

                string message = FunapiMessage.JsonHelper.GetStringField(body, "_message");
                FunDebug.Log("Received a multicast message from the '{0}' channel.\nMessage: {1}",
                             channel_id, message);
            }
            else
            {
                FunDebug.Assert(body is FunMulticastMessage);
                FunMulticastMessage mcast_msg = body as FunMulticastMessage;
                FunDebug.Assert(channel_id == mcast_msg.channel);

                object obj = FunapiMessage.GetMulticastMessage(mcast_msg, MulticastMessageType.pbuf_hello);
                PbufHelloMessage hello_msg = obj as PbufHelloMessage;
                if (hello_msg == null)
                    return;

                FunDebug.Log("Received a multicast message from the '{0}' channel.\nMessage: {1}",
                             channel_id, hello_msg.message);
            }
        }

        void onMulticastError (string channel_id, FunMulticastMessage.ErrorCode code)
        {
            if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
            {
                // If the server is closed, try to rejoin the channel.
                if (multicast_ != null && multicast_.Connected)
                    multicast_.JoinChannel(kChannelName, onMulticastChannelReceived);
            }
        }


        const string kChannelName = "multicast";

        // Member variables.
        FunapiMulticastClient multicast_;
    }
}
