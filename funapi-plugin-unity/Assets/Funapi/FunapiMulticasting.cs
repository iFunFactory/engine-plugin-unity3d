// Copyright 2013 iFunFactory Inc. All Rights Reserved.
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
    public class FunapiMulticastClient : FunapiMulticast<object>
    {
        public FunapiMulticastClient (FunapiSession session, FunEncoding encoding, TransportProtocol protocol = TransportProtocol.kDefault)
            : base(session, encoding, protocol)
        {
        }
    }


    public class FunapiMulticast<T>
    {
        public FunapiMulticast (FunapiSession session, FunEncoding encoding, TransportProtocol protocol = TransportProtocol.kDefault)
        {
            FunDebug.Assert(session != null);

            session_ = session;
            encoding_ = encoding;
            protocol_ = protocol;

            session_.MulticastMessageCallback += onReceivedMessage;
        }

        ~FunapiMulticast ()
        {
            if (session_ != null)
            {
                session_.MulticastMessageCallback -= onReceivedMessage;
                session_ = null;
            }
        }

        public string sender
        {
            get { return sender_; }
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
                FunDebug.LogWarning("[Multicast] request a channel list but the session is not connected.");
                return;
            }

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast = new Dictionary<string, object>();
                mcast[kSender] = sender_;
                session_.SendMessage(kMulticastMsgType, mcast, protocol_);
            }
            else
            {
                FunMulticastMessage mcast = new FunMulticastMessage();
                mcast.sender = sender_;

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg, protocol_);
            }
        }

        public bool JoinChannel (string channel_id, ChannelMessage handler)
        {
            return JoinChannel(channel_id, "", handler);
        }

        public bool JoinChannel (string channel_id, string token, ChannelMessage handler)
        {
            if (!Connected)
            {
                FunDebug.LogWarning("[Multicast] request to join '{0}' channel but session is not connected.", channel_id);
                return false;
            }

            lock (channel_lock_)
            {
                if (channels_.ContainsKey(channel_id))
                {
                    FunDebug.LogWarning("[Multicast] request to join '{0}' channel but already joined that channel.", channel_id);
                    return false;
                }

                channels_.Add(channel_id, handler);
            }

            requestToJoin(channel_id, token);

            return true;
        }

        public bool LeaveChannel (string channel_id)
        {
            lock (channel_lock_)
            {
                if (channels_.ContainsKey(channel_id))
                {
                    channels_.Remove(channel_id);
                }
                else
                {
                    FunDebug.LogWarning("[Multicast] request to leave '{0}' channel but you are not in that channel.", channel_id);
                    return false;
                }
            }

            if (Connected)
            {
                requestToLeave(channel_id);
                onUserLeft(channel_id, sender_);
            }

            return true;
        }

        public virtual void LeaveAllChannels ()
        {
            lock (channel_lock_)
            {
                if (channels_.Count <= 0)
                    return;

                if (Connected)
                {
                    foreach (string channel_id in channels_.Keys)
                    {
                        requestToLeave(channel_id);
                        onUserLeft(channel_id, sender_);
                    }
                }

                channels_.Clear();
                tokens_.Clear();
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
        ///
        public bool SendToChannel (FunMulticastMessage mcast)
        {
            if (mcast == null)
                return false;

            FunDebug.Assert(!mcast.join);
            FunDebug.Assert(!mcast.leave);

            return sendToChannel(mcast.channel, mcast);
        }

        /// The 'channel_id' field is mandatory.
        /// The 'sender' must fill in the message.
        /// The message shouldn't include join and leave flags.
        ///
        public bool SendToChannel (object json)
        {
            if (json == null)
                return false;

            FunDebug.Assert(!json_helper_.HasField(json, kJoin));
            FunDebug.Assert(!json_helper_.HasField(json, kLeave));

            return sendToChannel(json_helper_.GetStringField(json, kChannelId), json);
        }


        void requestToJoin (string channel_id, string token)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast = new Dictionary<string, object>();
                mcast[kChannelId] = channel_id;
                mcast[kSender] = sender_;
                mcast[kJoin] = true;

                if (!string.IsNullOrEmpty(token))
                {
                    mcast[kToken] = token;
                }

                session_.SendMessage(kMulticastMsgType, mcast, protocol_);
            }
            else
            {
                FunMulticastMessage mcast = new FunMulticastMessage();
                mcast.channel = channel_id;
                mcast.sender = sender_;
                mcast.join = true;

                if (!string.IsNullOrEmpty(token))
                {
                    mcast.token = token;
                }

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg, protocol_);
            }

            if (!string.IsNullOrEmpty(token))
            {
                lock (token_lock_)
                {
                    tokens_[channel_id] = token;
                }
            }

            FunDebug.Log("[Multicast] requested to join '{0}' channel.", channel_id);
        }

        void requestToLeave (string channel_id)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast = new Dictionary<string, object>();
                mcast[kChannelId] = channel_id;
                mcast[kSender] = sender_;
                mcast[kLeave] = true;

                session_.SendMessage(kMulticastMsgType, mcast, protocol_);
            }
            else
            {
                FunMulticastMessage mcast = new FunMulticastMessage();
                mcast.channel = channel_id;
                mcast.sender = sender_;
                mcast.leave = true;

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg, protocol_);
            }

            FunDebug.Log("[Multicast] requested to leave '{0}' channel.", channel_id);
        }

        bool sendToChannel (string channel_id, object message)
        {
            if (message == null)
                return false;

            if (string.IsNullOrEmpty(channel_id))
            {
                FunDebug.LogWarning("[Multicast] can't send a message. invalid channel id.");
                return false;
            }

            if (!Connected)
            {
                FunDebug.LogWarning("[Multicast] can't send a message. session is not connected.");
                return false;
            }

            if (!InChannel(channel_id))
            {
                FunDebug.LogWarning("[Multicast] can't send a message. you aren't in '{0}' channel.", channel_id);
                return false;
            }

            if (encoding_ == FunEncoding.kJson)
            {
                json_helper_.SetStringField(message, kSender, sender_);

                session_.SendMessage(kMulticastMsgType, message, protocol_);
            }
            else
            {
                FunMulticastMessage mcast = message as FunMulticastMessage;
                mcast.sender = sender_;

                FunMessage fun_msg = FunapiMessage.CreateFunMessage(mcast, MessageType.multicast);
                session_.SendMessage(kMulticastMsgType, fun_msg, protocol_);
            }

            return true;
        }

        void onReceivedMessage (string msg_type, object body)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                onReceivedMessage(body);
            }
            else
            {
                FunMessage msg = body as FunMessage;
                FunMulticastMessage mcast = FunapiMessage.GetMessage<FunMulticastMessage>(msg, MessageType.multicast);
                if (mcast != null)
                {
                    onReceivedMessage(mcast);
                }
            }
        }

        void onReceivedMessage (object json)
        {
            string channel_id = "";
            string sender = "";

            if (json_helper_.HasField(json, kChannelList))
            {
                if (ChannelListCallback != null)
                {
                    object list = json_helper_.GetObject(json, kChannelList);
                    ChannelListCallback(list);
                }
                return;
            }

            if (json_helper_.HasField(json, kChannelId))
                channel_id = json_helper_.GetStringField(json, kChannelId);

            // If the channel id is not in the channel list, ignores it.
            if (!InChannel(channel_id))
                return;

            if (json_helper_.HasField(json, kErrorCode))
            {
                int error_code = (int)json_helper_.GetIntegerField(json, kErrorCode);
                onError(channel_id, (FunMulticastMessage.ErrorCode)error_code);
                return;
            }

            if (json_helper_.HasField(json, kSender))
                sender = json_helper_.GetStringField(json, kSender);

            if (json_helper_.HasField(json, kJoin))
            {
                if (json_helper_.GetBooleanField(json, kJoin))
                {
                    onUserJoined(channel_id, sender);
                    return;
                }
            }
            else if (json_helper_.HasField(json, kLeave))
            {
                if (json_helper_.GetBooleanField(json, kLeave))
                {
                    onUserLeft(channel_id, sender);
                    return;
                }
            }

            onMessageCallback(channel_id, sender, json);
        }

        void onReceivedMessage (FunMulticastMessage mcast)
        {
            string channel_id = "";
            string sender = "";

            if (mcast.channelSpecified)
                channel_id = mcast.channel;

            if (mcast.channels.Count > 0 || string.IsNullOrEmpty(channel_id))
            {
                if (ChannelListCallback != null)
                {
                    ChannelListCallback(mcast.channels);
                }
                return;
            }

            // If the channel id is not in the channel list, ignores it.
            if (!InChannel(channel_id))
                return;

            if (mcast.error_codeSpecified)
            {
                int error_code = (int)mcast.error_code;
                onError(channel_id, (FunMulticastMessage.ErrorCode)error_code);
                return;
            }

            if (mcast.senderSpecified)
                sender = mcast.sender;

            if (mcast.joinSpecified && mcast.join)
            {
                onUserJoined(channel_id, sender);
                return;
            }
            else if (mcast.leaveSpecified && mcast.leave)
            {
                onUserLeft(channel_id, sender);
                return;
            }

            onMessageCallback(channel_id, sender, mcast);
        }

        void onUserJoined (string channel_id, string user_id)
        {
            FunDebug.Log("[Multicast] '{0}' joined the '{1}' channel.", user_id, channel_id);

            if (JoinedCallback != null)
            {
                JoinedCallback(channel_id, user_id);
            }
        }

        void onUserLeft (string channel_id, string user_id)
        {
            FunDebug.Log("[Multicast] '{0}' left the '{1}' channel.", user_id, channel_id);

            if (user_id == sender_)
            {
                lock (channel_lock_)
                {
                    if (tokens_.ContainsKey(channel_id))
                    {
                        tokens_.Remove(channel_id);
                    }
                }
            }

            if (LeftCallback != null)
            {
                LeftCallback(channel_id, user_id);
            }
        }

        protected virtual void onMessageCallback (string channel_id, string user_id, object message)
        {
            lock (channel_lock_)
            {
                channels_[channel_id](channel_id, user_id, (T)message);
            }
        }

        void onError (string channel_id, FunMulticastMessage.ErrorCode code)
        {
            FunDebug.LogWarning("[Multicast] error occurred. channel:{0} error:{1}", channel_id, code);

            if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
            {
                // This error occurs when the server is closed.
                // If the session is connected, tries to rejoin the channel.

                if (Connected && InChannel(channel_id))
                {
                    string token = null;
                    lock (token_lock_)
                    {
                        if (tokens_.ContainsKey(channel_id))
                        {
                            token = tokens_[channel_id];
                        }
                    }

                    requestToJoin(channel_id, token);
                    return;
                }
            }

            if (code != FunMulticastMessage.ErrorCode.EC_ALREADY_JOINED)
            {
                lock (channel_lock_)
                {
                    if (channels_.ContainsKey(channel_id))
                    {
                        channels_.Remove(channel_id);
                    }
                }
            }

            if (ErrorCallback != null)
            {
                ErrorCallback(channel_id, code);
            }
        }


        const string kMulticastMsgType = "_multicast";
        const string kChannelList = "_channels";
        const string kChannelId = "_channel";
        const string kSender = "_sender";
        const string kJoin = "_join";
        const string kToken = "_token";
        const string kLeave = "_leave";
        const string kErrorCode = "_error_code";

        public delegate void ChannelMessage (string channel_id, string sender, T message);

        public event Action<object> ChannelListCallback;      // channel list
        public event Action<string, string> JoinedCallback;   // channel id, sender
        public event Action<string, string> LeftCallback;     // channel id, sender
        public event Action<string, FunMulticastMessage.ErrorCode> ErrorCallback;  // channel id, error code

        protected JsonAccessor json_helper_ = FunapiMessage.JsonHelper;
        protected TransportProtocol protocol_;
        protected FunEncoding encoding_;
        protected string sender_ = "";

        FunapiSession session_ = null;
        object token_lock_ = new object();
        Dictionary<string, string> tokens_ = new Dictionary<string, string>();
        object channel_lock_ = new object();
        Dictionary<string, ChannelMessage> channels_ = new Dictionary<string, ChannelMessage>();
    }
}
