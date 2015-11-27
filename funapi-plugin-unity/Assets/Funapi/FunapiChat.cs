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
    public class FunapiChatClient : FunapiMulticastClient
    {
        public FunapiChatClient (FunapiNetwork network, FunEncoding encoding)
            : base(network, encoding)
        {
            JoinedCallback += new ChannelNotify(OnJoinedCallback);
            LeftCallback += new ChannelNotify(OnLeftCallback);
        }

        public bool JoinChannel (string channel_id, string my_name, OnChatMessage handler)
        {
            sender_ = my_name;

            return this.JoinChannel(channel_id, handler);
        }

        public bool JoinChannel (string channel_id, OnChatMessage handler)
        {
            if (!base.JoinChannel(channel_id, OnMulticastingReceived))
                return false;

            lock (chat_channel_lock_)
            {
                if (chat_channels_.ContainsKey(channel_id))
                {
                    DebugUtils.Log("Already joined the '{0}' channel.", channel_id);
                    return false;
                }

                chat_channels_.Add (channel_id, handler);
            }

            return true;
        }

        private void OnJoinedCallback (string channel_id, string sender)
        {
        }

        private void OnLeftCallback (string channel_id, string sender)
        {
            if (sender == sender_)
            {
                lock (chat_channel_lock_)
                {
                    if (!chat_channels_.ContainsKey(channel_id))
                    {
                        DebugUtils.Log("You are not in the '{0}' channel.", channel_id);
                        return;
                    }

                    chat_channels_.Remove(channel_id);
                }
            }
        }

        public bool SendText (string channel_id, string text)
        {
            if (!Connected)
                return false;

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kChannelId] = channel_id;
                mcast_msg[kBounce] = true;
                mcast_msg[kMessage] = text;

                SendToChannel(mcast_msg);
            }
            else
            {
                FunChatMessage chat_msg = new FunChatMessage ();
                chat_msg.text = text;

                FunMulticastMessage mcast_msg = new FunMulticastMessage ();
                mcast_msg.channel = channel_id;
                mcast_msg.bounce = true;
                Extensible.AppendValue (mcast_msg, (int)MulticastMessageType.chat, chat_msg);

                SendToChannel(mcast_msg);
            }

            return true;
        }

        private void OnMulticastingReceived (string channel_id, string sender, object data)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                DebugUtils.Assert(data is Dictionary<string, object>);
                Dictionary<string, object> mcast_msg = data as Dictionary<string, object>;

                lock (chat_channel_lock_)
                {
                    if (chat_channels_.ContainsKey(channel_id))
                    {
                        chat_channels_[channel_id](channel_id, sender, mcast_msg[kMessage] as string);
                    }
                }
            }
            else
            {
                DebugUtils.Assert (data is FunMulticastMessage);
                FunMulticastMessage mcast_msg = data as FunMulticastMessage;
                FunChatMessage chat_msg = Extensible.GetValue<FunChatMessage> (mcast_msg, (int)MulticastMessageType.chat);

                lock (chat_channel_lock_)
                {
                    if (chat_channels_.ContainsKey(channel_id))
                    {
                        chat_channels_[channel_id](channel_id, sender, chat_msg.text);
                    }
                }
            }
        }


        private static readonly string kBounce = "_bounce";
        private static readonly string kMessage = "_message";

        public delegate void OnChatMessage(string channel_id, string sender, string text);

        private object chat_channel_lock_ = new object();
        private Dictionary<string, OnChatMessage> chat_channels_ = new Dictionary<string, OnChatMessage>();
    }
}