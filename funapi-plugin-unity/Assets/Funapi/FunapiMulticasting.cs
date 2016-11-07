﻿// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;

// protobuf
using funapi.network.fun_message;
using funapi.service.multicast_message;


namespace Fun
{
    public class FunapiMulticastClient
    {
        public FunapiMulticastClient (FunapiSession session, FunEncoding encoding)
        {
            FunDebug.Assert(session != null);

            session_ = session;
            encoding_ = encoding;

            session_.ReceivedMessageCallback += onReceived;
        }

        public void Clear ()
        {
            session_.ReceivedMessageCallback -= onReceived;
        }

        public string sender
        {
            set { sender_ = value; }
        }

        public FunEncoding encoding
        {
            get { return encoding_; }
        }

        public bool Connected
        {
            get { return session_ != null && session_.Connected; }
        }

        public void RequestChannelList ()
        {
            if (!Connected)
            {
                FunDebug.Log("Not connected. First connect before join a multicast channel.");
                return;
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kSender] = sender_;
                session_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage();
                mcast_msg.sender = sender_;

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast_msg, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg);
            }
        }

        public bool JoinChannel (string channel_id, ChannelMessage handler)
        {
            if (!Connected)
            {
                FunDebug.Log("Not connected. First connect before join a multicast channel.");
                return false;
            }

            lock (channel_lock_)
            {
                if (channels_.ContainsKey(channel_id))
                {
                    FunDebug.Log("Already joined the channel: {0}", channel_id);
                    return false;
                }

                channels_.Add(channel_id, handler);
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kChannelId] = channel_id;
                mcast_msg[kSender] = sender_;
                mcast_msg[kJoin] = true;

                session_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage();
                mcast_msg.channel = channel_id;
                mcast_msg.sender = sender_;
                mcast_msg.join = true;

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast_msg, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg);
            }

            FunDebug.Log("Request to join '{0}' channel", channel_id);

            return true;
        }

        public bool LeaveChannel (string channel_id)
        {
            if (!Connected)
            {
                FunDebug.Log("Not connected. If you are trying to leave a channel in which you were, "
                               + "connect first while preserving the session id you used for join.");
                return false;
            }

            lock (channel_lock_)
            {
                if (!channels_.ContainsKey(channel_id))
                {
                    FunDebug.Log("You are not in the channel: {0}", channel_id);
                    return false;
                }
            }

            lock (channel_lock_)
            {
                channels_.Remove(channel_id);
            }

            sendLeaveMessage(channel_id);
            onUserLeft(channel_id, sender_);

            return true;
        }

        public virtual void LeaveAllChannels ()
        {
            if (!Connected)
                return;

            lock (channel_lock_)
            {
                if (channels_.Count <= 0)
                    return;

                foreach (string channel_id in channels_.Keys)
                {
                    sendLeaveMessage(channel_id);
                    onUserLeft(channel_id, sender_);
                }

                channels_.Clear();

                FunDebug.Log("Leave all channels.");
            }
        }

        public bool InChannel (string channel_id)
        {
            lock (channel_lock_)
            {
                return channels_.ContainsKey(channel_id);
            }
        }


        /// The 'channel_id' field is mandatory.
        /// The 'sender' must fill in the message.
        /// The message shouldn't include join and leave flags.
        public bool SendToChannel (FunMulticastMessage mcast_msg)
        {
            if (mcast_msg == null)
                return false;

            FunDebug.Assert(!mcast_msg.join);
            FunDebug.Assert(!mcast_msg.leave);

            string channel_id = mcast_msg.channel;
            if (channel_id == "")
            {
                FunDebug.LogWarning("You should set a vaild channel id.");
                return false;
            }

            lock (channel_lock_)
            {
                if (!Connected)
                {
                    FunDebug.Log("Not connected. If you are trying to leave a channel in which you were, "
                                   + "connect first while preserving the session id you used for join.");
                    return false;
                }
                if (!channels_.ContainsKey(channel_id))
                {
                    FunDebug.Log("You are not in the channel: {0}", channel_id);
                    return false;
                }
            }

            mcast_msg.sender = sender_;

            FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast_msg, MessageType.multicast);
            session_.SendMessage(kMulticastMsgType, fun_msg);
            return true;
        }

        /// The 'channel_id' field is mandatory.
        /// The 'sender' must fill in the message.
        /// The message shouldn't include join and leave flags.
        public bool SendToChannel (object json_msg)
        {
            if (json_msg == null)
                return false;

            FunDebug.Assert(!json_helper_.HasField(json_msg, kJoin));
            FunDebug.Assert(!json_helper_.HasField(json_msg, kLeave));

            string channel_id = json_helper_.GetStringField(json_msg, kChannelId);
            if (channel_id == "")
            {
                FunDebug.LogWarning("You should set a vaild channel id.");
                return false;
            }

            lock (channel_lock_)
            {
                if (!Connected)
                {
                    FunDebug.Log("Not connected. If you are trying to leave a channel in which you were, "
                                   + "connect first while preserving the session id you used for join.");
                    return false;
                }
                if (!channels_.ContainsKey(channel_id))
                {
                    FunDebug.Log("You are not in the channel: {0}", channel_id);
                    return false;
                }
            }

            json_helper_.SetStringField(json_msg, kSender, sender_);

            session_.SendMessage(kMulticastMsgType, json_msg);
            return true;
        }


        void sendLeaveMessage (string channel_id)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kChannelId] = channel_id;
                mcast_msg[kSender] = sender_;
                mcast_msg[kLeave] = true;

                session_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage();
                mcast_msg.channel = channel_id;
                mcast_msg.sender = sender_;
                mcast_msg.leave = true;

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast_msg, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg);
            }
        }

        void onUserJoined (string channel_id, string user_id)
        {
            FunDebug.Log("{0} joined the '{1}' channel", user_id, channel_id);
            if (JoinedCallback != null)
                JoinedCallback(channel_id, user_id);
        }

        void onUserLeft (string channel_id, string user_id)
        {
            FunDebug.Log("{0} left the '{1}' channel", user_id, channel_id);
            if (LeftCallback != null)
                LeftCallback(channel_id, user_id);
        }

        void onReceived (string msg_type, object body)
        {
            FunDebug.Assert(msg_type == kMulticastMsgType);

            string channel_id = "";
            string sender = "";
            bool join = false;
            bool leave = false;
            int error_code = 0;

            if (encoding_ == FunEncoding.kJson)
            {
                if (json_helper_.HasField(body, kChannelId))
                    channel_id = json_helper_.GetStringField(body, kChannelId);

                if (json_helper_.HasField(body, kSender))
                    sender = json_helper_.GetStringField(body, kSender);

                if (json_helper_.HasField(body, kErrorCode))
                    error_code = (int)json_helper_.GetIntegerField(body, kErrorCode);

                if (json_helper_.HasField(body, kChannelList))
                {
                    if (ChannelListCallback != null)
                    {
                        object list = json_helper_.GetObject(body, kChannelList);
                        ChannelListCallback(list);
                    }
                    return;
                }
                else if (json_helper_.HasField(body, kJoin))
                {
                    join = json_helper_.GetBooleanField(body, kJoin);
                }
                else if (json_helper_.HasField(body, kLeave))
                {
                    leave = json_helper_.GetBooleanField(body, kLeave);
                }
            }
            else
            {
                FunMessage msg = body as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType.multicast);
                FunDebug.Assert(obj != null);

                FunMulticastMessage mcast_msg = obj as FunMulticastMessage;

                if (mcast_msg.channelSpecified)
                    channel_id = mcast_msg.channel;

                if (mcast_msg.senderSpecified)
                    sender = mcast_msg.sender;

                if (mcast_msg.error_codeSpecified)
                    error_code = (int)mcast_msg.error_code;

                if (mcast_msg.channels.Count > 0 || (channel_id == "" && sender == ""))
                {
                    if (ChannelListCallback != null)
                        ChannelListCallback(mcast_msg.channels);
                    return;
                }
                else if (mcast_msg.joinSpecified)
                {
                    join = mcast_msg.join;
                }
                else if (mcast_msg.leaveSpecified)
                {
                    leave = mcast_msg.leave;
                }

                body = mcast_msg;
            }

            if (error_code != 0)
            {
                FunMulticastMessage.ErrorCode code = (FunMulticastMessage.ErrorCode)error_code;
                FunDebug.LogWarning("Multicast error - channel: {0} code: {1}", channel_id, code);

                if (code == FunMulticastMessage.ErrorCode.EC_FULL_MEMBER ||
                    code == FunMulticastMessage.ErrorCode.EC_ALREADY_LEFT ||
                    code == FunMulticastMessage.ErrorCode.EC_CLOSED)
                {
                    lock (channel_lock_)
                    {
                        if (channels_.ContainsKey(channel_id))
                            channels_.Remove(channel_id);
                    }
                }

                if (ErrorCallback != null)
                    ErrorCallback(channel_id, code);

                return;
            }

            lock (channel_lock_)
            {
                if (!channels_.ContainsKey(channel_id))
                {
                    FunDebug.Log("You are not in the channel: {0}", channel_id);
                    return;
                }
            }

            if (join)
            {
                onUserJoined(channel_id, sender);
            }
            else if (leave)
            {
                onUserLeft(channel_id, sender);
            }
            else
            {
                lock (channel_lock_)
                {
                    if (channels_.ContainsKey(channel_id))
                        channels_[channel_id](channel_id, sender, body);
                }
            }
        }


        const string kMulticastMsgType = "_multicast";
        const string kChannelList = "_channels";
        const string kChannelId = "_channel";
        const string kSender = "_sender";
        const string kJoin = "_join";
        const string kLeave = "_leave";
        const string kErrorCode = "_error_code";

        public delegate void ChannelList(object channel_list);
        public delegate void ChannelNotify(string channel_id, string sender);
        public delegate void ChannelMessage(string channel_id, string sender, object body);
        public delegate void ErrorNotify(string channel_id, FunMulticastMessage.ErrorCode code);

        public event ChannelList ChannelListCallback;
        public event ChannelNotify JoinedCallback;
        public event ChannelNotify LeftCallback;
        public event ErrorNotify ErrorCallback;

        protected JsonAccessor json_helper_ = FunapiMessage.JsonHelper;
        protected FunEncoding encoding_;
        protected string sender_ = "";

        FunapiSession session_ = null;
        object channel_lock_ = new object();
        Dictionary<string, ChannelMessage> channels_ = new Dictionary<string, ChannelMessage>();
    }
}
