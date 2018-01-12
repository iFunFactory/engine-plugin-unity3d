// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.TestTools;

// protobuf
using funapi.service.multicast_message;
using plugin_messages;


public class TestMulticast
{
    [UnityTest]
    public IEnumerator multicastJson ()
    {
        yield return new TestImpl<FunapiMulticastClient> (TransportProtocol.kTcp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator multicastProtobuf ()
    {
        yield return new TestImpl<FunapiMulticastClient> (TransportProtocol.kTcp, FunEncoding.kProtobuf);
    }

    [UnityTest]
    public IEnumerator Chat_Json ()
    {
        yield return new TestImpl<FunapiChatClient> (TransportProtocol.kTcp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator Chat_Protobuf ()
    {
        yield return new TestImpl<FunapiChatClient> (TransportProtocol.kTcp, FunEncoding.kProtobuf);
    }


    class TestImpl<T> : TestSessionBase
    {
        public TestImpl (TransportProtocol protocol, FunEncoding encoding)
        {
            // Creates a session
            ushort port = getPort("multicast", protocol, encoding);
            session = FunapiSession.Create(TestInfo.ServerIp);
            session.Connect(protocol, encoding, port);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    // Creates a multicast handler
                    multicast = (FunapiMulticastClient)Activator.CreateInstance(typeof(T), new object[] {session, encoding});
                    multicast.sender = "player_" + UnityEngine.Random.Range(1, 100);

                    multicast.ChannelListCallback += delegate (object channel_list) {
                        onReceivedChannelList(encoding, channel_list);
                        joinMulticastChannel();
                    };
                    multicast.JoinedCallback += delegate (string channel_id, string sender) {
                        sendMulticastMessage();
                    };
                    multicast.LeftCallback += delegate (string channel_id, string sender) {
                        multicast.Clear();
                        isFinished = true;
                    };
                    multicast.ErrorCallback += onMulticastError;

                    multicast.RequestChannelList();
                }
            };

            setTimeoutCallbackWithFail (3f, delegate ()
            {
                session.Stop();
            });
        }

        void joinMulticastChannel ()
        {
            if (multicast is FunapiChatClient)
            {
                FunapiChatClient chat = (FunapiChatClient)multicast;
                chat.JoinChannel(kChannelName, onReceivedChatMessage);
            }
            else
                multicast.JoinChannel(kChannelName, onReceivedMulticastMessage);
        }

        void sendMulticastMessage ()
        {
            if (multicast is FunapiChatClient)
            {
                FunapiChatClient chat = (FunapiChatClient)multicast;
                chat.SendText(kChannelName, "chat message");
                return;
            }

            if (multicast.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg["_channel"] = kChannelName;
                mcast_msg["_bounce"] = true;
                mcast_msg["_message"] = "multicast message";

                multicast.SendToChannel(mcast_msg);
            }
            else
            {
                PbufHelloMessage hello_msg = new PbufHelloMessage();
                hello_msg.message = "multicast message";

                FunMulticastMessage mcast_msg = FunapiMessage.CreateMulticastMessage(hello_msg, MulticastMessageType.pbuf_hello);
                mcast_msg.channel = kChannelName;
                mcast_msg.bounce = true;

                multicast.SendToChannel(mcast_msg);
            }
        }

        void onReceivedChannelList (FunEncoding encoding, object channel_list)
        {
            if (encoding == FunEncoding.kJson)
            {
                List<object> list = channel_list as List<object>;
                if (list.Count <= 0) {
                    FunDebug.Log("Multicast - There are no channels.");
                    return;
                }

                StringBuilder data = new StringBuilder();
                data.Append("Multicast - channel list\n");
                foreach (Dictionary<string, object> info in list)
                {
                    data.AppendFormat("name:{0} members:{1}", info["_name"], info["_members"]);
                    data.AppendLine();
                }
                FunDebug.Log(data.ToString());
            }
            else
            {
                List<FunMulticastChannelListMessage> list = channel_list as List<FunMulticastChannelListMessage>;
                if (list.Count <= 0) {
                    FunDebug.Log("Multicast There are no channels.");
                    return;
                }

                StringBuilder data = new StringBuilder();
                data.Append("Multicast - channel list\n");
                foreach (FunMulticastChannelListMessage info in list)
                {
                    data.AppendFormat("name:{0} members:{1}", info.channel_name, info.num_members);
                    data.AppendLine();
                }
                FunDebug.Log(data.ToString());
            }
        }

        void onReceivedMulticastMessage (string channel_id, string sender, object body)
        {
            if (multicast.encoding == FunEncoding.kJson)
            {
                string channel = FunapiMessage.JsonHelper.GetStringField(body, "_channel");
                FunDebug.Assert(channel != null && channel == channel_id);

                string message = FunapiMessage.JsonHelper.GetStringField(body, "_message");
                FunDebug.Log("Multicast - Received a message from the '{0}' channel.\nMessage: {1}",
                             channel_id, message);
            }
            else
            {
                FunDebug.Assert(body is FunMulticastMessage);
                FunMulticastMessage mcast_msg = body as FunMulticastMessage;
                FunDebug.Assert(channel_id == mcast_msg.channel);

                PbufHelloMessage hello_msg = FunapiMessage.GetMulticastMessage<PbufHelloMessage>(mcast_msg, MulticastMessageType.pbuf_hello);
                if (hello_msg == null)
                    return;

                FunDebug.Log("Multicast - Received a message from the '{0}' channel.\nMessage: {1}",
                             channel_id, hello_msg.message);
            }

            multicast.LeaveChannel(kChannelName);
        }

        void onReceivedChatMessage (string chat_channel, string sender, string text)
        {
            FunDebug.Log("Chatting - Received a message.\nChannel={0}, sender={1}, text={2}",
                         chat_channel, sender, text);

            multicast.LeaveChannel(kChannelName);
        }

        void onMulticastError (string channel_id, FunMulticastMessage.ErrorCode code)
        {
            if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
            {
                // If the server is closed, try to rejoin the channel.
                if (multicast != null && multicast.Connected)
                    joinMulticastChannel();
            }
        }


        const string kChannelName = "multicast";

        FunapiMulticastClient multicast;
    }
}
