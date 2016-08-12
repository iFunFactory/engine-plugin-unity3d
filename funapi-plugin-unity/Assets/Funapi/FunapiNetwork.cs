// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Text;

// protobuf
using funapi.network.fun_message;


namespace Fun
{
    // Driver to use Funapi network plugin.
    public class FunapiNetwork : FunapiUpdater
    {
        public FunapiNetwork (bool session_reliability = false)
        {
            state_ = State.kUnknown;
            session_reliability_ = session_reliability;
            response_timer_ = 0f;
            ResponseTimeout = 0f;

            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            message_handlers_[kMaintenanceMessageType] = this.OnMaintenanceMessage;

            InitSession();
        }

        public bool SessionReliability
        {
            get { return session_reliability_; }
        }

        public bool SequenceNumberValidation
        {
            set; private get;
        }

        public float ResponseTimeout
        {
            get; set;
        }

        public FunEncoding GetEncoding (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return FunEncoding.kNone;

            return transport.Encoding;
        }

        private void OnMaintenanceMessage (string msg_type, object body)
        {
            if (MaintenanceCallback != null)
            {
                MaintenanceCallback(msg_type, body);
            }
        }


        //---------------------------------------------------------------------
        // Protocol-related functions
        //---------------------------------------------------------------------
        public void SetDefaultProtocol (TransportProtocol protocol)
        {
            FunDebug.Assert(protocol != TransportProtocol.kDefault);

            default_protocol_ = protocol;
            FunDebug.Log("The default protocol is '{0}'", protocol);
        }

        public TransportProtocol GetDefaultProtocol()
        {
            return default_protocol_;
        }

        // Set message protocol
        public void SetMessageProtocol (TransportProtocol protocol, string msg_type)
        {
            FunDebug.Assert(protocol != TransportProtocol.kDefault);
            message_protocols_[msg_type] = protocol;
        }

        public TransportProtocol GetMessageProtocol (string msg_type)
        {
            if (message_protocols_.ContainsKey(msg_type))
                return message_protocols_[msg_type];

            return default_protocol_;
        }


        //---------------------------------------------------------------------
        // Connect-related functions
        //---------------------------------------------------------------------
        // For http transport, Pass a HostHttp instead of HostAddr.
        public bool Connect (TransportProtocol protocol, FunEncoding type, HostAddr addr)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.LogWarning("Connect - Can't find a {0} transport.", protocol);
                return false;
            }

            transport.Encoding = type;
            transport.ResetAddress(addr);

            transport.Connect();
            return true;
        }

        public bool Reconnect (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.LogWarning("Reconnect - Can't find a {0} transport.", protocol);
                return false;
            }

            transport.Reconnect();
            return true;
        }

        public bool Redirect (TransportProtocol protocol, HostAddr addr, bool keep_session_id = false)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.LogWarning("Redirect - Can't find a {0} transport.", protocol);
                return false;
            }

            if (!keep_session_id)
            {
                InitSession();
            }

            transport.Redirect(addr);
            return true;
        }

        // Starts FunapiNetwork
        public void Start()
        {
            if (Started)
            {
                FunDebug.LogWarning("FunapiNetwork.Start() called, but the network already started. This request has ignored.\n{0}",
                                      "If you want to reconnect, call FunapiNetwork.Stop() first and wait for it to stop.");
                return;
            }

            lock (state_lock_)
            {
                state_ = State.kStarted;
            }

            createUpdater();

            event_list.Add (delegate {
                FunDebug.Log("Starting a network module.");

                lock (transports_lock_)
                {
                    foreach (FunapiTransport transport in transports_.Values)
                    {
                        StartTransport(transport);
                    }
                }
            });
        }

        // Stops FunapiNetwork
        public void Stop (bool clear_all = true, bool force_stop = false)
        {
            stop_with_clear_ = clear_all;

            // Checks transport state
            if (!force_stop)
            {
                lock (transports_lock_)
                {
                    foreach (FunapiTransport transport in transports_.Values)
                    {
                        if (transport.IsConnecting)
                        {
                            state_ = State.kWaitForStop;
                            FunDebug.DebugLog("Wait the connection is complete...");
                            return;
                        }
                    }
                }
            }

            // Waits for unsent messages.
            // If the response time is exceeded, don't check the remaining unsent packets.
            if (response_timer_ <= 0f || response_timer_ < ResponseTimeout)
            {
                lock (transports_lock_)
                {
                    foreach (FunapiTransport transport in transports_.Values)
                    {
                        if (transport.Protocol == TransportProtocol.kTcp &&
                            transport.Started && transport.HasUnsentMessages)
                        {
                            lock (state_lock_)
                            {
                                state_ = State.kWaitForStop;
                                FunDebug.Log("{0} Stop waiting for send unsent messages...",
                                               transport.str_protocol);
                                return;
                            }
                        }
                    }
                }
            }

            FunDebug.Log("Stopping a network module.");

            releaseUpdater();

            // Stops all transport
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    StopTransport(transport);
                }
            }

            // Closes session
            if (stop_with_clear_)
            {
                CloseSession();

                lock (state_lock_)
                {
                    state_ = State.kUnknown;
                }
            }
        }

        public bool Started
        {
            get
            {
                lock (state_lock_)
                {
                    return state_ != State.kUnknown && state_ != State.kStopped;
                }
            }
        }

        public bool Connected
        {
            get
            {
                lock (state_lock_)
                {
                    return state_ == State.kConnected;
                }
            }
        }

        private void OnConnectTimeout (TransportProtocol protocol)
        {
            StopTransport(protocol);
        }

        // Updates FunapiNetwork
        protected override bool onUpdate (float deltaTime)
        {
            if (!base.onUpdate(deltaTime))
                return false;

            lock (transports_lock_)
            {
                if (transports_.Count > 0)
                {
                    foreach (FunapiTransport transport in transports_.Values)
                    {
                        if (transport != null)
                            transport.Update(deltaTime);
                    }
                }
            }

            lock (message_lock_)
            {
                if (message_buffer_.Count > 0)
                {
                    FunDebug.DebugLog("Update messages. count: {0}", message_buffer_.Count);

                    foreach (FunapiMessage message in message_buffer_)
                    {
                        ProcessMessage(message);
                    }

                    message_buffer_.Clear();
                    response_timer_ = 0f;
                }
                else if (state_ == State.kConnected && ResponseTimeout > 0f)
                {
                    response_timer_ += deltaTime;
                    if (response_timer_ >= ResponseTimeout)
                    {
                        FunDebug.LogWarning("Response timeout. disconnect to server...");
                        Stop(!session_reliability_);
                        return true;
                    }
                }
            }

            lock (state_lock_)
            {
                if (state_ == State.kUnknown || state_ == State.kStopped)
                {
                    lock (message_lock_)
                    {
                        if (message_buffer_.Count > 0)
                            message_buffer_.Clear();
                    }

                    lock (expected_reply_lock)
                    {
                        if (expected_replies_.Count > 0)
                            expected_replies_.Clear();
                    }
                    return true;
                }

                if (state_ == State.kWaitForStop)
                {
                    Stop(stop_with_clear_);
                    return true;
                }
            }

            lock (expected_reply_lock)
            {
                if (expected_replies_.Count > 0)
                {
                    List<string> remove_list = new List<string>();
                    Dictionary<string, List<ExpectedReply>> exp_list = expected_replies_;
                    expected_replies_ = new Dictionary<string, List<ExpectedReply>>();

                    foreach (var item in exp_list)
                    {
                        int remove_count = 0;
                        foreach (ExpectedReply er in item.Value)
                        {
                            er.reply_timeout -= deltaTime;
                            if (er.reply_timeout <= 0f)
                            {
                                FunDebug.Log("'{0}' message waiting time has been exceeded.", er.reply_type);
                                er.timeout_callback(er.msg_type);
                                ++remove_count;
                            }
                        }

                        if (remove_count > 0)
                        {
                            if (item.Value.Count <= remove_count)
                                remove_list.Add(item.Key);
                            else
                                item.Value.RemoveRange(0, remove_count);
                        }
                    }

                    if (remove_list.Count > 0)
                    {
                        foreach (string key in remove_list)
                        {
                            exp_list.Remove(key);
                        }
                    }

                    if (exp_list.Count > 0)
                    {
                        Dictionary<string, List<ExpectedReply>> added_list = expected_replies_;
                        expected_replies_ = exp_list;

                        if (added_list.Count > 0)
                        {
                            foreach (var item in added_list)
                            {
                                if (expected_replies_.ContainsKey(item.Key))
                                    expected_replies_[item.Key].AddRange(item.Value);
                                else
                                    expected_replies_.Add(item.Key, item.Value);
                            }
                            added_list = null;
                        }
                    }
                }
            }

            return true;
        }

        protected override void onQuit ()
        {
            Stop(true, true);
        }


        //---------------------------------------------------------------------
        // FunapiTransport-related functions
        //---------------------------------------------------------------------
        public void AttachTransport (FunapiTransport transport)
        {
            FunDebug.Assert(transport != null);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(transport.Protocol))
                {
                    StringBuilder strlog = new StringBuilder();
                    strlog.AppendFormat("AttachTransport - transport of '{0}' type already exists.", transport.Protocol);
                    strlog.Append(" You should call DetachTransport first.");
                    FunDebug.LogWarning(strlog.ToString());
                    return;
                }

                // Callback functions
                transport.ConnectTimeoutCallback += OnConnectTimeout;
                transport.StartedInternalCallback += OnTransportStarted;
                transport.StoppedCallback += OnTransportStopped;
                transport.ConnectFailureCallback += OnTransportConnectFailure;
                transport.DisconnectedCallback += OnTransportDisconnected;
                transport.ReceivedCallback += OnTransportReceived;
                transport.MessageFailureCallback += OnTransportFailure;

                transports_[transport.Protocol] = transport;

                if (default_protocol_ == TransportProtocol.kDefault)
                {
                    SetDefaultProtocol(transport.Protocol);
                }

                if (Started)
                {
                    StartTransport(transport);
                }

                FunDebug.Log("{0} transport attached.", transport.Protocol);
            }
        }

        public void DetachTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                {
                    FunapiTransport transport = transports_[protocol];
                    if (transport != null && transport.Started)
                        StopTransport(transport);

                    transports_.Remove(protocol);
                    FunDebug.Log("{0} transport detached.", protocol);

                    if (protocol == default_protocol_)
                    {
                        FunapiTransport other = FindOtherTransport(transport.Protocol);
                        if (other != null)
                        {
                            SetDefaultProtocol(other.Protocol);
                        }
                        else
                        {
                            default_protocol_ = TransportProtocol.kDefault;
                            FunDebug.LogWarning("DetachTransport - Deletes default protocol. You need to set default protocol up.");
                        }
                    }
                }
                else
                {
                    FunDebug.LogWarning("DetachTransport - Can't find a transport of '{0}' type.", protocol);
                }
            }
        }

        public void StartTransport (TransportProtocol protocol)
        {
            StartTransport(GetTransport(protocol));
        }

        internal void StartTransport (FunapiTransport transport)
        {
            if (transport == null)
                return;

            FunDebug.Log("Starting {0} transport.", transport.Protocol);

            lock (state_lock_)
            {
                if (state_ == State.kUnknown)
                {
                    Start();
                    return;
                }
            }

            if (transport.Protocol == TransportProtocol.kHttp)
            {
                ((FunapiHttpTransport)transport).mono = mono;
            }

            transport.Start();
        }

        public void StopTransport (TransportProtocol protocol)
        {
            StopTransport(GetTransport(protocol));
        }

        private void StopTransport (FunapiTransport transport)
        {
            if (transport == null)
                return;

            FunDebug.Log("Stopping {0} transport.", transport.Protocol);

            transport.Stop();
        }

        private void SetTransportStarted (FunapiTransport transport, bool send_unsent = true)
        {
            if (transport == null)
                return;

            transport.OnStarted(session_id_);

            if (send_unsent && unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }
        }

        private void CheckTransportConnection (TransportProtocol protocol)
        {
            lock (state_lock_)
            {
                if (state_ == State.kStopped)
                    return;

                if (state_ == State.kWaitForSessionId && protocol == session_protocol_)
                {
                    FunapiTransport other = FindOtherTransport(protocol);
                    if (other != null)
                    {
                        other.state = FunapiTransport.State.kWaitForSessionId;
                        SendEmptyMessage(other.Protocol);
                    }
                    else
                    {
                        state_ = State.kStarted;
                    }
                }

                lock (transports_lock_)
                {
                    bool all_stopped = true;
                    foreach (FunapiTransport t in transports_.Values)
                    {
                        if (t.IsReconnecting || t.Started)
                        {
                            all_stopped = false;
                            break;
                        }
                    }

                    if (all_stopped)
                    {
                        state_ = State.kStopped;
                        OnStoppedAllTransportCallback();
                    }
                }
            }
        }

        public bool HasTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return true;
            }

            return false;
        }

        public FunapiTransport GetTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

            return null;
        }

        private FunapiTransport FindOtherTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (protocol == TransportProtocol.kDefault || transports_.Count <= 0)
                    return null;

                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.Protocol != protocol && transport.Started)
                    {
                        return transport;
                    }
                }
            }

            return null;
        }

        private void OnTransportStarted (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            FunDebug.Assert(transport != null);
            FunDebug.Log("{0} Transport started.", protocol);

            lock (state_lock_)
            {
                if (session_id_.Length > 0)
                {
                    state_ = State.kConnected;
                    response_timer_ = 0f;

                    if (session_reliability_ && protocol == TransportProtocol.kTcp && seq_recvd_ != 0)
                    {
                        transport.state = FunapiTransport.State.kWaitForAck;
                        SendAck(transport, seq_recvd_ + 1);
                    }
                    else
                    {
                        SetTransportStarted(transport);
                    }
                }
                else if (state_ == State.kStarted || state_ == State.kStopped)
                {
                    state_ = State.kWaitForSessionId;
                    transport.state = FunapiTransport.State.kWaitForSessionId;

                    // To get a session id
                    SendEmptyMessage(protocol);
                }
                else if (state_ == State.kWaitForSessionId)
                {
                    transport.state = FunapiTransport.State.kWaitForSessionId;
                }
            }
        }

        private void OnTransportStopped (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            FunDebug.Assert(transport != null);
            FunDebug.Log("{0} Transport Stopped.", protocol);

            CheckTransportConnection(protocol);
        }

        private void OnTransportConnectFailure (TransportProtocol protocol)
        {
            FunDebug.Log("'{0}' transport connect failed.", protocol);

            CheckTransportConnection(protocol);

            if (TransportConnectFailedCallback != null)
                TransportConnectFailedCallback(protocol);
        }

        private void OnTransportDisconnected (TransportProtocol protocol)
        {
            FunDebug.Log("'{0}' transport disconnected.", protocol);

            CheckTransportConnection(protocol);

            if (TransportDisconnectedCallback != null)
                TransportDisconnectedCallback(protocol);
        }

        private void OnStoppedAllTransportCallback ()
        {
            FunDebug.Log("All transports has stopped.");

            if (StoppedAllTransportCallback != null)
                StoppedAllTransportCallback();
        }

        private void OnTransportReceived (FunapiMessage message)
        {
            FunDebug.DebugLog("OnTransportReceived invoked.");
            last_received_ = DateTime.Now;

            lock (message_lock_)
            {
                message_buffer_.Add(message);
            }
        }

        private void OnTransportFailure (TransportProtocol protocol, FunapiMessage fun_msg)
        {
            if (fun_msg == null || fun_msg.reply == null)
                return;

            ExpectedReply reply = fun_msg.reply as ExpectedReply;
            DeleteExpectedReply(reply.reply_type);
        }


        //---------------------------------------------------------------------
        // Message-related functions
        //---------------------------------------------------------------------
        public void RegisterHandler(string type, MessageEventHandler handler)
        {
            FunDebug.DebugLog("New handler for message type '{0}'", type);
            message_handlers_[type] = handler;
        }

        public void RegisterHandlerWithProtocol(string type, TransportProtocol protocol, MessageEventHandler handler)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                RegisterHandler(type, handler);
                return;
            }

            FunDebug.DebugLog("New handler for and message type '{0}' of '{1}' protocol.", type, protocol);
            message_protocols_[type] = protocol;
            message_handlers_[type] = handler;
        }

        public void SendMessage (MessageType msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 string expected_reply_type = null, float expected_reply_time = 0f,
                                 TimeoutEventHandler onReplyMissed = null)
        {
            string _msg_type = MessageTable.Lookup(msg_type);
            SendMessage(_msg_type, message, protocol, expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage (MessageType msg_type, object message,
                                 string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            string _msg_type = MessageTable.Lookup(msg_type);
            SendMessage(_msg_type, message, GetMessageProtocol(_msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage (string msg_type, object message,
                                 string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, message, GetMessageProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage (string msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 string expected_reply_type = null, float expected_reply_time = 0f,
                                 TimeoutEventHandler onReplyMissed = null)
        {
            if (protocol == TransportProtocol.kDefault)
                protocol = GetMessageProtocol(msg_type);

            bool transport_reliability = (protocol == TransportProtocol.kTcp && session_reliability_);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                FunDebug.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && transport.state == FunapiTransport.State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                FunapiMessage fun_msg = null;

                bool sending_sequence = SequenceNumberValidation &&
                    (protocol == TransportProtocol.kTcp || protocol == TransportProtocol.kHttp);

                if (transport.Encoding == FunEncoding.kJson)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, FunapiMessage.JsonHelper.Clone(message));

                    // Encodes a messsage type
                    FunapiMessage.JsonHelper.SetStringField(fun_msg.message, kMsgTypeBodyField, msg_type);

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        FunapiMessage.JsonHelper.SetStringField(fun_msg.message, kSessionIdBodyField, session_id_);
                    }

                    if (transport_reliability || sending_sequence)
                    {
                        UInt32 seq = GetNextSeq(protocol);
                        FunapiMessage.JsonHelper.SetIntegerField(fun_msg.message, kSeqNumberField, seq);

                        if (transport_reliability)
                            send_queue_.Enqueue(fun_msg);

                        FunDebug.DebugLog("{0} send message - msgtype:{1} seq:{2}", protocol, msg_type, seq);
                    }
                    else
                    {
                        FunDebug.DebugLog("{0} send message - msgtype:{1}", protocol, msg_type);
                    }
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, message);

                    FunMessage pbuf = fun_msg.message as FunMessage;
                    pbuf.msgtype = msg_type;

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        pbuf.sid = session_id_;
                    }

                    if (transport_reliability || sending_sequence)
                    {
                        pbuf.seq = GetNextSeq(protocol);

                        if (transport_reliability)
                            send_queue_.Enqueue(fun_msg);

                        FunDebug.DebugLog("{0} send message - msgtype:{1} seq:{2}",
                                            protocol, msg_type, pbuf.seq);
                    }
                    else
                    {
                        FunDebug.DebugLog("{0} send message - msgtype:{1}", protocol, msg_type);
                    }
                }

                if (fun_msg != null && expected_reply_type != null && expected_reply_type.Length > 0)
                {
                    fun_msg.reply = new ExpectedReply(msg_type, expected_reply_type, expected_reply_time, onReplyMissed);
                    AddExpectedReply(fun_msg);
                }

                transport.SendMessage(fun_msg);
            }
            else if (transport != null &&
                     (transport_reliability || transport.state == FunapiTransport.State.kEstablished))
            {
                FunapiMessage fun_msg = null;

                if (transport.Encoding == FunEncoding.kJson)
                {
                    if (transport == null)
                        fun_msg = new FunapiMessage(protocol, msg_type, message);
                    else
                        fun_msg = new FunapiMessage(protocol, msg_type,
                                                    FunapiMessage.JsonHelper.Clone(message));
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, message);
                }

                if (fun_msg != null)
                {
                    if (expected_reply_type != null && expected_reply_type.Length > 0)
                        fun_msg.reply = new ExpectedReply(msg_type, expected_reply_type, expected_reply_time, onReplyMissed);

                    unsent_queue_.Enqueue(fun_msg);
                    FunDebug.Log("SendMessage - '{0}' message queued.", msg_type);
                }
            }
            else
            {
                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("SendMessage - '{0}' message skipped.", msg_type);
                if (transport == null)
                    strlog.AppendFormat(" There's no '{0}' transport.", protocol);
                else if (transport.state != FunapiTransport.State.kEstablished)
                    strlog.AppendFormat(" Transport's state is '{0}'.", transport.state);

                FunDebug.Log(strlog.ToString());
            }
        }

        private void AddExpectedReply (FunapiMessage fun_msg)
        {
            ExpectedReply er = fun_msg.reply as ExpectedReply;
            if (er == null)
                return;

            lock (expected_reply_lock)
            {
                if (!expected_replies_.ContainsKey(er.reply_type))
                    expected_replies_[er.reply_type] = new List<ExpectedReply>();

                expected_replies_[er.reply_type].Add(er);
            }

            FunDebug.Log("Adds expected reply message - {0} > {1} ({2})",
                         fun_msg.msg_type, er.reply_type, er.reply_timeout);
        }

        private void DeleteExpectedReply (string reply_type)
        {
            lock (expected_reply_lock)
            {
                if (expected_replies_.ContainsKey(reply_type))
                {
                    List<ExpectedReply> list = expected_replies_[reply_type];
                    if (list.Count > 0)
                    {
                        list.RemoveAt(0);
                        FunDebug.Log("Deletes expected reply message - {0}", reply_type);
                    }

                    if (list.Count <= 0)
                        expected_replies_.Remove(reply_type);
                }
            }
        }

        private void ProcessMessage (FunapiMessage msg)
        {
            FunapiTransport transport = GetTransport(msg.protocol);
            if (transport == null)
                return;

            object message = msg.message;
            if (message == null)
            {
                FunDebug.Log("ProcessMessage - '{0}' message is null.", msg.msg_type);
                return;
            }

            string msg_type = msg.msg_type;
            string session_id = "";

            if (transport.Encoding == FunEncoding.kJson)
            {
                try
                {
                    FunDebug.Assert(FunapiMessage.JsonHelper.GetStringField(message, kSessionIdBodyField) is string);
                    string session_id_node = FunapiMessage.JsonHelper.GetStringField(message, kSessionIdBodyField) as string;
                    session_id = session_id_node;
                    FunapiMessage.JsonHelper.RemoveStringField(message, kSessionIdBodyField);

                    PrepareSession(session_id);

                    if (session_reliability_ && msg.protocol == TransportProtocol.kTcp)
                    {
                        if (FunapiMessage.JsonHelper.HasField(message, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)FunapiMessage.JsonHelper.GetIntegerField(message, kAckNumberField);
                            OnAckReceived(transport, ack);
                            // Does not support piggybacking.
                            FunDebug.Assert(!FunapiMessage.JsonHelper.HasField(message, kMsgTypeBodyField));
                            return;
                        }

                        if (FunapiMessage.JsonHelper.HasField(message, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)FunapiMessage.JsonHelper.GetIntegerField(message, kSeqNumberField);
                            if (!OnSeqReceived(transport, seq))
                                return;
                            FunapiMessage.JsonHelper.RemoveStringField(message, kSeqNumberField);
                        }
                    }
                }
                catch (Exception e)
                {
                    FunDebug.Log("Failure in ProcessMessage: {0}", e.ToString());
                    StopTransport(transport);
                    return;
                }

                if (msg_type.Length > 0)
                {
                    DeleteExpectedReply(msg_type);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, message);
                }
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage funmsg = message as FunMessage;

                try
                {
                    session_id = funmsg.sid;
                    PrepareSession(session_id);

                    if (session_reliability_ && msg.protocol == TransportProtocol.kTcp)
                    {
                        if (funmsg.ackSpecified)
                        {
                            OnAckReceived(transport, funmsg.ack);
                            // Does not support piggybacking.
                            return;
                        }

                        if (funmsg.seqSpecified)
                        {
                            if (!OnSeqReceived(transport, funmsg.seq))
                                return;
                        }
                    }
                }
                catch (Exception e)
                {
                    FunDebug.Log("Failure in ProcessMessage: {0}", e.ToString());
                    StopTransport(transport);
                    return;
                }

                if (msg_type.Length > 0)
                {
                    DeleteExpectedReply(msg_type);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, funmsg);
                }
            }
            else
            {
                FunDebug.Log("Invalid message type. type: {0}", transport.Encoding);
                FunDebug.Assert(false);
                return;
            }

            if (!message_handlers_.ContainsKey(msg_type))
            {
                if (session_id_.Length > 0 && transport.state == FunapiTransport.State.kWaitForAck)
                {
                    SetTransportStarted(transport);
                }

                if (msg_type.Length > 0)
                {
                    FunDebug.Log("No handler for message '{0}'. Ignoring.", msg_type);
                }
            }
        }

        private void SendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            FunDebug.Log("SendUnsentMessages - {0} unsent messages.", unsent_queue_.Count);

            foreach (FunapiMessage msg in unsent_queue_)
            {
                FunapiTransport transport = GetTransport(msg.protocol);
                if (transport == null || transport.state != FunapiTransport.State.kEstablished)
                {
                    FunDebug.Log("SendUnsentMessages - {0} isn't a valid transport. Message skipped.", msg.protocol);
                    continue;
                }

                bool transport_reliability = (transport.Protocol == TransportProtocol.kTcp && session_reliability_);
                bool sending_sequence = SequenceNumberValidation &&
                    (transport.Protocol == TransportProtocol.kTcp || transport.Protocol == TransportProtocol.kHttp);

                if (transport.Encoding == FunEncoding.kJson)
                {
                    object json = msg.message;

                    // Encodes a messsage type
                    FunapiMessage.JsonHelper.SetStringField(json, kMsgTypeBodyField, msg.msg_type);

                    if (session_id_.Length > 0)
                        FunapiMessage.JsonHelper.SetStringField(json, kSessionIdBodyField, session_id_);

                    if (transport_reliability || sending_sequence)
                    {
                        UInt32 seq = GetNextSeq(transport.Protocol);
                        FunapiMessage.JsonHelper.SetIntegerField(json, kSeqNumberField, seq);

                        if (transport_reliability)
                            send_queue_.Enqueue(msg);

                        FunDebug.Log("{0} send unsent message - msgtype:{1} seq:{2}",
                                       transport.Protocol, msg.msg_type, seq);
                    }
                    else
                    {
                        FunDebug.Log("{0} send unsent message - msgtype:{1}",
                                       transport.Protocol, msg.msg_type);
                    }
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    FunMessage pbuf = msg.message as FunMessage;
                    pbuf.msgtype = msg.msg_type;

                    if (session_id_.Length > 0)
                        pbuf.sid = session_id_;

                    if (transport_reliability || sending_sequence)
                    {
                        pbuf.seq = GetNextSeq(transport.Protocol);

                        if (transport_reliability)
                            send_queue_.Enqueue(msg);

                        FunDebug.Log("{0} send unsent message - msgtype:{1} seq:{2}",
                                       transport.Protocol, msg.msg_type, pbuf.seq);
                    }
                    else
                    {
                        FunDebug.Log("{0} send unsent message - msgtype:{1}",
                                       transport.Protocol, msg.msg_type);
                    }
                }

                if (msg.reply != null)
                    AddExpectedReply(msg);

                transport.SendMessage(msg);
            }

            unsent_queue_.Clear();
        }

        private bool SeqLess (UInt32 x, UInt32 y)
        {
            // 아래 참고
            //  - http://en.wikipedia.org/wiki/Serial_number_arithmetic
            //  - RFC 1982
            return (Int32)(y - x) > 0;
        }

        private void SendAck (FunapiTransport transport, UInt32 ack)
        {
            FunDebug.Assert(session_reliability_);
            if (transport == null)
            {
                FunDebug.Log("SendAck - transport is null.");
                return;
            }

            if (state_ != State.kConnected)
                return;

            FunDebug.DebugLog("{0} send ack message - ack:{1}", transport.Protocol, ack);

            if (transport.Encoding == FunEncoding.kJson)
            {
                object ack_msg = FunapiMessage.Deserialize("{}");
                FunapiMessage.JsonHelper.SetStringField(ack_msg, kSessionIdBodyField, session_id_);
                FunapiMessage.JsonHelper.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport.SendMessage(new FunapiMessage(transport.Protocol, "", ack_msg));
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.sid = session_id_;
                ack_msg.ack = ack;
                transport.SendMessage(new FunapiMessage(transport.Protocol, "", ack_msg));
            }
        }

        private void SendEmptyMessage (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                FunDebug.Log("SendEmptyMessage - transport is null.");
                return;
            }

            session_protocol_ = protocol;
            FunDebug.DebugLog("{0} send empty message", transport.str_protocol);

            if (transport.Encoding == FunEncoding.kJson)
            {
                object msg = FunapiMessage.Deserialize("{}");
                transport.SendMessage(new FunapiMessage(transport.Protocol, "", msg));
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = new FunMessage();
                transport.SendMessage(new FunapiMessage(transport.Protocol, "", msg));
            }
        }

        private bool OnSeqReceived (FunapiTransport transport, UInt32 seq)
        {
            if (transport == null)
            {
                FunDebug.LogWarning("OnSeqReceived - transport is null.");
                return false;
            }

            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                if (!SeqLess(seq_recvd_, seq))
                {
                    FunDebug.Log("Last sequence number is {0} but {1} received. Skipping message.", seq_recvd_, seq);
                    return false;
                }
                else if (seq != seq_recvd_ + 1)
                {
                    FunDebug.LogError("Received wrong sequence number {0}. {1} expected.", seq, seq_recvd_ + 1);
                    StopTransport(transport);
                    return false;
                }
            }

            seq_recvd_ = seq;
            SendAck(transport, seq_recvd_ + 1);

            return true;
        }

        private void OnAckReceived (FunapiTransport transport, UInt32 ack)
        {
            if (transport == null)
            {
                FunDebug.LogWarning("OnAckReceived - transport is null.");
                return;
            }

            if (state_ != State.kConnected)
                return;

            FunDebug.DebugLog("received ack message - ack:{0}", ack);

            UInt32 seq = 0;
            while (send_queue_.Count > 0)
            {
                FunapiMessage last_msg = send_queue_.Peek();
                if (transport.Encoding == FunEncoding.kJson)
                {
                    seq = (UInt32)FunapiMessage.JsonHelper.GetIntegerField(last_msg.message, kSeqNumberField);
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    seq = (last_msg.message as FunMessage).seq;
                }
                else
                {
                    FunDebug.Assert(false);
                    seq = 0;
                }

                if (SeqLess(seq, ack))
                {
                    send_queue_.Dequeue();
                }
                else
                {
                    break;
                }
            }

            if (transport.state == FunapiTransport.State.kWaitForAck)
            {
                if (send_queue_.Count > 0)
                {
                    foreach (FunapiMessage msg in send_queue_)
                    {
                        if (transport.Encoding == FunEncoding.kJson)
                        {
                            seq = (UInt32)FunapiMessage.JsonHelper.GetIntegerField(msg.message, kSeqNumberField);
                        }
                        else if (transport.Encoding == FunEncoding.kProtobuf)
                        {
                            seq = (msg.message as FunMessage).seq;
                        }
                        else
                        {
                            FunDebug.Assert(false);
                            seq = 0;
                        }

                        if (seq == ack || SeqLess(ack, seq))
                        {
                            transport.SendMessage(msg);
                        }
                        else
                        {
                            FunDebug.LogWarning("OnAckReceived({0}) - wrong sequence number {1}. ", ack, seq);
                        }
                    }

                    FunDebug.Log("Resend {0} messages.", send_queue_.Count);
                }

                SetTransportStarted(transport);
            }
        }


        //---------------------------------------------------------------------
        // Session-related functions
        //---------------------------------------------------------------------
        private void InitSession()
        {
            session_id_ = "";

            if (session_reliability_)
            {
                seq_recvd_ = 0;
                send_queue_.Clear();
                first_receiving_ = true;
            }

            tcp_seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            http_seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
        }

        internal UInt32 GetNextSeq (TransportProtocol protocol)
        {
            if (protocol == TransportProtocol.kTcp) {
                return ++tcp_seq_;
            }
            else if (protocol == TransportProtocol.kHttp) {
                return ++http_seq_;
            }

            FunDebug.Assert(false);
            return 0;
        }

        private void PrepareSession(string session_id)
        {
            if (session_id_.Length == 0)
            {
                FunDebug.Log("New session id: {0}", session_id);
                OpenSession(session_id);
            }

            if (session_id_ != session_id)
            {
                FunDebug.Log("Session id changed: {0} => {1}", session_id_, session_id);

                CloseSession();
                OpenSession(session_id);
            }
        }

        private void OpenSession(string session_id)
        {
            FunDebug.Assert(session_id_.Length == 0);

            lock (state_lock_)
            {
                state_ = State.kConnected;
                response_timer_ = 0f;
            }

            session_id_ = session_id;
            first_receiving_ = true;

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.state == FunapiTransport.State.kWaitForSessionId)
                    {
                        SetTransportStarted(transport, false);
                    }
                }
            }

            if (OnSessionInitiated != null)
            {
                OnSessionInitiated(session_id_);
            }

            if (unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }
        }

        private void CloseSession()
        {
            lock (state_lock_)
            {
                state_ = State.kUnknown;
            }

            if (session_id_.Length == 0)
                return;

            InitSession();

            if (OnSessionClosed != null)
            {
                OnSessionClosed();
            }
        }

        private void OnNewSession (string msg_type, object body)
        {
            // ignore.
        }

        private void OnSessionTimedout (string msg_type, object body)
        {
            FunDebug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");

            Stop();
        }


        //---------------------------------------------------------------------
        // Ping-related functions
        //---------------------------------------------------------------------
        // This function is no longer used.
        [System.Obsolete("This will be deprecated Oct 2016. You can use FunapiTransport.EnablePing property.")]
        public bool EnablePing
        {
            get
            {
                FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
                if (transport != null)
                    return transport.EnablePing;

                return false;
            }
            set
            {
                FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
                if (transport != null)
                    transport.EnablePing = value;
            }
        }

        // This function is no longer used.
        [System.Obsolete("This will be deprecated Oct 2016. You can use FunapiTransport.PingTime property.")]
        public int PingTime
        {
            get
            {
                FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
                if (transport != null)
                    return transport.PingTime;

                return 0;
            }
        }


        //---------------------------------------------------------------------
        //---------------------------------------------------------------------
        public ErrorCode LastErrorCode (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return ErrorCode.kNone;

            return transport.LastErrorCode;
        }

        public string LastErrorMessage (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return "";

            return transport.LastErrorMessage;
        }


        // Expected-reply-related class
        class ExpectedReply
        {
            public ExpectedReply (string msg_type, string reply_type,
                                  float reply_timeout, TimeoutEventHandler callback)
            {
                this.msg_type = msg_type;
                this.reply_type = reply_type;
                this.reply_timeout = reply_timeout;
                this.timeout_callback = callback;
            }

            public string msg_type;
            public string reply_type;
            public float reply_timeout;
            public TimeoutEventHandler timeout_callback;
        }



        // Status
        public enum State
        {
            kUnknown = 0,
            kStarted,
            kConnected,
            kWaitForSessionId,
            kWaitForStop,
            kStopped
        };

        // Delegates
        public delegate void MessageEventHandler(string msg_type, object body);
        public delegate void SessionInitHandler(string session_id);
        public delegate void SessionCloseHandler();
        public delegate void NotifyHandler();

        // Funapi message-related events.
        public event SessionInitHandler OnSessionInitiated;
        public event SessionCloseHandler OnSessionClosed;
        public event MessageEventHandler MaintenanceCallback;
        public event NotifyHandler StoppedAllTransportCallback;
        public event TransportEventHandler TransportConnectFailedCallback;
        public event TransportEventHandler TransportDisconnectedCallback;

        // Funapi message-related constants.
        private static readonly float kFunapiSessionTimeout = 3600.0f;
        private static readonly string kMsgTypeBodyField = "_msgtype";
        private static readonly string kSessionIdBodyField = "_sid";
        private static readonly string kSeqNumberField = "_seq";
        private static readonly string kAckNumberField = "_ack";
        private static readonly string kNewSessionMessageType = "_session_opened";
        private static readonly string kSessionClosedMessageType = "_session_closed";
        private static readonly string kMaintenanceMessageType = "_maintenance";

        // Member variables.
        private State state_;
        private string session_id_ = "";
        private object state_lock_ = new object();
        private float response_timer_ = 0f;
        private bool stop_with_clear_ = false;

        // Reliability-related member variables.
        private bool session_reliability_ = false;
        private UInt32 seq_recvd_ = 0;
        private UInt32 tcp_seq_ = 0;
        private UInt32 http_seq_ = 0;
        private bool first_receiving_ = false;
        private TransportProtocol session_protocol_;
        private Queue<FunapiMessage> send_queue_ = new Queue<FunapiMessage>();
        private Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();
        private System.Random rnd_ = new System.Random();

        // Transport-related member variables.
        private object transports_lock_ = new object();
        private TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        private Dictionary<TransportProtocol, FunapiTransport> transports_ = new Dictionary<TransportProtocol, FunapiTransport>();

        // Message-related member variables.
        private object message_lock_ = new object();
        private object expected_reply_lock = new object();
        private DateTime last_received_ = DateTime.Now;
        private Dictionary<string, TransportProtocol> message_protocols_ = new Dictionary<string, TransportProtocol>();
        private Dictionary<string, MessageEventHandler> message_handlers_ = new Dictionary<string, MessageEventHandler>();
        private Dictionary<string, List<ExpectedReply>> expected_replies_ = new Dictionary<string, List<ExpectedReply>>();
        private List<FunapiMessage> message_buffer_ = new List<FunapiMessage>();
    }
}  // namespace Fun
