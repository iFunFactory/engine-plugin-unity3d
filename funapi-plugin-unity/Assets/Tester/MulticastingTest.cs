// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using ProtoBuf;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// protobuf
using funapi.service.multicast_message;
using plugin_messages;


public class MulticastingTest : MonoBehaviour
{
    public void OnGUI ()
    {
        GUI.Label(new Rect(30, 8, 300, 20), "Server - " + kServerIp);
        GUI.Label(new Rect(30, 40, 300, 20), "[Muticast Test]");
        GUI.Label(new Rect(300, 40, 300, 20), "[Chat Test]");

        GUI.enabled = (multicast_ == null);
        string multicast_title = "Create 'multicast'";
        if (GUI.Button(new Rect(30, 60, 240, 40), multicast_title))
        {
            if (network_ == null)
                Connect();

            FunapiTransport transport = network_.GetTransport(TransportProtocol.kTcp);

            multicast_ = new FunapiMulticastClient(network_, transport.Encoding);
            multicast_.sender = "player" + UnityEngine.Random.Range(1, 100);

            multicast_.ChannelListCallback += delegate(object channel_list) {
                OnMulticastChannelList(multicast_.encoding, channel_list);
            };
            multicast_.JoinedCallback += delegate(string channel_id, string sender) {
                DebugUtils.DebugLog("JoinedCallback called. player:{0}", sender);
            };
            multicast_.LeftCallback += delegate(string channel_id, string sender) {
                DebugUtils.DebugLog("LeftCallback called. player:{0}", sender);
            };
            multicast_.ErrorCallback += OnMulticastError;
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected && !multicast_.InChannel(kMulticastTestChannel));
        multicast_title = "Join a channel";
        if (GUI.Button(new Rect(30, 105, 240, 40), multicast_title))
        {
            multicast_.JoinChannel(kMulticastTestChannel, OnMulticastChannelReceived);
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected && multicast_.InChannel(kMulticastTestChannel));
        multicast_title = "Send a message";
        if (GUI.Button(new Rect(30, 150, 240, 40), multicast_title))
        {
            if (multicast_.encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg["_channel"] = kMulticastTestChannel;
                mcast_msg["_bounce"] = true;
                mcast_msg["_message"] = "multicast test message";

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
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected && multicast_.InChannel(kMulticastTestChannel));
        multicast_title = "Leave a channel";
        if (GUI.Button(new Rect(30, 195, 240, 40), multicast_title))
        {
            multicast_.LeaveChannel(kMulticastTestChannel);
        }

        GUI.enabled = (multicast_ != null && multicast_.Connected);
        if (GUI.Button(new Rect(30, 240, 240, 40), "Get List"))
        {
            multicast_.RequestChannelList();
        }


        GUI.enabled = (chat_ == null);
        string chat_title = "Create 'chat'";
        if (GUI.Button(new Rect(300, 60, 240, 40), chat_title))
        {
            if (network_ == null)
                Connect();

            FunapiTransport transport = network_.GetTransport(TransportProtocol.kTcp);

            chat_ = new FunapiChatClient(network_, transport.Encoding);
            chat_.sender = "player" + UnityEngine.Random.Range(1, 100);

            chat_.ChannelListCallback += delegate(object channel_list) {
                OnMulticastChannelList(chat_.encoding, channel_list);
            };
            chat_.JoinedCallback += delegate(string channel_id, string sender) {
                DebugUtils.DebugLog("JoinedCallback called. player:{0}", sender);
            };
            chat_.LeftCallback += delegate(string channel_id, string sender) {
                DebugUtils.DebugLog("LeftCallback called. player:{0}", sender);
            };
            chat_.ErrorCallback += OnChatError;
        }

        GUI.enabled = (chat_ != null && chat_.Connected && !chat_.InChannel(kChatTestChannel));
        chat_title = "Join a channel";
        if (GUI.Button(new Rect(300, 105, 240, 40), chat_title))
        {
            chat_.JoinChannel(kChatTestChannel, OnChatChannelReceived);
        }

        GUI.enabled = (chat_ != null && chat_.Connected && chat_.InChannel(kChatTestChannel));
        chat_title = "Send a message";
        if (GUI.Button(new Rect(300, 150, 240, 40), chat_title))
        {
            chat_.SendText(kChatTestChannel, "hello everyone.");
        }

        GUI.enabled = (chat_ != null && chat_.Connected && chat_.InChannel(kChatTestChannel));
        chat_title = "Leave a channel";
        if (GUI.Button(new Rect(300, 195, 240, 40), chat_title))
        {
            chat_.LeaveChannel(kChatTestChannel);
        }

        GUI.enabled = (chat_ != null && chat_.Connected);
        if (GUI.Button(new Rect(300, 240, 240, 40), "Get List"))
        {
            chat_.RequestChannelList();
        }
    }


    private FunapiTransport GetNewTransport ()
    {
        FunapiTransport transport = null;
        FunEncoding encoding = FunEncoding.kJson;

        transport = new FunapiTcpTransport(kServerIp, 8012, encoding);
        transport.AutoReconnect = true;

        return transport;
    }

    private void Connect ()
    {
        DebugUtils.Log("-------- Connect --------");

        network_ = new FunapiNetwork(false);
        network_.StoppedAllTransportCallback += OnStoppedAllTransport;

        FunapiTransport transport = GetNewTransport();
        network_.AttachTransport(transport);

        network_.Start();
    }

    private void Disconnect ()
    {
        if (multicast_ != null)
            multicast_.LeaveAllChannels();

        if (network_.Started)
            network_.Stop();
    }

    private void OnStoppedAllTransport ()
    {
        DebugUtils.Log("OnStoppedAllTransport called.");
        network_ = null;
        multicast_ = null;
        chat_ = null;
    }

    private void OnMulticastChannelList (FunEncoding encoding, object channel_list)
    {
        if (encoding == FunEncoding.kJson)
        {
            List<object> list = channel_list as List<object>;
            if (list.Count <= 0)
                return;

            StringBuilder data = new StringBuilder();
            foreach (Dictionary<string, object> info in list)
            {
                foreach (KeyValuePair <string, object> item in info)
                    data.AppendFormat("{0}:{1}  ", item.Key, item.Value);
                data.AppendLine();
            }
            DebugUtils.Log(data.ToString());
        }
        else
        {
            List<FunMulticastChannelListMessage> list = channel_list as List<FunMulticastChannelListMessage>;
            if (list.Count <= 0)
                return;

            StringBuilder data = new StringBuilder();
            foreach (FunMulticastChannelListMessage info in list)
            {
                data.AppendFormat("name:{0}  members:{1}  ", info.channel_name, info.num_members);
                data.AppendLine();
            }
            DebugUtils.Log(data.ToString());
        }
    }

    private void OnMulticastChannelReceived (string channel_id, string sender, object body)
    {
        if (multicast_.encoding == FunEncoding.kJson)
        {
            DebugUtils.Assert(body is Dictionary<string, object>);
            Dictionary<string, object> mcast_msg = body as Dictionary<string, object>;
            DebugUtils.Assert (channel_id == (mcast_msg["_channel"] as string));

            string message = mcast_msg["_message"] as string;
            DebugUtils.Log("Received a multicast message from the '{0}' channel.\nMessage: {1}",
                           channel_id, message);
        }
        else
        {
            DebugUtils.Assert (body is FunMulticastMessage);
            FunMulticastMessage mcast_msg = body as FunMulticastMessage;
            DebugUtils.Assert (channel_id == mcast_msg.channel);

            PbufHelloMessage hello_msg = Extensible.GetValue<PbufHelloMessage>(mcast_msg, (int)MulticastMessageType.pbuf_hello);
            if (hello_msg == null)
                return;

            DebugUtils.Log("Received a multicast message from the '{0}' channel.\nMessage: {1}",
                           channel_id, hello_msg.message);
        }
    }

    private void OnChatChannelReceived (string chat_channel, string sender, string text)
    {
        DebugUtils.Log("Received a chat channel message.\nChannel={0}, sender={1}, text={2}",
                       chat_channel, sender, text);
    }

    private void OnMulticastError (string channel_id, FunMulticastMessage.ErrorCode code)
    {
        if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
        {
            // If the server is closed, try to rejoin the channel.
            if (multicast_ != null && multicast_.Connected)
                multicast_.JoinChannel(kMulticastTestChannel, OnMulticastChannelReceived);
        }
    }

    private void OnChatError (string channel_id, FunMulticastMessage.ErrorCode code)
    {
        if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
        {
            // If the server is closed, try to rejoin the channel.
            if (chat_ != null && chat_.Connected)
                chat_.JoinChannel(kChatTestChannel, OnChatChannelReceived);
        }
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";
    private const string kMulticastTestChannel = "multicast";
    private const string kChatTestChannel = "chat";

    // member variables.
    private FunapiNetwork network_ = null;
    private FunapiMulticastClient multicast_ = null;
    private FunapiChatClient chat_ = null;
}
