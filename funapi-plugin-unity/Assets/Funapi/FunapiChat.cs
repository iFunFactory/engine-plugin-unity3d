// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System.Collections.Generic;

// protobuf
using ProtoBuf;
using funapi.service.multicast_message;


namespace Fun
{
    public class FunapiChatClient : FunapiMulticastClient
    {
        public FunapiChatClient (FunapiSession session, FunEncoding encoding)
            : base(session, encoding)
        {
            JoinedCallback += onUserJoined;
            LeftCallback += onUserLeft;
            ErrorCallback += onError;
        }

        public bool JoinChannel (string channel_id, string my_name, OnChatMessage handler)
        {
            sender_ = my_name;

            return this.JoinChannel(channel_id, handler);
        }

        public bool JoinChannel (string channel_id, OnChatMessage handler)
        {
            if (!base.JoinChannel(channel_id, onReceived))
                return false;

            lock (chat_channel_lock_)
            {
                if (chat_channels_.ContainsKey(channel_id))
                {
                    FunDebug.Log("Already joined the '{0}' channel.", channel_id);
                    return false;
                }

                chat_channels_.Add (channel_id, handler);
            }

            return true;
        }

        public override void LeaveAllChannels ()
        {
            if (!Connected)
                return;

            base.LeaveAllChannels();

            lock (chat_channel_lock_)
            {
                chat_channels_.Clear();
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

                FunMulticastMessage mcast_msg = FunapiMessage.CreateFunMessage(chat_msg, MulticastMessageType.chat);
                mcast_msg.channel = channel_id;
                mcast_msg.bounce = true;

                SendToChannel(mcast_msg);
            }

            return true;
        }


        void onUserJoined (string channel_id, string sender)
        {
        }

        void onUserLeft (string channel_id, string sender)
        {
            if (sender == sender_)
            {
                lock (chat_channel_lock_)
                {
                    if (!chat_channels_.ContainsKey(channel_id))
                    {
                        FunDebug.Log("You are not in the '{0}' channel.", channel_id);
                        return;
                    }

                    chat_channels_.Remove(channel_id);
                }
            }
        }

        void onReceived (string channel_id, string sender, object data)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                string text = json_helper_.GetStringField(data, kMessage);

                lock (chat_channel_lock_)
                {
                    if (chat_channels_.ContainsKey(channel_id))
                    {
                        chat_channels_[channel_id](channel_id, sender, text);
                    }
                }
            }
            else
            {
                FunDebug.Assert(data is FunMulticastMessage);
                FunMulticastMessage mcast_msg = data as FunMulticastMessage;

                object obj = FunapiMessage.GetMessage(mcast_msg, MulticastMessageType.chat);
                FunChatMessage chat_msg = obj as FunChatMessage;

                lock (chat_channel_lock_)
                {
                    if (chat_channels_.ContainsKey(channel_id))
                    {
                        chat_channels_[channel_id](channel_id, sender, chat_msg.text);
                    }
                }
            }
        }

        void onError (string channel_id, FunMulticastMessage.ErrorCode code)
        {
            if (code == FunMulticastMessage.ErrorCode.EC_FULL_MEMBER ||
                code == FunMulticastMessage.ErrorCode.EC_ALREADY_LEFT ||
                code == FunMulticastMessage.ErrorCode.EC_CLOSED)
            {
                lock (chat_channel_lock_)
                {
                    if (chat_channels_.ContainsKey(channel_id))
                        chat_channels_.Remove(channel_id);
                }
            }
        }


        const string kChannelId = "_channel";
        const string kBounce = "_bounce";
        const string kMessage = "_message";

        public delegate void OnChatMessage(string channel_id, string sender, string text);

        object chat_channel_lock_ = new object();
        Dictionary<string, OnChatMessage> chat_channels_ = new Dictionary<string, OnChatMessage>();
    }
}
