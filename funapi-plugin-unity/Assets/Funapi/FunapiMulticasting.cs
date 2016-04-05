// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using ProtoBuf;
using System;
using System.Collections.Generic;
#if !NO_UNITY
using UnityEngine;
#endif

// protobuf
using funapi.network.fun_message;
using funapi.service.multicast_message;


namespace Fun
{
    public class FunapiMulticastClient
    {
        public FunapiMulticastClient (FunapiNetwork network, FunEncoding encoding)
        {
            DebugUtils.Assert(network != null);
            network_ = network;
            encoding_ = encoding;

            network_.RegisterHandlerWithProtocol(kMulticastMsgType, TransportProtocol.kTcp, OnReceived);
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
            get { return network_ != null && network_.Connected; }
        }

        public void RequestChannelList ()
        {
            if (!Connected)
            {
                DebugUtils.Log("Not connected. First connect before join a multicast channel.");
                return;
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kSender] = sender_;
                network_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage ();
                mcast_msg.sender = sender_;

                FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
                network_.SendMessage (kMulticastMsgType, fun_msg);
            }
        }

        public bool JoinChannel (string channel_id, ChannelMessage handler)
        {
            if (!Connected)
            {
                DebugUtils.Log("Not connected. First connect before join a multicast channel.");
                return false;
            }

            lock (channel_lock_)
            {
                if (channels_.ContainsKey(channel_id))
                {
                    DebugUtils.Log("Already joined the channel: {0}", channel_id);
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

                network_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage();
                mcast_msg.channel = channel_id;
                mcast_msg.sender = sender_;
                mcast_msg.join = true;

                FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
                network_.SendMessage(kMulticastMsgType, fun_msg);
            }

            DebugUtils.Log("Request to join '{0}' channel", channel_id);

            return true;
        }

        public bool LeaveChannel (string channel_id)
        {
            if (!Connected)
            {
                DebugUtils.Log("Not connected. If you are trying to leave a channel in which you were, "
                               + "connect first while preserving the session id you used for join.");
                return false;
            }

            lock (channel_lock_)
            {
                if (!channels_.ContainsKey(channel_id))
                {
                    DebugUtils.Log("You are not in the channel: {0}", channel_id);
                    return false;
                }
            }

            SendLeaveMessage(channel_id);
            OnLeftCallback(channel_id, sender_);

            lock (channel_lock_)
            {
                channels_.Remove(channel_id);
            }

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
                    SendLeaveMessage(channel_id);
                    OnLeftCallback(channel_id, sender_);
                }

                channels_.Clear();

                DebugUtils.Log("Leave all channels.");
            }
        }

        public bool InChannel (string channel_id)
        {
            lock (channel_lock_)
            {
                return channels_.ContainsKey(channel_id);
            }
        }

        /// <summary>
        /// The sender must fill in the mcast_msg.
        /// The "channel_id" field is mandatory.
        /// And mcas_msg must have join and leave flags set.
        /// </summary>
        public bool SendToChannel (FunMulticastMessage mcast_msg)
        {
            DebugUtils.Assert(encoding_ == FunEncoding.kProtobuf);
            DebugUtils.Assert(mcast_msg != null);
            DebugUtils.Assert(!mcast_msg.join);
            DebugUtils.Assert(!mcast_msg.leave);

            string channel_id = mcast_msg.channel;
            DebugUtils.Assert(channel_id != "");

            lock (channel_lock_)
            {
                if (!Connected)
                {
                    DebugUtils.Log("Not connected. If you are trying to leave a channel in which you were, "
                                   + "connect first while preserving the session id you used for join.");
                    return false;
                }
                if (!channels_.ContainsKey(channel_id))
                {
                    DebugUtils.Log("You are not in the channel: {0}", channel_id);
                    return false;
                }
            }

            mcast_msg.sender = sender_;

            FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
            network_.SendMessage(kMulticastMsgType, fun_msg);
            return true;
        }

        /// <summary>
        /// The sender must fill in the mcast_msg.
        /// The "channel_id" field is mandatory.
        /// And mcas_msg must have join and leave flags set.
        /// </summary>
        public bool SendToChannel (object json_msg)
        {
            DebugUtils.Assert(encoding_ == FunEncoding.kJson);
            DebugUtils.Assert(json_msg != null);

            Dictionary<string, object> mcast_msg = json_msg as Dictionary<string, object>;
            DebugUtils.Assert(!mcast_msg.ContainsKey(kJoin));
            DebugUtils.Assert(!mcast_msg.ContainsKey(kLeave));

            string channel_id = mcast_msg[kChannelId] as string;
            DebugUtils.Assert(channel_id != "");

            lock (channel_lock_)
            {
                if (!Connected)
                {
                    DebugUtils.Log("Not connected. If you are trying to leave a channel in which you were, "
                                   + "connect first while preserving the session id you used for join.");
                    return false;
                }
                if (!channels_.ContainsKey(channel_id))
                {
                    DebugUtils.Log("You are not in the channel: {0}", channel_id);
                    return false;
                }
            }

            mcast_msg[kSender] = sender_;

            network_.SendMessage(kMulticastMsgType, mcast_msg);
            return true;
        }

        private void SendLeaveMessage (string channel_id)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kChannelId] = channel_id;
                mcast_msg[kSender] = sender_;
                mcast_msg[kLeave] = true;

                network_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage();
                mcast_msg.channel = channel_id;
                mcast_msg.sender = sender_;
                mcast_msg.leave = true;

                FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
                network_.SendMessage(kMulticastMsgType, fun_msg);
            }
        }

        protected void OnReceived (string msg_type, object body)
        {
            DebugUtils.Assert(msg_type == kMulticastMsgType);

            string channel_id = "";
            string sender = "";
            bool join = false;
            bool leave = false;
            int error_code = 0;

            if (encoding_ == FunEncoding.kJson)
            {
                DebugUtils.Assert(body is Dictionary<string, object>);
                Dictionary<string, object> msg = body as Dictionary<string, object>;

                if (msg.ContainsKey(kChannelId))
                    channel_id = msg[kChannelId] as string;

                if (msg.ContainsKey(kSender))
                    sender = msg[kSender] as String;

                if (msg.ContainsKey(kErrorCode))
                    error_code = Convert.ToInt32(msg[kErrorCode]);

                if (msg.ContainsKey(kChannels))
                {
                    if (ChannelListCallback != null)
                        ChannelListCallback(msg[kChannels]);
                    return;
                }
                else if (msg.ContainsKey(kJoin))
                {
                    join = (bool)msg[kJoin];
                }
                else if (msg.ContainsKey(kLeave))
                {
                    leave = (bool)msg[kLeave];
                }
            }
            else
            {
                DebugUtils.Assert(body is FunMessage);
                FunMessage msg = body as FunMessage;

                object obj = network_.GetMessage(msg, MessageType.multicast);
                DebugUtils.Assert(obj != null);

                FunMulticastMessage mcast_msg = obj as FunMulticastMessage;

                if (mcast_msg.channelSpecified)
                    channel_id = mcast_msg.channel;

                if (mcast_msg.senderSpecified)
                    sender = mcast_msg.sender;

                if (mcast_msg.error_codeSpecified)
                    error_code = (int)mcast_msg.error_code;

                if (mcast_msg.channels.Count > 0)
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
                DebugUtils.LogWarning("Multicast error - channel: {0} code: {1}", channel_id, code);

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
                    DebugUtils.Log("You are not in the channel: {0}", channel_id);
                    return;
                }
            }

            if (join)
            {
                OnJoinedCallback(channel_id, sender);
            }
            else if (leave)
            {
                OnLeftCallback(channel_id, sender);
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

        private void OnJoinedCallback (string channel_id, string sender)
        {
            DebugUtils.Log("{0} joined the '{1}' channel", sender, channel_id);
            if (JoinedCallback != null)
                JoinedCallback(channel_id, sender);
        }

        private void OnLeftCallback (string channel_id, string sender)
        {
            DebugUtils.Log("{0} left the '{1}' channel", sender, channel_id);
            if (LeftCallback != null)
                LeftCallback(channel_id, sender);
        }


        protected static readonly string kMulticastMsgType = "_multicast";
        protected static readonly string kChannels = "_channels";
        protected static readonly string kChannelId = "_channel";
        protected static readonly string kSender = "_sender";
        protected static readonly string kJoin = "_join";
        protected static readonly string kLeave = "_leave";
        protected static readonly string kErrorCode = "_error_code";

        public delegate void ChannelList(object channel_list);
        public delegate void ChannelNotify(string channel_id, string sender);
        public delegate void ChannelMessage(string channel_id, string sender, object body);
        public delegate void ErrorNotify(string channel_id, FunMulticastMessage.ErrorCode code);

        private FunapiNetwork network_ = null;
        private object channel_lock_ = new object();
        private Dictionary<string, ChannelMessage> channels_ = new Dictionary<string, ChannelMessage>();

        protected FunEncoding encoding_;
        protected string sender_ = "";

        public event ChannelList ChannelListCallback;
        public event ChannelNotify JoinedCallback;
        public event ChannelNotify LeftCallback;
        public event ErrorNotify ErrorCallback;
    }
}
