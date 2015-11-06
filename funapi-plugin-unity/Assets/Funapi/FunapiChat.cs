// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using ProtoBuf;
using System.Collections.Generic;
#if !NO_UNITY
using UnityEngine;
#endif

// protobuf
using funapi.network.fun_message;
using funapi.service.multicast_message;


namespace Fun
{
    public class FunapiChatClient
    {
        public FunapiChatClient (FunapiNetwork network, FunEncoding encoding)
        {
            multicasting_ = new FunapiMulticastClient(network, encoding);
            encoding_ = encoding;
        }

        public bool Connected
        {
            get { return multicasting_ != null && multicasting_.Connected; }
        }

        public bool JoinChannel (string chat_channel, string my_name, OnChannelMessage handler)
        {
            if (!Connected)
            {
                return false;
            }

            bool need_multicast_join = false;
            lock (channel_lock_)
            {
                KeyValuePair<string, OnChannelMessage> p;
                if (!channel_info_.TryGetValue(chat_channel, out p))
                {
                    p = new KeyValuePair<string, OnChannelMessage>(my_name, handler);
                    channel_info_.Add (chat_channel, p);
                    need_multicast_join = true;
                }
            }
            if (need_multicast_join)
            {
                if (!multicasting_.JoinChannel(chat_channel, OnMulticastingReceived))
                {
                    return false;
                }
            }
            return true;
        }

        public bool LeaveChannel (string chat_channel)
        {
            if (!Connected)
            {
                return false;
            }

            lock (channel_lock_)
            {
                if (!channel_info_.ContainsKey(chat_channel))
                {
                    DebugUtils.Log("You are not in the chat channel: {0}", chat_channel);
                    return false;
                }
                channel_info_.Remove(chat_channel);
            }
            return multicasting_.LeaveChannel(chat_channel);
        }

        public bool InChannel (string chat_channel)
        {
            if (multicasting_ == null)
            {
                return false;
            }
            return multicasting_.InChannel (chat_channel);
        }

        public bool SendText (string chat_channel, string text)
        {
            if (!Connected)
            {
                return false;
            }

            KeyValuePair<string, OnChannelMessage> p;
            lock (channel_lock_)
            {
                if (!channel_info_.TryGetValue(chat_channel, out p))
                {
                    DebugUtils.Log("You are not in the chat channel: {0}", chat_channel);
                    return false;
                }
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg["_channel"] = chat_channel;
                mcast_msg["_bounce"] = true;

                mcast_msg["sender"] = p.Key;
                mcast_msg["text"] = text;

                multicasting_.SendToChannel(mcast_msg);
            }
            else
            {
                FunChatMessage chat_msg = new FunChatMessage ();
                chat_msg.sender = p.Key;
                chat_msg.text = text;

                FunMulticastMessage mcast_msg = new FunMulticastMessage ();
                mcast_msg.channel = chat_channel;
                mcast_msg.bounce = true;
                Extensible.AppendValue (mcast_msg, (int)MulticastMessageType.chat, chat_msg);

                multicasting_.SendToChannel(mcast_msg);
            }

            return true;
        }

        private void OnMulticastingReceived (string chat_channel, object data)
        {
            KeyValuePair<string, OnChannelMessage> p;
            lock (channel_lock_)
            {
                if (!channel_info_.TryGetValue(chat_channel, out p))
                {
                    DebugUtils.Log("You are not in the chat channel: {0}", chat_channel);
                    return;
                }
            }

            if (encoding_ == FunEncoding.kJson)
            {
                DebugUtils.Assert(data is Dictionary<string, object>);
                Dictionary<string, object> mcast_msg = data as Dictionary<string, object>;

                p.Value (chat_channel, mcast_msg["sender"] as string, mcast_msg["text"] as string);
            }
            else
            {
                DebugUtils.Assert (data is FunMulticastMessage);
                FunMulticastMessage mcast_msg = data as FunMulticastMessage;
                FunChatMessage chat_msg = Extensible.GetValue<FunChatMessage> (mcast_msg, (int)MulticastMessageType.chat);

                p.Value (chat_channel, chat_msg.sender, chat_msg.text);
            }
        }


        public delegate void OnChannelMessage(string channel_id, string sender, string text);

        private FunapiMulticastClient multicasting_ = null;
        private FunEncoding encoding_;
        private object channel_lock_ = new object();
        private Dictionary<string, KeyValuePair<string, OnChannelMessage>> channel_info_ = new Dictionary<string, KeyValuePair<string, OnChannelMessage>>();
    }
}