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
        public delegate void OnChannelMessage(string channel_id, string sender, string text);

        public FunapiChatClient()
        {
        }

        public bool Connected
        {
            get { return multicasting_ != null && multicasting_.Connected; }
        }

        public void Connect(string hostname_or_ip, ushort port, FunEncoding encoding, bool session_reliability)
        {
            // TODO(dkmoon): currenlty only Protobuf is supported.
            DebugUtils.Assert(encoding == FunEncoding.kProtobuf);

            // Discards previous instance, if any, and creates a brand new instance.
            multicasting_ = new FunapiMulticastClient (encoding);
            multicasting_.Connect(hostname_or_ip, port, session_reliability);
        }

        public void Close()
        {
            if (multicasting_ != null)
                multicasting_.Close();
        }

        public bool JoinChannel(string chat_channel, string my_name, OnChannelMessage handler)
        {
            if (multicasting_ == null || !multicasting_.Connected)
            {
                return false;
            }

            bool need_multicast_join = false;
            lock (lock_)
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

        public bool LeaveChannel(string chat_channel)
        {
            if (multicasting_ == null || !multicasting_.Connected)
            {
                return false;
            }

            lock (lock_)
            {
                if (!channel_info_.ContainsKey(chat_channel))
                {
                    Debug.Log("You are not in the chat channel: " + chat_channel);
                    return false;
                }
                channel_info_.Remove(chat_channel);
            }
            return multicasting_.LeaveChannel(chat_channel);
        }

        public bool InChannel(string chat_channel)
        {
            if (multicasting_ == null)
            {
                return false;
            }
            return multicasting_.InChannel (chat_channel);
        }

        public bool SendText(string chat_channel, string text)
        {
            if (multicasting_ == null || !multicasting_.Connected)
            {
                return false;
            }

            KeyValuePair<string, OnChannelMessage> p;
            lock (lock_)
            {
                if (!channel_info_.TryGetValue(chat_channel, out p))
                {
                    Debug.Log("You are not in the chat channel: " + chat_channel);
                    return false;
                }
            }

            FunChatMessage chat_msg = new FunChatMessage ();
            chat_msg.sender = p.Key;
            chat_msg.text = text;

            FunMulticastMessage mcast_msg = new FunMulticastMessage ();
            mcast_msg.channel = chat_channel;
            mcast_msg.bounce = true;
            Extensible.AppendValue (mcast_msg, (int)MulticastMessageType.chat, chat_msg);

            multicasting_.SendToChannel (mcast_msg);
            return true;
        }

        /// <summary>
        /// Please call this Update function inside your Unity3d Update.
        /// </summary>
        public void Update()
        {
            if (multicasting_ != null)
                multicasting_.Update ();
        }

        private void OnMulticastingReceived(string chat_channel, object data)
        {
            DebugUtils.Assert (data is FunMulticastMessage);
            FunMulticastMessage mcast_msg = data as FunMulticastMessage;
            FunChatMessage chat_msg = Extensible.GetValue<FunChatMessage> (mcast_msg, (int)MulticastMessageType.chat);

            KeyValuePair<string, OnChannelMessage> p;
            lock (lock_)
            {
                if (!channel_info_.TryGetValue(chat_channel, out p))
                {
                    Debug.Log("You are not in the chat channel: " + chat_channel);
                    return;
                }
            }
            p.Value (chat_channel, chat_msg.sender, chat_msg.text);
        }


        private FunapiMulticastClient multicasting_;

        private object lock_ = new object();
        private Dictionary<string, KeyValuePair<string, OnChannelMessage>> channel_info_ = new Dictionary<string, KeyValuePair<string, OnChannelMessage>>();
    }
}