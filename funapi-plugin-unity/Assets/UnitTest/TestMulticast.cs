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
    public IEnumerator Multicast_Json ()
    {
        yield return new TestImpl<FunapiMulticastClient> (TransportProtocol.kTcp, FunEncoding.kJson);
    }

    [UnityTest]
    public IEnumerator Multicast_Protobuf ()
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
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.TransportEventCallback += delegate (TransportProtocol p, TransportEventType type)
            {
                if (isFinished)
                    return;

                if (type == TransportEventType.kStarted)
                {
                    // Creates a multicast handler
                    multicast = (FunapiMulticastClient)Activator.CreateInstance(typeof(T), new object[] {session, encoding});
                    multicast.sender = "player_" + UnityEngine.Random.Range(1, 100);

                    multicast.ChannelListCallback += delegate (object channel_list)
                    {
                        onReceivedChannelList(channel_list);
                        joinMulticastChannel();
                    };

                    multicast.JoinedCallback += delegate (string channel_id, string sender)
                    {
                        for (int i = 0; i < 5; ++i)
                            sendMulticastMessage();
                    };

                    multicast.LeftCallback += delegate (string channel_id, string sender)
                    {
                        multicast.Clear();
                        onTestFinished();
                    };

                    multicast.ErrorCallback += onMulticastError;

                    multicast.RequestChannelList();
                }
            };

            setTestTimeout(3f);

            ushort port = getPort("multicast", protocol, encoding);
            session.Connect(protocol, encoding, port);
        }


        void onReceivedChannelList (object channel_list)
        {
            if (multicast.encoding == FunEncoding.kJson)
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

        void joinMulticastChannel ()
        {
            if (multicast is FunapiChatClient)
            {
                FunapiChatClient chat = (FunapiChatClient)multicast;
                chat.JoinChannel(kChannelName, onReceivedChatMessage);
            }
            else
            {
                multicast.JoinChannel(kChannelName, onReceivedMulticastMessage);
            }
        }

        void sendMulticastMessage ()
        {
            if (multicast is FunapiChatClient)
            {
                FunapiChatClient chat = (FunapiChatClient)multicast;
                chat.SendText(kChannelName, "test chat message");
                ++sending_count;
                return;
            }

            if (multicast.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg["_channel"] = kChannelName;
                mcast_msg["_bounce"] = true;
                mcast_msg["_message"] = "test multicast message";

                multicast.SendToChannel(mcast_msg);
            }
            else
            {
                PbufHelloMessage hello_msg = new PbufHelloMessage();
                hello_msg.message = "test multicast message";

                FunMulticastMessage mcast_msg = FunapiMessage.CreateMulticastMessage(hello_msg, MulticastMessageType.pbuf_hello);
                mcast_msg.channel = kChannelName;
                mcast_msg.bounce = true;

                multicast.SendToChannel(mcast_msg);
            }

            ++sending_count;
        }

        void onReceivedMulticastMessage (string channel_id, string sender, object body)
        {
            if (multicast.encoding == FunEncoding.kJson)
            {
                string channel = FunapiMessage.JsonHelper.GetStringField(body, "_channel");
                FunDebug.Assert(channel != null && channel == channel_id);

                string message = FunapiMessage.JsonHelper.GetStringField(body, "_message");
                FunDebug.Log("Received message - channel: '{0}', message: {1}",
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

                FunDebug.Log("Received message - channel: '{0}', message: {1}",
                             channel_id, hello_msg.message);
            }

            --sending_count;
            if (sending_count <= 0)
                multicast.LeaveChannel(kChannelName);
        }

        void onReceivedChatMessage (string chat_channel, string sender, string text)
        {
            FunDebug.Log("Received message - Channel={0}, sender={1}, text={2}",
                         chat_channel, sender, text);

            --sending_count;
            if (sending_count <= 0)
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


        const string kChannelName = "test";

        FunapiMulticastClient multicast;
        int sending_count = 0;
    }
}
