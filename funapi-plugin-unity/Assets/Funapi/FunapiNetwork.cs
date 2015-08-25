// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if !NO_UNITY
using UnityEngine;
#endif

// Protobuf
using funapi.network.fun_message;
using funapi.network.ping_message;
using funapi.service.multicast_message;


namespace Fun
{
    // Funapi version
    internal class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 98;
    }

    // Sending message-related class.
    internal class FunapiMessage
    {
        public FunapiMessage (TransportProtocol protocol, string msg_type, object message,
                              EncryptionType enc = EncryptionType.kDefaultEncryption)
        {
            this.protocol = protocol;
            this.enc_type = enc;
            this.msg_type = msg_type;
            this.message = message;
        }

        // Sets expected reply
        public void SetReply (string reply_type, float reply_timeout, TimeoutEventHandler callback)
        {
            this.reply_type = reply_type;
            this.reply_timeout = reply_timeout;
            this.timeout_callback = callback;
        }

        // member variables.
        public TransportProtocol protocol;
        public EncryptionType enc_type;
        public string msg_type;
        public object message;
        public ArraySegment<byte> buffer;

        // expected reply-related members.
        public string reply_type = "";
        public float reply_timeout = 0f;
        public TimeoutEventHandler timeout_callback = null;
    }


    // Driver to use Funapi network plugin.
    public class FunapiNetwork
    {
        public FunapiNetwork (bool session_reliability = false)
        {
            state_ = State.kUnknown;
            recv_type_ = typeof(FunMessage);
            session_reliability_ = session_reliability;
            serializer_ = new FunMessageSerializer ();

            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            message_handlers_[kMaintenanceMessageType] = this.OnMaintenanceMessage;

            InitSession();
            InitPing();
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiNetwork(bool session_reliability)' instead.")]
        public FunapiNetwork (FunapiTransport transport, bool session_reliability,
                              SessionInitHandler on_session_initiated, SessionCloseHandler on_session_closed)
            : this(session_reliability)
        {
            OnSessionInitiated += new SessionInitHandler(on_session_initiated);
            OnSessionClosed += new SessionCloseHandler(on_session_closed);

            AttachTransport(transport);
            SetDefaultProtocol(transport.Protocol);
        }

        public bool SessionReliability
        {
            get { return session_reliability_; }
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
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);

            default_protocol_ = protocol;
            Debug.Log(String.Format("SetProtocol - default protocol is '{0}'.", protocol));
        }

        public TransportProtocol GetDefaultProtocol()
        {
            return default_protocol_;
        }

        // Set message protocol
        public void SetMessageProtocol (TransportProtocol protocol, string msg_type)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);
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
                Debug.LogWarning(String.Format("Connect - Can't find a {0} transport.", protocol));
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
                Debug.LogWarning(String.Format("Reconnect - Can't find a {0} transport.", protocol));
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
                Debug.LogWarning(String.Format("Redirect - Can't find a {0} transport.", protocol));
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
            Debug.Log("Starting a network module.");

            lock (state_lock_)
            {
                state_ = State.kStarted;
            }

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    StartTransport(transport);
                }
            }
        }

        // Stops FunapiNetwork
        public void Stop (bool clear_all = true)
        {
            stop_with_clear_ = clear_all;

            // Waits for unsent messages.
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
                            Debug.Log(string.Format("{0} Stop waiting for send unsent messages...",
                                                         transport.str_protocol));
                            return;
                        }
                    }
                }
            }

            Debug.Log("Stopping a network module.");

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    StopTransport(transport);
                }
            }

            if (clear_all)
            {
                transports_.Clear();

                CloseSession();

                lock (state_lock_)
                {
                    state_ = State.kUnknown;
                }
            }
            else
            {
                lock (state_lock_)
                {
                    state_ = State.kStopped;
                }
            }

            lock (message_lock_)
            {
                message_buffer_.Clear();
            }

            OnStoppedAllTransportCallback();
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


        //---------------------------------------------------------------------
        // Updates FunapiNetwork
        // Please call this Update function inside your Unity3d Update.
        //---------------------------------------------------------------------
        public void Update()
        {
            timer_.Update();

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport != null)
                        transport.Update();
                }
            }

            lock (state_lock_)
            {
                if (state_ == State.kWaitForStop)
                {
                    Stop(stop_with_clear_);
                    return;
                }
            }

            lock (message_lock_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugUtils.Log(String.Format("Update messages. count: {0}", message_buffer_.Count));

                    foreach (KeyValuePair<TransportProtocol, ArraySegment<byte>> buffer in message_buffer_)
                    {
                        ProcessMessage(buffer.Key, buffer.Value);
                    }

                    message_buffer_.Clear();
                }
            }

            lock (expected_reply_lock)
            {
                if (expected_replies_.Count > 0)
                {
                    List<string> remove_list = new List<string>();

                    foreach (var item in expected_replies_)
                    {
                        int remove_count = 0;
                        foreach (FunapiMessage exp in item.Value)
                        {
                            exp.reply_timeout -= Time.deltaTime;
                            if (exp.reply_timeout <= 0f)
                            {
                                Debug.Log(String.Format("'{0}' message waiting time has been exceeded.", exp.msg_type));
                                exp.timeout_callback(exp.msg_type);
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
                            expected_replies_.Remove(key);
                        }
                    }
                }
            }
        }


        //---------------------------------------------------------------------
        // FunapiTransport-related functions
        //---------------------------------------------------------------------
        public void AttachTransport (FunapiTransport transport)
        {
            DebugUtils.Assert(transport != null);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(transport.Protocol))
                {
                    StringBuilder strlog = new StringBuilder();
                    strlog.AppendFormat("AttachTransport - transport of '{0}' type already exists.", transport.Protocol);
                    strlog.Append(" You should call DetachTransport first.");
                    Debug.LogWarning(strlog);
                    return;
                }

                transport.Timer = timer_;

                // Callback functions
                transport.ConnectTimeoutCallback += new TransportEventHandler(OnConnectTimeout);
                transport.StartedInternalCallback += new TransportEventHandler(OnTransportStarted);
                transport.StoppedCallback += new TransportEventHandler(OnTransportStopped);
                transport.ConnectFailureCallback += new TransportEventHandler(OnTransportConnectFailure);
                transport.DisconnectedCallback += new TransportEventHandler(OnTransportDisconnected);
                transport.ReceivedCallback += new TransportReceivedHandler(OnTransportReceived);
                transport.MessageFailureCallback += new TransportMessageHandler(OnTransportFailure);

                transport.ProtobufHelper = serializer_;

                transports_[transport.Protocol] = transport;

                if (default_protocol_ == TransportProtocol.kDefault)
                {
                    SetDefaultProtocol(transport.Protocol);
                }

                if (Started)
                {
                    StartTransport(transport);
                }

                Debug.Log(String.Format("{0} transport attached.", transport.Protocol));
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
                    Debug.Log(String.Format("{0} transport detached.", protocol));

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
                            Debug.LogWarning("DetachTransport - Deletes default protocol. You need to set default protocol up.");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning(String.Format("DetachTransport - Can't find a transport of '{0}' type.", protocol));
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

            Debug.Log(String.Format("Starting {0} transport.", transport.Protocol));

            lock (state_lock_)
            {
                if (state_ == State.kUnknown)
                {
                    Start();
                    return;
                }
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

            Debug.Log(String.Format("Stopping {0} transport.", transport.Protocol));

            StopPingTimer(transport);

            transport.Stop();
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiNetwork.Stop(false)' instead.")]
        public void StopTransportAll()
        {
            lock (state_lock_)
            {
                state_ = State.kStopped;
            }

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    StopTransport(transport);
                }
            }

            OnStoppedAllTransportCallback();
        }

        private void SetTransportStarted (FunapiTransport transport, bool send_unsent = true)
        {
            if (transport == null)
                return;

            transport.OnStarted();

            if (send_unsent && unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }

            if (transport.EnablePing && ping_interval_ > 0)
            {
                StartPingTimer(transport);
            }
        }

        private void CheckTransportConnection (TransportProtocol protocol)
        {
            lock (state_lock_)
            {
                if (state_ == State.kStopped)
                    return;

                if (state_ == State.kWaitForSession && protocol == session_protocol_)
                {
                    FunapiTransport other = FindOtherTransport(protocol);
                    if (other != null)
                    {
                        other.state = FunapiTransport.State.kWaitForSession;
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
                        if (t.IsConnecting || t.Started)
                        {
                            all_stopped = false;
                            break;
                        }
                    }

                    if (all_stopped)
                    {
                        state_ = State.kStopped;

                        lock (message_lock_)
                        {
                            message_buffer_.Clear();
                        }

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
            DebugUtils.Assert(transport != null);
            Debug.Log(String.Format("{0} Transport started.", protocol));

            lock (state_lock_)
            {
                if (session_id_.Length > 0)
                {
                    state_ = State.kConnected;

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
                    state_ = State.kWaitForSession;
                    transport.state = FunapiTransport.State.kWaitForSession;

                    // To get a session id
                    SendEmptyMessage(protocol);
                }
                else if (state_ == State.kWaitForSession)
                {
                    transport.state = FunapiTransport.State.kWaitForSession;
                }
            }
        }

        private void OnTransportStopped (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            DebugUtils.Assert(transport != null);
            Debug.Log(String.Format("{0} Transport Stopped.", protocol));

            StopPingTimer(transport);

            CheckTransportConnection(protocol);
        }

        private void OnTransportConnectFailure (TransportProtocol protocol)
        {
            Debug.Log(string.Format("'{0}' transport connect failed.", protocol));

            CheckTransportConnection(protocol);

            if (TransportConnectFailedCallback != null)
                TransportConnectFailedCallback(protocol);
        }

        private void OnTransportDisconnected (TransportProtocol protocol)
        {
            Debug.Log(string.Format("'{0}' transport disconnected.", protocol));

            CheckTransportConnection(protocol);

            if (TransportDisconnectedCallback != null)
                TransportDisconnectedCallback(protocol);
        }

        private void OnStoppedAllTransportCallback ()
        {
            Debug.Log("All transports has stopped.");

            if (StoppedAllTransportCallback != null)
                StoppedAllTransportCallback();
        }

        private void OnTransportReceived (TransportProtocol protocol, Dictionary<string, string> header, ArraySegment<byte> body)
        {
            DebugUtils.Log("OnTransportReceived invoked.");
            last_received_ = DateTime.Now;

            lock (message_lock_)
            {
                message_buffer_.Add(new KeyValuePair<TransportProtocol, ArraySegment<byte>>(protocol, body));
            }
        }

        private void OnTransportFailure (TransportProtocol protocol, FunapiMessage fun_msg)
        {
            if (fun_msg == null || fun_msg.reply_type.Length <= 0)
                return;

            DeleteExpectedReply(fun_msg.reply_type);
        }


        //---------------------------------------------------------------------
        // Message-related functions
        //---------------------------------------------------------------------
        public void RegisterHandler(string type, MessageEventHandler handler)
        {
            DebugUtils.Log(String.Format("New handler for message type '{0}'", type));
            message_handlers_[type] = handler;
        }

        public void RegisterHandlerWithProtocol(string type, TransportProtocol protocol, MessageEventHandler handler)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                RegisterHandler(type, handler);
                return;
            }

            DebugUtils.Log(String.Format("New handler for and message type '{0}' of '{1}' protocol.", type, protocol));
            message_protocols_[type] = protocol;
            message_handlers_[type] = handler;
        }

        [System.Obsolete("This will be deprecated in September 2015. Use 'CreateFunMessage(object, MessageType)' instead.")]
        public FunMessage CreateFunMessage (object msg, int msg_index)
        {
            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, msg_index, DataFormat.Default, msg);
            return _msg;
        }

        public FunMessage CreateFunMessage (object msg, MessageType msg_type)
        {
            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, (int)msg_type, DataFormat.Default, msg);
            return _msg;
        }

        [System.Obsolete("This will be deprecated in September 2015. Use 'GetMessage(FunMessage, MessageType)' instead.")]
        public object GetMessage (FunMessage msg, Type msg_type, int msg_index)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(serializer_, msg_type, msg,
                                                  msg_index, DataFormat.Default, true, out _msg);
            if (!success)
            {
                Debug.Log(String.Format("Failed to decode {0} {1}", msg_type, msg_index));
                return null;
            }

            return _msg;
        }

        public object GetMessage (FunMessage msg, MessageType msg_type)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(serializer_, MessageTable.GetType(msg_type), msg,
                                                  (int)msg_type, DataFormat.Default, true, out _msg);
            if (!success)
            {
                Debug.Log(String.Format("Failed to decode {0} {1}", MessageTable.GetType(msg_type), (int)msg_type));
                return null;
            }

            return _msg;
        }

        public void SendMessage (MessageType msg_type, object message,
                                 EncryptionType encryption = EncryptionType.kDefaultEncryption,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 string expected_reply_type = null, float expected_reply_time = 0f,
                                 TimeoutEventHandler onReplyMissed = null)
        {
            string _msg_type = MessageTable.Lookup(msg_type);
            SendMessage(_msg_type, message, encryption, protocol, expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage (MessageType msg_type, object message,
                                 string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            string _msg_type = MessageTable.Lookup(msg_type);
            SendMessage(_msg_type, message, EncryptionType.kDefaultEncryption, GetMessageProtocol(_msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage (string msg_type, object message,
                                 string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, message, EncryptionType.kDefaultEncryption, GetMessageProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage (string msg_type, object message,
                                 EncryptionType encryption = EncryptionType.kDefaultEncryption,
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
                Debug.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && transport.state == FunapiTransport.State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                FunapiMessage fun_msg = null;

                if (transport.Encoding == FunEncoding.kJson)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, transport.JsonHelper.Clone(message), encryption);

                    // Encodes a messsage type
                    transport.JsonHelper.SetStringField(fun_msg.message, kMsgTypeBodyField, msg_type);

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        transport.JsonHelper.SetStringField(fun_msg.message, kSessionIdBodyField, session_id_);
                    }

                    if (transport_reliability)
                    {
                        transport.JsonHelper.SetIntegerField(fun_msg.message, kSeqNumberField, seq_);
                        ++seq_;

                        send_queue_.Enqueue(fun_msg);
                        Debug.Log(String.Format("{0} send message - msgtype:{1} seq:{2}", protocol, msg_type, seq_ - 1));
                    }
                    else
                    {
                        Debug.Log(String.Format("{0} send message - msgtype:{1}", protocol, msg_type));
                    }
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, message, encryption);

                    FunMessage pbuf = fun_msg.message as FunMessage;
                    pbuf.msgtype = msg_type;

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        pbuf.sid = session_id_;
                    }

                    if (transport_reliability)
                    {
                        pbuf.seq = seq_;
                        ++seq_;

                        send_queue_.Enqueue(fun_msg);
                        Debug.Log(String.Format("{0} send message - msgtype:{1} seq:{2}", protocol, msg_type, pbuf.seq));
                    }
                    else
                    {
                        Debug.Log(String.Format("{0} send message - msgtype:{1}", protocol, msg_type));
                    }
                }

                if (expected_reply_type != null && expected_reply_type.Length > 0)
                {
                    AddExpectedReply(fun_msg, expected_reply_type, expected_reply_time, onReplyMissed);
                }

                transport.SendMessage(fun_msg);
            }
            else if (transport != null &&
                     (transport_reliability || transport.state == FunapiTransport.State.kEstablished))
            {
                if (transport.Encoding == FunEncoding.kJson)
                {
                    if (transport == null)
                        unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, message, encryption));
                    else
                        unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, transport.JsonHelper.Clone(message), encryption));
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, message, encryption));
                }

                Debug.Log(String.Format("SendMessage - '{0}' message queued.", msg_type));
            }
            else
            {
                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("SendMessage - '{0}' message skipped.", msg_type);
                if (transport == null)
                    strlog.AppendFormat(" There's no '{0}' transport.", protocol);
                else if (transport.state != FunapiTransport.State.kEstablished)
                    strlog.AppendFormat(" Transport's state is '{0}'.", transport.state);

                Debug.Log(strlog);
            }
        }

        private void AddExpectedReply (FunapiMessage fun_msg, string reply_type,
                                       float reply_time, TimeoutEventHandler onReplyMissed)
        {
            lock (expected_reply_lock)
            {
                if (!expected_replies_.ContainsKey(reply_type))
                {
                    expected_replies_[reply_type] = new List<FunapiMessage>();
                }

                fun_msg.SetReply(reply_type, reply_time, onReplyMissed);
                expected_replies_[reply_type].Add(fun_msg);
                Debug.Log(String.Format("Adds expected reply message - {0} > {1}", fun_msg.msg_type, reply_type));
            }
        }

        private void DeleteExpectedReply (string reply_type)
        {
            lock (expected_reply_lock)
            {
                if (expected_replies_.ContainsKey(reply_type))
                {
                    List<FunapiMessage> list = expected_replies_[reply_type];
                    if (list.Count > 0)
                    {
                        list.RemoveAt(0);
                        Debug.Log("Deletes expected reply message - " + reply_type);
                    }

                    if (list.Count <= 0)
                        expected_replies_.Remove(reply_type);
                }
            }
        }

        private void ProcessMessage (TransportProtocol protocol, ArraySegment<byte> buffer)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return;

            string msg_type = "";
            string session_id = "";

            if (transport.Encoding == FunEncoding.kJson)
            {
                object json;

                try
                {
                    string str = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                    json = transport.JsonHelper.Deserialize(str);
                    DebugUtils.Log("Parsed json: " + str);

                    DebugUtils.Assert(transport.JsonHelper.GetStringField(json, kSessionIdBodyField) is string);
                    string session_id_node = transport.JsonHelper.GetStringField(json, kSessionIdBodyField) as string;
                    session_id = session_id_node;
                    transport.JsonHelper.RemoveStringField(json, kSessionIdBodyField);

                    PrepareSession(session_id);

                    if (session_reliability_ && protocol == TransportProtocol.kTcp)
                    {
                        if (transport.JsonHelper.HasField(json, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)transport.JsonHelper.GetIntegerField(json, kAckNumberField);
                            OnAckReceived(transport, ack);
                            // Does not support piggybacking.
                            DebugUtils.Assert(!transport.JsonHelper.HasField(json, kMsgTypeBodyField));
                            return;
                        }

                        if (transport.JsonHelper.HasField(json, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(json, kSeqNumberField);
                            if (!OnSeqReceived(transport, seq))
                            {
                                return;
                            }
                            transport.JsonHelper.RemoveStringField(json, kSeqNumberField);
                        }
                    }

                    if (transport.JsonHelper.HasField(json, kMsgTypeBodyField))
                    {
                        string msg_type_node = transport.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                        msg_type = msg_type_node;
                        transport.JsonHelper.RemoveStringField(json, kMsgTypeBodyField);
                    }

                    if (msg_type.Length > 0)
                    {
                        if (msg_type == kServerPingMessageType)
                        {
                            OnServerPingMessage(transport, json);
                            return;
                        }
                        else if (msg_type == kClientPingMessageType)
                        {
                            OnClientPingMessage(transport, json);
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Failure in ProcessMessage: " + e.ToString());
                    StopTransport(transport);
                    return;
                }

                if (msg_type.Length > 0)
                {
                    DeleteExpectedReply(msg_type);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, json);
                }
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage message;

                try
                {
                    MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                    message = (FunMessage)serializer_.Deserialize(stream, null, recv_type_);

                    session_id = message.sid;

                    PrepareSession(session_id);

                    if (session_reliability_ && protocol == TransportProtocol.kTcp)
                    {
                        if (message.ackSpecified)
                        {
                            OnAckReceived(transport, message.ack);
                            // Does not support piggybacking.
                            return;
                        }

                        if (message.seqSpecified)
                        {
                            if (!OnSeqReceived(transport, message.seq))
                            {
                                return;
                            }
                        }
                    }

                    if (message.msgtype != null)
                    {
                        msg_type = message.msgtype;
                    }

                    if (msg_type.Length > 0)
                    {
                        if (msg_type == kServerPingMessageType)
                        {
                            OnServerPingMessage(transport, message);
                            return;
                        }
                        else if (msg_type == kClientPingMessageType)
                        {
                            OnClientPingMessage(transport, message);
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Failure in ProcessMessage: " + e.ToString());
                    StopTransport(transport);
                    return;
                }

                if (msg_type != null && msg_type.Length > 0)
                {
                    DeleteExpectedReply(msg_type);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, message);
                }
            }
            else
            {
                Debug.Log("Invalid message type. type: " + transport.Encoding);
                DebugUtils.Assert(false);
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
                    Debug.Log(String.Format("No handler for message '{0}'. Ignoring.", msg_type));
                }
            }
        }

        private void SendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            Debug.Log(String.Format("SendUnsentMessages - {0} unsent messages.", unsent_queue_.Count));

            foreach (FunapiMessage msg in unsent_queue_)
            {
                FunapiTransport transport = GetTransport(msg.protocol);
                if (transport == null || transport.state != FunapiTransport.State.kEstablished)
                {
                    Debug.Log(String.Format("SendUnsentMessages - {0} isn't a valid transport. Message skipped.", msg.protocol));
                    continue;
                }

                if (transport.Encoding == FunEncoding.kJson)
                {
                    object json = msg.message;

                    // Encodes a messsage type
                    transport.JsonHelper.SetStringField(json, kMsgTypeBodyField, msg.msg_type);

                    if (session_id_.Length > 0)
                        transport.JsonHelper.SetStringField(json, kSessionIdBodyField, session_id_);

                    if (session_reliability_ && transport.Protocol == TransportProtocol.kTcp)
                    {
                        transport.JsonHelper.SetIntegerField(json, kSeqNumberField, seq_);
                        ++seq_;

                        send_queue_.Enqueue(msg);
                        Debug.Log(String.Format("{0} send unsent message - msgtype:{1} seq:{2}",
                                                transport.Protocol, msg.msg_type, seq_ - 1));
                    }
                    else
                    {
                        Debug.Log(String.Format("{0} send unsent message - msgtype:{1}",
                                                transport.Protocol, msg.msg_type));
                    }
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    FunMessage pbuf = msg.message as FunMessage;
                    pbuf.msgtype = msg.msg_type;

                    if (session_id_.Length > 0)
                        pbuf.sid = session_id_;

                    if (session_reliability_ && transport.Protocol == TransportProtocol.kTcp)
                    {
                        pbuf.seq = seq_;
                        ++seq_;

                        send_queue_.Enqueue(msg);
                        Debug.Log(String.Format("{0} send unsent message - msgtype:{1} seq:{2}",
                                                transport.Protocol, msg.msg_type, pbuf.seq));
                    }
                    else
                    {
                        Debug.Log(String.Format("{0} send unsent message - msgtype:{1}",
                                                transport.Protocol, msg.msg_type));
                    }
                }

                if (msg.reply_type != null && msg.reply_type.Length > 0)
                {
                    AddExpectedReply(msg, msg.reply_type, msg.reply_timeout, msg.timeout_callback);
                }

                transport.SendMessage(msg);
            }

            unsent_queue_.Clear();
        }

        private bool SeqLess (UInt32 x, UInt32 y)
        {
            Int32 dist = (Int32)(x - y);
            return dist > 0;
        }

        private void SendAck (FunapiTransport transport, UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);
            if (transport == null)
            {
                Debug.Log("SendAck - transport is null.");
                return;
            }

            if (state_ == State.kStopped)
                return;

            DebugUtils.Log(String.Format("{0} send ack message - ack:{1}", transport.Protocol, ack));

            if (transport.Encoding == FunEncoding.kJson)
            {
                object ack_msg = transport.JsonHelper.Deserialize("{}");
                transport.JsonHelper.SetStringField(ack_msg, kSessionIdBodyField, session_id_);
                transport.JsonHelper.SetIntegerField(ack_msg, kAckNumberField, ack);
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
                Debug.Log("SendEmptyMessage - transport is null.");
                return;
            }

            session_protocol_ = protocol;
            DebugUtils.Log(String.Format("{0} send empty message", transport.str_protocol));

            if (transport.Encoding == FunEncoding.kJson)
            {
                object msg = transport.JsonHelper.Deserialize("{}");
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
            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                UInt32 seq_interval = seq - seq_recvd_;
                if (seq_interval < 1 && Math.Abs(seq_interval) < kStableSequenceInterval)
                {
                    Debug.Log(String.Format("Received previous sequence number {0}. Skipping message.", seq));
                    SendAck(transport, seq_recvd_ + 1);
                    return false;
                }
                else if (seq_interval != 1)
                {
                    Debug.LogWarning(String.Format("Received wrong sequence number {0}. {1} expected.",
                                                   seq, seq_recvd_ + 1));
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
            DebugUtils.Assert(session_reliability_);
            if (transport == null)
            {
                Debug.LogError("OnAckReceived - transport is null.");
                return;
            }

            while (send_queue_.Count > 0)
            {
                UInt32 seq;
                FunapiMessage last_msg = send_queue_.Peek();
                if (transport.Encoding == FunEncoding.kJson)
                {
                    seq = (UInt32)transport.JsonHelper.GetIntegerField(last_msg.message, kSeqNumberField);
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    seq = (last_msg.message as FunMessage).seq;
                }
                else
                {
                    DebugUtils.Assert(false);
                    seq = 0;
                }

                if (SeqLess(ack, seq))
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
                foreach (FunapiMessage msg in send_queue_)
                {
                    if (transport.Encoding == FunEncoding.kJson)
                    {
                        UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(msg.message, kSeqNumberField);
                        DebugUtils.Assert(seq == ack || SeqLess(seq, ack));
                        transport.SendMessage(msg);
                    }
                    else if (transport.Encoding == FunEncoding.kProtobuf)
                    {
                        UInt32 seq = (msg.message as FunMessage).seq;
                        DebugUtils.Assert(seq == ack || SeqLess (seq, ack));
                        transport.SendMessage(msg);
                    }
                    else
                    {
                        DebugUtils.Assert(false);
                    }
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
                seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            }
        }

        private void PrepareSession(string session_id)
        {
            if (session_id_.Length == 0)
            {
                Debug.Log(String.Format("New session id: {0}", session_id));
                OpenSession(session_id);
            }

            if (session_id_ != session_id)
            {
                Debug.Log(String.Format("Session id changed: {0} => {1}", session_id_, session_id));

                CloseSession();
                OpenSession(session_id);
            }
        }

        private void OpenSession(string session_id)
        {
            DebugUtils.Assert(session_id_.Length == 0);

            lock (state_lock_)
            {
                state_ = State.kConnected;
            }

            session_id_ = session_id;
            first_receiving_ = true;

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.state == FunapiTransport.State.kWaitForSession)
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
            Debug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");

            CloseSession();
        }


        //---------------------------------------------------------------------
        // Ping-related functions
        //---------------------------------------------------------------------
        private void InitPing()
        {
            ping_interval_ = 0;
            ping_timeout_seconds_ = 0f;

            if (FunapiConfig.IsValid)
            {
                ping_interval_ = FunapiConfig.PingInterval;
                ping_timeout_seconds_ = FunapiConfig.PingTimeoutSeconds;
            }

            if (ping_interval_ <= 0)
                ping_interval_ = kPingIntervalSecond;

            if (ping_timeout_seconds_ <= 0f)
                ping_timeout_seconds_ = kPingTimeoutSeconds;

            Debug.Log(string.Format("Ping - interval seconds: {0}, timeout seconds: {1}",
                                    ping_interval_, ping_timeout_seconds_));
        }

        public bool EnablePing
        {
            get
            {
                FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
                if (transport == null)
                {
                    Debug.LogWarning("EnablePing - Tcp transport is null.");
                    return false;
                }

                return transport.EnablePing;
            }
            set
            {
                FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
                if (transport == null)
                {
                    Debug.LogWarning("EnablePing - Tcp transport is null.");
                    return;
                }

                if (transport.EnablePing == value)
                    return;

                if (value)
                {
                    if (ping_interval_ <= 0)
                    {
                        Debug.LogWarning("EnablePing - ping interval time is 0.");
                        return;
                    }

                    transport.EnablePing = true;
                    StartPingTimer(transport);
                }
                else
                {
                    transport.EnablePing = false;
                    StopPingTimer(transport);
                }
            }
        }

        public int PingTime
        {
            get
            {
                FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
                if (transport == null)
                {
                    Debug.LogWarning("PingTime - Tcp transport is null.");
                    return 0;
                }

                return transport.PingTime;
            }
        }

        private void StartPingTimer (FunapiTransport transport)
        {
            if (transport.Protocol != TransportProtocol.kTcp)
                return;

            string timer_id = string.Format("{0}_ping", transport.str_protocol);
            timer_.AddTimer(timer_id, ping_interval_, true, OnPingTimerEvent, transport.Protocol);
            transport.PingWaitTime = 0f;
        }

        private void StopPingTimer (FunapiTransport transport)
        {
            if (transport.Protocol != TransportProtocol.kTcp)
                return;

            transport.PingTime = 0;

            string timer_id = string.Format("{0}_ping", transport.str_protocol);
            if (timer_.ContainTimer(timer_id))
                timer_.KillTimer(timer_id);
        }

        private void SendPingMessage (FunapiTransport transport)
        {
            long timestamp = DateTime.Now.Ticks;

            // Send response
            if (transport.Encoding == FunEncoding.kJson)
            {
                object msg = transport.JsonHelper.Deserialize("{}");
                transport.JsonHelper.SetStringField(msg, kMsgTypeBodyField, kClientPingMessageType);
                transport.JsonHelper.SetStringField(msg, kSessionIdBodyField, session_id_);
                transport.JsonHelper.SetIntegerField(msg, kPingTimestampField, timestamp);
                transport.SendMessage(new FunapiMessage(transport.Protocol, kClientPingMessageType, msg));
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunPingMessage ping = new FunPingMessage();
                ping.timestamp = timestamp;
                FunMessage msg = CreateFunMessage(ping, MessageType.cs_ping);
                msg.msgtype = kClientPingMessageType;
                msg.sid = session_id_;
                transport.SendMessage(new FunapiMessage(transport.Protocol, kClientPingMessageType, msg));
            }

            transport.PingWaitTime += ping_interval_;
            DebugUtils.Log(String.Format("Send {0} ping - timestamp: {1}", transport.str_protocol, timestamp));
        }

        private void OnPingTimerEvent (object param)
        {
            FunapiTransport transport = GetTransport((TransportProtocol)param);
            if (transport == null)
                return;

            if (transport.PingWaitTime > ping_timeout_seconds_)
            {
                Debug.LogWarning("Network seems disabled. Stopping the transport.");
                transport.OnDisconnected();
                return;
            }

            SendPingMessage(transport);
        }

        private void OnServerPingMessage (FunapiTransport transport, object body)
        {
            if (transport == null)
            {
                Debug.Log("OnServerPingMessage - transport is null.");
                return;
            }

            // Send response
            if (transport.Encoding == FunEncoding.kJson)
            {
                transport.JsonHelper.SetStringField(body, kMsgTypeBodyField, kServerPingMessageType);

                if (session_id_.Length > 0)
                    transport.JsonHelper.SetStringField(body, kSessionIdBodyField, session_id_);

                transport.SendMessage(new FunapiMessage(transport.Protocol,
                                                        kServerPingMessageType,
                                                        transport.JsonHelper.Clone(body)));
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = body as FunMessage;
                FunPingMessage obj = (FunPingMessage)GetMessage(msg, MessageType.cs_ping);
                if (obj == null)
                    return;

                FunPingMessage ping = new FunPingMessage();
                ping.timestamp = obj.timestamp;
                if (obj.data.Length > 0) {
                    ping.data = new byte[obj.data.Length];
                    Buffer.BlockCopy(ping.data, 0, obj.data, 0, obj.data.Length);
                }

                FunMessage send_msg = CreateFunMessage(ping, MessageType.cs_ping);
                send_msg.msgtype = msg.msgtype;
                send_msg.sid = session_id_;

                transport.SendMessage(new FunapiMessage(transport.Protocol, kServerPingMessageType, send_msg));
            }
        }

        private void OnClientPingMessage (FunapiTransport transport, object body)
        {
            if (transport == null)
            {
                Debug.Log("OnClientPingMessage - transport is null.");
                return;
            }

            long timestamp = 0;

            if (transport.Encoding == FunEncoding.kJson)
            {
                if (transport.JsonHelper.HasField(body, kPingTimestampField))
                {
                    timestamp = (long)transport.JsonHelper.GetIntegerField(body, kPingTimestampField);
                }
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = body as FunMessage;
                object obj = GetMessage(msg, MessageType.cs_ping);
                if (obj == null)
                    return;

                FunPingMessage ping = obj as FunPingMessage;
                timestamp = ping.timestamp;
            }

            if (transport.PingWaitTime > 0)
                transport.PingWaitTime -= ping_interval_;

            transport.PingTime = (int)((DateTime.Now.Ticks - timestamp) / 10000);

            DebugUtils.Log(String.Format("Receive {0} ping - timestamp:{1} time={2} ms",
                                         transport.str_protocol, timestamp, transport.PingTime));
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


        // Status
        public enum State
        {
            kUnknown = 0,
            kStarted,
            kConnected,
            kWaitForSession,
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
        private static readonly int kStableSequenceInterval = 20;

        // Ping message-related constants.
        private static readonly int kPingIntervalSecond = 3;
        private static readonly float kPingTimeoutSeconds = 20f;
        private static readonly string kServerPingMessageType = "_ping_s";
        private static readonly string kClientPingMessageType = "_ping_c";
        private static readonly string kPingTimestampField = "timestamp";

        // Member variables.
        private State state_;
        private string session_id_ = "";
        private object state_lock_ = new object();
        private FunapiTimer timer_ = new FunapiTimer();
        private bool stop_with_clear_ = false;

        // Reliability-releated member variables.
        private bool session_reliability_;
        private UInt32 seq_ = 0;
        private UInt32 seq_recvd_ = 0;
        private bool first_receiving_ = false;
        private TransportProtocol session_protocol_;
        private Queue<FunapiMessage> send_queue_ = new Queue<FunapiMessage>();
        private Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();
        private System.Random rnd_ = new System.Random();

        // Transport-releated member variables.
        private object transports_lock_ = new object();
        private TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        private Dictionary<TransportProtocol, FunapiTransport> transports_ = new Dictionary<TransportProtocol, FunapiTransport>();

        // Message-releated member variables.
        private FunMessageSerializer serializer_;
        private Type recv_type_;
        private DateTime last_received_ = DateTime.Now;
        private object message_lock_ = new object();
        private object expected_reply_lock = new object();
        private Dictionary<string, TransportProtocol> message_protocols_ = new Dictionary<string, TransportProtocol>();
        private Dictionary<string, MessageEventHandler> message_handlers_ = new Dictionary<string, MessageEventHandler>();
        private Dictionary<string, List<FunapiMessage>> expected_replies_ = new Dictionary<string, List<FunapiMessage>>();
        private List<KeyValuePair<TransportProtocol, ArraySegment<byte>>> message_buffer_ = new List<KeyValuePair<TransportProtocol, ArraySegment<byte>>>();

        // Ping-releated member variables.
        private int ping_interval_ = 0;
        private float ping_timeout_seconds_ = 0f;
    }
}  // namespace Fun
