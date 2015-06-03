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


namespace Fun
{
    // Funapi version
    public class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 79;
    }

    // Funapi message type
    public enum FunMsgType
    {
        kNone,
        kJson,
        kProtobuf
    }

    // Error code
    public enum ErrorCode
    {
        kNone,
        kConnectFailed,
        kSendFailed,
        kReceiveFailed,
        kEncryptionFailed,
        kInvalidEncryption,
        kUnknownEncryption,
        kExceptionError
    }

    // Sending message-related class.
    internal class FunapiMessage
    {
        public FunapiMessage (TransportProtocol protocol, string msg_type, object message,
                              EncryptionType enc = EncryptionType.kDefaultEncryption)
        {
            this.protocol = protocol;
            this.msg_type = msg_type;
            this.message = message;
            this.enc_type = enc;
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
        #region public interface
        public FunapiNetwork(bool session_reliability = false)
        {
            state_ = State.kUnknown;
            recv_type_ = typeof(FunMessage);

            session_reliability_ = session_reliability;
            InitSession();

            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            message_handlers_[kMaintenanceMessageType] = this.OnMaintenanceMessage;
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiNetwork(bool session_reliability)' instead.")]
        public FunapiNetwork(FunMsgType type, bool session_reliability)
            : this(session_reliability)
        {
            msg_type_ = type;
        }

        [System.Obsolete("This will be deprecated September 2015. Use 'FunapiNetwork(bool session_reliability)' instead.")]
        public FunapiNetwork(FunapiTransport transport, FunMsgType type, bool session_reliability,
                             SessionInitHandler on_session_initiated, SessionCloseHandler on_session_closed)
            : this(type, session_reliability)
        {
            OnSessionInitiated += new SessionInitHandler(on_session_initiated);
            OnSessionClosed += new SessionCloseHandler(on_session_closed);

            AttachTransport(transport);
            SetDefaultProtocol(transport.protocol);
        }

        // Set default protocol
        public void SetDefaultProtocol (TransportProtocol protocol)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);

            default_protocol_ = protocol;
            Debug.Log("SetProtocol - default protocol is '" + protocol + "'.");
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

        public void AttachTransport (FunapiTransport transport)
        {
            DebugUtils.Assert(transport != null);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(transport.protocol))
                {
                    Debug.LogWarning("AttachTransport - transport of '" + transport.protocol +
                                     "' type already exists. You should call DetachTransport first.");
                    return;
                }

                if (transport.msg_type_ == FunMsgType.kNone)
                    transport.msg_type_ = msg_type_;

                transport.ConnectTimeoutCallback += new TransportEventHandler(OnConnectTimeout);
                transport.StartedInternalCallback += new TransportEventHandler(OnTransportStarted);
                transport.StoppedCallback += new TransportEventHandler(OnTransportStopped);
                transport.ReceivedCallback += new TransportReceivedHandler(OnTransportReceived);
                transport.MessageFailureCallback += new TransportMessageHandler(OnTransportFailure);

                serializer_ = new FunMessageSerializer ();
                transport.ProtobufHelper = serializer_;

                transports_[transport.protocol] = transport;

                if (default_protocol_ == TransportProtocol.kDefault)
                {
                    SetDefaultProtocol(transport.protocol);
                }

                if (Started)
                {
                    StartTransport(transport);
                }

                Debug.Log("'" + transport.protocol + "' transport attached.");
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
                    Debug.Log("'" + protocol + "' transport detached.");

                    if (protocol == default_protocol_)
                    {
                        FunapiTransport other = FindOtherTransport(transport.protocol);
                        if (other != null)
                        {
                            SetDefaultProtocol(other.protocol);
                        }
                        else
                        {
                            default_protocol_ = TransportProtocol.kDefault;
                            Debug.LogWarning("DetachTransport - Deletes default protocol.\n" +
                                             "You need to set default protocol up.");
                        }
                    }
                }
                else
                {
                    Debug.Log("DetachTransport - Can't find a transport of '" + protocol + "' type.");
                }
            }
        }

        public void Redirect (TransportProtocol protocol, string hostname_or_ip, UInt16 port, bool keep_session_id = false)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                Debug.LogWarning("Redirect - Can't find a " + protocol + " transport.");
                return;
            }

            if (!keep_session_id)
            {
                InitSession();
            }

            if (protocol == TransportProtocol.kTcp)
            {
                ((FunapiTcpTransport)transport).Redirect(hostname_or_ip, port);
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                ((FunapiUdpTransport)transport).Redirect(hostname_or_ip, port);
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                ((FunapiHttpTransport)transport).Redirect(hostname_or_ip, port);
            }
        }

        public void RedirectHttps (string hostname_or_ip, UInt16 port)
        {
            FunapiTransport transport = GetTransport(TransportProtocol.kHttp);
            if (transport == null)
            {
                Debug.LogWarning("RedirectHttps - Can't find a Http transport.");
                return;
            }

            ((FunapiHttpTransport)transport).Redirect(hostname_or_ip, port, true);
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

        public bool HasTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return true;
            }

            return false;
        }

        public void StartTransport (TransportProtocol protocol)
        {
            StartTransport(GetTransport(protocol));
        }

        internal void StartTransport (FunapiTransport transport)
        {
            if (transport == null)
                return;

            Debug.Log("Starting " + transport.protocol + " transport.");

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

        internal void StopTransport (FunapiTransport transport)
        {
            if (transport == null)
                return;

            Debug.Log("Stopping " + transport.protocol + " transport.");

            lock (state_lock_)
            {
                if (state_ == State.kWaitForSession &&
                    transport.state == FunapiTransport.State.kWaitForSessionResponse)
                {
                    FunapiTransport other = FindOtherTransport(transport.protocol);
                    if (other != null)
                    {
                        other.state = FunapiTransport.State.kWaitForSessionResponse;
                        SendEmptyMessage(other.protocol);
                    }
                }
            }

            if (transport.protocol == default_protocol_)
            {
                FunapiTransport other = FindOtherTransport(transport.protocol);
                if (other != null)
                {
                    SetDefaultProtocol(other.protocol);
                }
            }

            transport.Stop();
        }

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
        public void Stop()
        {
            // Waits for unsent messages.
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.Started && transport.HasUnsentMessages)
                    {
                        lock (state_lock_)
                        {
                            state_ = State.kWaitForStop;
                            return;
                        }
                    }
                }
            }

            StopTransportAll();
            transports_.Clear();

            CloseSession();

            lock (state_lock_)
            {
                state_ = State.kUnknown;
            }

            Debug.Log("Stopping a network module.");
        }

        // Your update method inheriting MonoBehaviour should explicitly invoke this method.
        public void Update()
        {
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
                    Stop();
                    return;
                }
            }

            lock (message_lock_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugUtils.Log("Update messages. count: " + message_buffer_.Count);

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
                                Debug.Log("'" + exp.msg_type + "' message waiting time has been exceeded.");
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

        public bool SessionReliability
        {
            get { return session_reliability_; }
        }

        [System.Obsolete("This will be deprecated in September 2015. Use 'GetMsgType(TransportProtocol)' instead.")]
        public FunMsgType MsgType
        {
            get { return msg_type_; }
        }

        public FunMsgType GetMsgType (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return FunMsgType.kNone;

            return transport.MsgType;
        }

        [System.Obsolete("This will be deprecated in September 2015. Use 'CreateFunMessage(object, MessageType)' instead.")]
        public FunMessage CreateFunMessage(object msg, int msg_index)
        {
            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, msg_index, ProtoBuf.DataFormat.Default, msg);
            return _msg;
        }

        public FunMessage CreateFunMessage(object msg, MessageType msg_type)
        {
            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, (int)msg_type, ProtoBuf.DataFormat.Default, msg);
            return _msg;
        }

        [System.Obsolete("This will be deprecated in September 2015. Use 'GetMessage(FunMessage, MessageType)' instead.")]
        public object GetMessage(FunMessage msg, Type msg_type, int msg_index)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(serializer_, msg_type, msg,
                                                  msg_index, ProtoBuf.DataFormat.Default, true, out _msg);
            if (!success)
            {
                Debug.Log(String.Format("Failed to decode {0} {1}", msg_type, msg_index));
                return null;
            }

            return _msg;
        }

        public object GetMessage(FunMessage msg, MessageType msg_type)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(
                    serializer_, MessageTable.GetType(msg_type), msg, (int)msg_type,
                    ProtoBuf.DataFormat.Default, true, out _msg);
            if (!success)
            {
                Debug.Log(String.Format("Failed to decode {0} {1}",
                            MessageTable.GetType(msg_type), (int)msg_type));
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
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && transport.state == FunapiTransport.State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                FunapiMessage fun_msg = null;

                if (transport.msg_type_ == FunMsgType.kJson)
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
                        Debug.Log(protocol + " send message - msgtype:" + msg_type + " seq:" + (seq_ - 1));
                    }
                    else
                    {
                        Debug.Log(protocol + " send message - msgtype:" + msg_type);
                    }
                }
                else if (transport.msg_type_ == FunMsgType.kProtobuf)
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
                        Debug.Log(protocol + " send message - msgtype:" + msg_type + " seq:" + pbuf.seq);
                    }
                    else
                    {
                        Debug.Log(protocol + " send message - msgtype:" + msg_type);
                    }
                }

                if (expected_reply_type != null && expected_reply_type.Length > 0)
                {
                    AddExpectedReply(fun_msg, expected_reply_type, expected_reply_time, onReplyMissed);
                }

                transport.SendMessage(fun_msg);
            }
            else if (transport_reliability ||
                     (transport != null && transport.state == FunapiTransport.State.kEstablished))
            {
                if (transport.msg_type_ == FunMsgType.kJson)
                {
                    if (transport == null)
                        unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, message, encryption));
                    else
                        unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, transport.JsonHelper.Clone(message), encryption));
                }
                else if (transport.msg_type_ == FunMsgType.kProtobuf)
                {
                    unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, message, encryption));
                }

                Debug.Log("SendMessage - '" + msg_type + "' message queued.");
            }
            else
            {
                string str_log = "SendMessage - '" + msg_type + "' message skipped.";
                if (transport == null)
                    str_log += "\nThere's no '" + protocol + "' transport.";
                else if (transport.state != FunapiTransport.State.kEstablished)
                    str_log += "\nTransport's state is '" + transport.state + "'.";

                Debug.Log(str_log);
            }
        }

        public void RegisterHandler(string type, MessageEventHandler handler)
        {
            DebugUtils.Log("New handler for message type '" + type + "'");
            message_handlers_[type] = handler;
        }

        public void RegisterHandlerWithProtocol(string type, TransportProtocol protocol, MessageEventHandler handler)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                RegisterHandler(type, handler);
                return;
            }

            DebugUtils.Log("New handler for and message type '" + type + "' of '" + protocol + "' protocol.");
            message_protocols_[type] = protocol;
            message_handlers_[type] = handler;
        }

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
        #endregion

        #region internal implementation
        private FunapiTransport FindOtherTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (protocol == TransportProtocol.kDefault || transports_.Count <= 0)
                    return null;

                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.protocol != protocol && transport.Started)
                    {
                        return transport;
                    }
                }
            }

            return null;
        }

        private void InitSession()
        {
            session_id_ = "";

            if (session_reliability_)
            {
                seq_recvd_ = 0;
                first_receiving_ = true;
                send_queue_.Clear();
                seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            }
        }

        private void PrepareSession(string session_id)
        {
            if (session_id_.Length == 0)
            {
                DebugUtils.Log("New session id: " + session_id);
                OpenSession(session_id);
            }

            if (session_id_ != session_id)
            {
                DebugUtils.Log("Session id changed: " + session_id_ + " => " + session_id);

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

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.state == FunapiTransport.State.kWaitForSession ||
                        transport.state == FunapiTransport.State.kWaitForSessionResponse)
                    {
                        transport.OnStarted();
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
                Debug.Log("Adds expected reply message - " + fun_msg.msg_type + " > " + reply_type);
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

            if (transport.msg_type_ == FunMsgType.kJson)
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

                    if (protocol == TransportProtocol.kTcp && session_reliability_)
                    {
                        if (transport.JsonHelper.HasField(json, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)transport.JsonHelper.GetIntegerField(json, kAckNumberField);
                            OnAckReceived(ack);
                            // Does not support piggybacking.
                            DebugUtils.Assert(!transport.JsonHelper.HasField(json, kMsgTypeBodyField));
                            return;
                        }

                        if (transport.JsonHelper.HasField(json, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(json, kSeqNumberField);
                            if (!OnSeqReceived(seq))
                            {
                                return;
                            }
                            transport.JsonHelper.RemoveStringField(json, kSeqNumberField);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Failure in ProcessMessage: " + e.ToString());
                    StopTransport(transport);
                    return;
                }

                if (transport.JsonHelper.HasField(json, kMsgTypeBodyField))
                {
                    string msg_type_node = transport.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                    msg_type = msg_type_node;
                    transport.JsonHelper.RemoveStringField(json, kMsgTypeBodyField);

                    DeleteExpectedReply(msg_type);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, json);
                }
            }
            else if (transport.msg_type_ == FunMsgType.kProtobuf)
            {
                FunMessage message;

                try
                {
                    MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                    message = (FunMessage)serializer_.Deserialize(stream, null, recv_type_);

                    session_id = message.sid;

                    PrepareSession(session_id);

                    if (protocol == TransportProtocol.kTcp && session_reliability_)
                    {
                        if (message.ackSpecified)
                        {
                            OnAckReceived(message.ack);
                            // Does not support piggybacking.
                            return;
                        }

                        if (message.seqSpecified)
                        {
                            if (!OnSeqReceived(message.seq))
                            {
                                return;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("Failure in ProcessMessage: " + e.ToString());
                    StopTransport(transport);
                    return;
                }

                if (message.msgtype != null && message.msgtype.Length > 0)
                {
                    msg_type = message.msgtype;

                    DeleteExpectedReply(msg_type);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, message);
                }
            }
            else
            {
                Debug.Log("Invalid message type. type: " + transport.msg_type_);
                DebugUtils.Assert(false);
                return;
            }

            if (!message_handlers_.ContainsKey(msg_type))
            {
                if (session_id_.Length > 0 && transport.state == FunapiTransport.State.kWaitForAck)
                {
                    transport.OnStarted();

                    if (unsent_queue_.Count > 0)
                    {
                        SendUnsentMessages();
                    }
                }

                Debug.Log("No handler for message '" + msg_type + "'. Ignoring.");
            }
        }

        private void SendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            Debug.Log("SendUnsentMessages - " + unsent_queue_.Count + " unsent messages.");

            foreach (FunapiMessage msg in unsent_queue_)
            {
                FunapiTransport transport = GetTransport(msg.protocol);
                if (transport == null || transport.state != FunapiTransport.State.kEstablished)
                {
                    Debug.Log("SendUnsentMessages - " + msg.protocol + " isn't a valid transport.\n" +
                              "Message skipped.");
                    continue;
                }

                if (transport.msg_type_ == FunMsgType.kJson)
                {
                    object json = msg.message;

                    // Encodes a messsage type
                    transport.JsonHelper.SetStringField(json, kMsgTypeBodyField, msg.msg_type);

                    if (session_id_.Length > 0)
                        transport.JsonHelper.SetStringField(json, kSessionIdBodyField, session_id_);

                    if (session_reliability_ && transport.protocol == TransportProtocol.kTcp)
                    {
                        transport.JsonHelper.SetIntegerField(json, kSeqNumberField, seq_);
                        ++seq_;

                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msg_type + " seq:" + (seq_ - 1));
                    }
                    else
                    {
                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msg_type);
                    }
                }
                else if (transport.msg_type_ == FunMsgType.kProtobuf)
                {
                    FunMessage pbuf = msg.message as FunMessage;
                    pbuf.msgtype = msg.msg_type;

                    if (session_id_.Length > 0)
                        pbuf.sid = session_id_;

                    if (session_reliability_ && transport.protocol == TransportProtocol.kTcp)
                    {
                        pbuf.seq = seq_;
                        ++seq_;

                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msg_type + " seq:" + pbuf.seq);
                    }
                    else
                    {
                        Debug.Log(transport.protocol + " send unsent message - msgtype:" + msg.msg_type);
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

        private bool SeqLess(UInt32 x, UInt32 y)
        {
            Int32 dist = (Int32)(x - y);
            return dist > 0;
        }

        private void SendAck(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
            if (transport == null)
                return;

            Debug.Log(transport.protocol + " send ack message - ack:" + ack);

            if (transport.msg_type_ == FunMsgType.kJson)
            {
                object ack_msg = transport.JsonHelper.Deserialize("{}");
                transport.JsonHelper.SetStringField(ack_msg, kSessionIdBodyField, session_id_);
                transport.JsonHelper.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport.SendMessage(new FunapiMessage(transport.protocol, "", ack_msg));
            }
            else if (transport.msg_type_ == FunMsgType.kProtobuf)
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.sid = session_id_;
                ack_msg.ack = ack;
                transport.SendMessage(new FunapiMessage(transport.protocol, "", ack_msg));
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

            Debug.Log(transport.protocol + " send empty message");

            if (transport.msg_type_ == FunMsgType.kJson)
            {
                object msg = transport.JsonHelper.Deserialize("{}");
                transport.SendMessage(new FunapiMessage(transport.protocol, "", msg));
            }
            else if (transport.msg_type_ == FunMsgType.kProtobuf)
            {
                FunMessage msg = new FunMessage();
                transport.SendMessage(new FunapiMessage(transport.protocol, "", msg));
            }
        }

        private bool OnSeqReceived(UInt32 seq)
        {
            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                if (seq_recvd_ + 1 != seq)
                {
                    Debug.LogWarning("Received wrong sequence number " + seq.ToString() +
                                     ".(" + (seq_recvd_ + 1).ToString() + " expected");
                    DebugUtils.Assert(false);
                    Stop();
                    return false;
                }
            }

            seq_recvd_ = seq;
            SendAck(seq_recvd_ + 1);
            return true;
        }

        private void OnAckReceived(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
            if (transport == null)
            {
                Debug.LogError("OnAckReceived - transport is null.");
                return;
            }

            while (send_queue_.Count > 0)
            {
                UInt32 seq;
                FunapiMessage last_msg = send_queue_.Peek();
                if (transport.msg_type_ == FunMsgType.kJson)
                {
                    seq = (UInt32)transport.JsonHelper.GetIntegerField(last_msg.message, kSeqNumberField);
                }
                else if (transport.msg_type_ == FunMsgType.kProtobuf)
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
                    if (transport.msg_type_ == FunMsgType.kJson)
                    {
                        UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(msg.message, kSeqNumberField);
                        DebugUtils.Assert(seq == ack || SeqLess(seq, ack));
                        transport.SendMessage(msg);
                    }
                    else if (transport.msg_type_ == FunMsgType.kProtobuf)
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

                transport.OnStarted();

                if (unsent_queue_.Count > 0)
                {
                    SendUnsentMessages();
                }
            }
        }

        private void OnConnectTimeout (TransportProtocol protocol)
        {
            if (protocol != TransportProtocol.kTcp || session_reliability_ == false)
            {
                StopTransport(protocol);
            }
        }

        private void OnTransportStarted (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            DebugUtils.Assert(transport != null);
            Debug.Log("'" + protocol + "' Transport started.");

            lock (state_lock_)
            {
                if (session_id_.Length > 0)
                {
                    state_ = State.kConnected;

                    if (session_reliability_ && protocol == TransportProtocol.kTcp && seq_recvd_ != 0)
                    {
                        transport.state = FunapiTransport.State.kWaitForAck;
                        SendAck(seq_recvd_ + 1);
                    }
                    else
                    {
                        transport.OnStarted();

                        if (unsent_queue_.Count > 0)
                        {
                            SendUnsentMessages();
                        }
                    }
                }
                else if (state_ == State.kStarted || state_ == State.kStopped)
                {
                    state_ = State.kWaitForSession;
                    transport.state = FunapiTransport.State.kWaitForSessionResponse;

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
            Debug.Log(protocol + " Transport Stopped.");

            lock (state_lock_)
            {
                if (state_ != State.kStopped)
                {
                    lock (transports_lock_)
                    {
                        bool all_stopped = true;
                        foreach (FunapiTransport t in transports_.Values)
                        {
                            if (t.Started)
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
        }

        private void OnStoppedAllTransportCallback()
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
        #endregion

        #region Funapi system message handlers
        private void OnNewSession(string msg_type, object body)
        {
            // ignore.
        }

        private void OnSessionTimedout(string msg_type, object body)
        {
            Debug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");

            CloseSession();
        }

        private void OnMaintenanceMessage(string msg_type, object body)
        {
            if (MaintenanceCallback != null)
            {
                MaintenanceCallback(msg_type, body);
            }
        }
        #endregion


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
        private Type recv_type_;
        private FunMsgType msg_type_ = FunMsgType.kNone;
        private TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        private FunMessageSerializer serializer_;
        private Dictionary<TransportProtocol, FunapiTransport> transports_ = new Dictionary<TransportProtocol, FunapiTransport>();
        private string session_id_ = "";
        private Dictionary<string, TransportProtocol> message_protocols_ = new Dictionary<string, TransportProtocol>();
        private Dictionary<string, MessageEventHandler> message_handlers_ = new Dictionary<string, MessageEventHandler>();
        private Dictionary<string, List<FunapiMessage>> expected_replies_ = new Dictionary<string, List<FunapiMessage>>();
        private List<KeyValuePair<TransportProtocol, ArraySegment<byte>>> message_buffer_ = new List<KeyValuePair<TransportProtocol, ArraySegment<byte>>>();
        private object state_lock_ = new object();
        private object message_lock_ = new object();
        private object transports_lock_ = new object();
        private object expected_reply_lock = new object();
        private DateTime last_received_ = DateTime.Now;

        // Reliability-releated member variables.
        private bool session_reliability_;
        private UInt32 seq_ = 0;
        private UInt32 seq_recvd_ = 0;
        private bool first_receiving_ = false;
        private Queue<FunapiMessage> send_queue_ = new Queue<FunapiMessage>();
        private Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();
        private System.Random rnd_ = new System.Random();
    }
}  // namespace Fun
