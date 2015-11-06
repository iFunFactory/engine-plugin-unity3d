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

        public bool Connected
        {
            get { return network_ != null && network_.Connected; }
        }

        public bool JoinChannel (string channel_id, ChannelReceiveHandler handler)
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

                channels_.Add (channel_id, handler);
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kChannelId] = channel_id;
                mcast_msg[kJoin] = true;
                network_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage ();
                mcast_msg.channel = channel_id;
                mcast_msg.join = true;

                FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
                network_.SendMessage (kMulticastMsgType, fun_msg);
            }

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

                channels_.Remove(channel_id);
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
                mcast_msg[kChannelId] = channel_id;
                mcast_msg[kLeave] = true;
                network_.SendMessage(kMulticastMsgType, mcast_msg);
            }
            else
            {
                FunMulticastMessage mcast_msg = new FunMulticastMessage ();
                mcast_msg.channel = channel_id;
                mcast_msg.leave = true;

                FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
                network_.SendMessage (kMulticastMsgType, fun_msg);
            }

            return true;
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

            FunMessage fun_msg = network_.CreateFunMessage(mcast_msg, MessageType.multicast);
            network_.SendMessage (kMulticastMsgType, fun_msg);
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
            // TODO(dkmoon): Verifies the passed json_msg has required fields.
            network_.SendMessage (kMulticastMsgType, json_msg);
            return true;
        }

        private void OnReceived (string msg_type, object body)
        {
            DebugUtils.Assert(msg_type == kMulticastMsgType);

            string channel_id = "";

            if (encoding_ == FunEncoding.kJson)
            {
                DebugUtils.Assert(body is Dictionary<string, object>);
                Dictionary<string, object> msg = body as Dictionary<string, object>;

                channel_id = msg[kChannelId] as string;

                lock (channel_lock_)
                {
                    if (!channels_.ContainsKey(channel_id))
                    {
                        DebugUtils.Log("You are not in the channel: {0}", channel_id);
                        return;
                    }

                    ChannelReceiveHandler h = channels_[channel_id];
                    h(channel_id, body);
                }
            }
            else
            {
                DebugUtils.Assert(body is FunMessage);
                FunMessage msg = body as FunMessage;

                object obj = network_.GetMessage(msg, MessageType.multicast);
                DebugUtils.Assert(obj != null);

                FunMulticastMessage mcast_msg = obj as FunMulticastMessage;
                channel_id = mcast_msg.channel;

                lock (channel_lock_)
                {
                    if (!channels_.ContainsKey(channel_id))
                    {
                        DebugUtils.Log("You are not in the channel: {0}", channel_id);
                        return;
                    }

                    ChannelReceiveHandler h = channels_[channel_id];
                    h(channel_id, mcast_msg);
                }
            }
        }


        public delegate void ChannelReceiveHandler(string channel_id, object body);

        private static readonly string kChannelId = "_channel";
        private static readonly string kJoin = "_join";
        private static readonly string kLeave = "_leave";
        private static readonly string kMulticastMsgType = "_multicast";

        private FunapiNetwork network_ = null;
        private FunEncoding encoding_;
        private object channel_lock_ = new object();
        private Dictionary<string, ChannelReceiveHandler> channels_ = new Dictionary<string, ChannelReceiveHandler>();
    }
}
