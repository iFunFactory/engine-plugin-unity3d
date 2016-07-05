// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if !NO_UNITY
using UnityEngine;
#else
using System.Threading;
#endif

// Protobuf
using ProtoBuf;
using funapi.network.fun_message;


namespace Fun
{
    public enum SessionEventType
    {
        kOpened,
        kClosed,
        kChanged
    };


    public partial class FunapiSession : FunapiUpdater
    {
        //
        // Create an instance of FunapiSession.
        //
        public static FunapiSession Create (string hostname_or_ip, bool session_reliability)
        {
            return new FunapiSession(hostname_or_ip, session_reliability);
        }

        private FunapiSession (string hostname_or_ip, bool session_reliability)
        {
            state_ = State.kUnknown;
            server_address_ = hostname_or_ip;
            reliable_session_ = session_reliability;

            InitSession();
        }

        //
        // Public functions.
        //
        public void Connect (TransportProtocol protocol, FunEncoding encoding,
                             UInt16 port, TransportOption option)
        {
            Transport transport = CreateTransport(protocol, encoding, port, option);
            if (transport == null)
                return;

            Connect(protocol);
        }

        public void Connect (TransportProtocol protocol)
        {
            if (!Started)
            {
                FunDebug.Log("Starting a network module.");

                lock (state_lock_)
                {
                    state_ = State.kStarted;
                }

                CreateUpdater();
            }

            Transport transport = GetTransport(protocol);
            if (transport == null || transport.Started)
                return;

            event_list.Add(() => StartTransport(protocol));
        }

        public void Close ()
        {
            StopAllTransports();
        }

        public void Close (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null || mono == null)
                return;

#if !NO_UNITY
            mono.StartCoroutine(TryToStopTransport(transport));
#else
            mono.StartCoroutine(() => TryToStopTransport(transport));
#endif
        }

        public void SendMessage (MessageType msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault)
        {
            SendMessage(MessageTable.Lookup(msg_type), message, protocol);
        }

        public void SendMessage (string msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault)
        {
            if (protocol == TransportProtocol.kDefault)
                protocol = default_protocol_;

            Transport transport = GetTransport(protocol);
            bool reliable_transport = IsReliableTransport(protocol);

            if (transport != null && transport.state == Transport.State.kEstablished &&
                (reliable_transport == false || unsent_queue_.Count <= 0))
            {
                FunapiMessage fun_msg = null;
                bool sending_sequence = IsSendingSequence(transport);

                if (transport.Encoding == FunEncoding.kJson)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, FunapiMessage.JsonHelper.Clone(message));

                    // Encodes a messsage type
                    FunapiMessage.JsonHelper.SetStringField(fun_msg.message, kMessageTypeField, msg_type);

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        FunapiMessage.JsonHelper.SetStringField(fun_msg.message, kSessionIdField, session_id_);
                    }

                    if (reliable_transport || sending_sequence)
                    {
                        UInt32 seq = GetNextSeq(protocol);
                        FunapiMessage.JsonHelper.SetIntegerField(fun_msg.message, kSeqNumberField, seq);

                        if (reliable_transport)
                            send_queue_.Enqueue(fun_msg);

                        FunDebug.DebugLog("{0} send message - msgtype:{1} seq:{2}",
                                          ConvertString(protocol), msg_type, seq);
                    }
                    else
                    {
                        FunDebug.DebugLog("{0} send message - msgtype:{1}",
                                          ConvertString(protocol), msg_type);
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

                    if (reliable_transport || sending_sequence)
                    {
                        pbuf.seq = GetNextSeq(protocol);

                        if (reliable_transport)
                            send_queue_.Enqueue(fun_msg);

                        FunDebug.DebugLog("{0} send message - msgtype:{1} seq:{2}",
                                          ConvertString(protocol), msg_type, pbuf.seq);
                    }
                    else
                    {
                        FunDebug.DebugLog("{0} send message - msgtype:{1}",
                                          ConvertString(protocol), msg_type);
                    }
                }
                else
                {
                    FunDebug.LogWarning("The encoding type is invalid. type: {0}", transport.Encoding);
                    return;
                }

                transport.SendMessage(fun_msg);
            }
            else if (transport != null &&
                     (reliable_transport || transport.state == Transport.State.kEstablished))
            {
                if (transport.Encoding == FunEncoding.kJson)
                {
                    unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, FunapiMessage.JsonHelper.Clone(message)));
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, message));
                }

                FunDebug.Log("SendMessage - '{0}' message queued.", msg_type);
            }
            else
            {
                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("SendMessage - '{0}' message skipped. ", msg_type);
                if (transport == null)
                    strlog.AppendFormat("There's no {0} transport.", ConvertString(protocol));
                else if (transport.state != Transport.State.kEstablished)
                    strlog.AppendFormat("Transport's state is '{0}'.", transport.state);

                FunDebug.Log(strlog.ToString());
            }
        }

        public void SetResponseTimeout (string msg_type, float waiting_time)
        {
            if (msg_type == null || msg_type.Length <= 0)
                return;

            lock (expected_response_lock)
            {
                if (expected_responses_.ContainsKey(msg_type))
                {
                    FunDebug.LogWarning("'{0}' expected response type is already added. Ignored.");
                    return;
                }

                expected_responses_[msg_type] = new ExpectedResponse(msg_type, waiting_time);
                FunDebug.DebugLog("Expected response message added - '{0}' ({1}s)", msg_type, waiting_time);
            }
        }

        public void RemoveResponseTimeout (string msg_type)
        {
            lock (expected_response_lock)
            {
                if (expected_responses_.ContainsKey(msg_type))
                {
                    expected_responses_.Remove(msg_type);
                    FunDebug.DebugLog("Expected response message removed - {0}", msg_type);
                }
            }
        }


        //
        // Properties
        //
        public bool ReliableSession
        {
            get { return reliable_session_; }
        }

        public TransportProtocol DefaultProtocol
        {
            get { return default_protocol_; }
            set { default_protocol_ = value;
                  FunDebug.Log("The default protocol is '{0}'", ConvertString(value)); }
        }

        public bool Started
        {
            get { lock (state_lock_) { return state_ != State.kUnknown && state_ != State.kStopped; } }
        }

        public bool Connected
        {
            get {  lock (state_lock_) { return state_ == State.kConnected; } }
        }


        //
        // Derived function from FunapiUpdater
        //
        protected override bool Update (float deltaTime)
        {
            if (!base.Update(deltaTime))
                return false;

            if (!Started)
                return true;

            lock (transports_lock_)
            {
                if (transports_.Count > 0)
                {
                    foreach (Transport transport in transports_.Values)
                    {
                        if (transport != null)
                            transport.Update(deltaTime);
                    }
                }
            }

            UpdateMessages();
            UpdateExpectedResponse(deltaTime);

            return true;
        }

        void UpdateMessages ()
        {
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
                }
            }
        }

        void UpdateExpectedResponse (float deltaTime)
        {
            lock (expected_response_lock)
            {
                if (expected_responses_.Count > 0)
                {
                    List<string> remove_list = new List<string>();
                    Dictionary<string, ExpectedResponse> temp_list = expected_responses_;
                    expected_responses_ = new Dictionary<string, ExpectedResponse>();

                    foreach (ExpectedResponse er in temp_list.Values)
                    {
                        er.wait_time -= deltaTime;
                        if (er.wait_time <= 0f)
                        {
                            FunDebug.Log("'{0}' message waiting time has been exceeded.", er.type);
                            remove_list.Add(er.type);

                            if (ResponseTimeoutCallback != null)
                                ResponseTimeoutCallback(er.type);
                        }
                    }

                    if (remove_list.Count > 0)
                    {
                        foreach (string key in remove_list)
                        {
                            temp_list.Remove(key);
                        }
                    }

                    if (temp_list.Count > 0)
                    {
                        Dictionary<string, ExpectedResponse> added_list = expected_responses_;
                        expected_responses_ = temp_list;

                        if (added_list.Count > 0)
                        {
                            foreach (var item in added_list)
                            {
                                expected_responses_[item.Key] = item.Value;
                            }
                        }
                    }
                }
            }
        }

        void OnClose ()
        {
            ReleaseUpdater();
            UpdateMessages();

            lock (expected_response_lock)
            {
                if (expected_responses_.Count > 0)
                    expected_responses_.Clear();
            }
        }

        protected override void OnQuit ()
        {
            StopAllTransports(true);
        }


        //
        // Session-related functions
        //
        void InitSession()
        {
            session_id_ = "";

            if (reliable_session_)
            {
                seq_recvd_ = 0;
                send_queue_.Clear();
                first_receiving_ = true;
            }

            tcp_seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            http_seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
        }

        void SetSessionId (string session_id)
        {
            if (session_id_.Length == 0)
            {
                FunDebug.Log("New session id: {0}", session_id);
                OpenSession(session_id);

                if (SessionEventCallback != null)
                    SessionEventCallback(SessionEventType.kOpened, session_id_);
            }

            if (session_id_ != session_id)
            {
                FunDebug.Log("Session id changed: {0} => {1}", session_id_, session_id);

                CloseSession();
                OpenSession(session_id);

                if (SessionEventCallback != null)
                    SessionEventCallback(SessionEventType.kChanged, session_id_);
            }
        }

        void OpenSession (string session_id)
        {
            lock (state_lock_)
            {
                state_ = State.kConnected;
            }

            session_id_ = session_id;
            first_receiving_ = true;

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    if (transport.state == Transport.State.kWaitForSession)
                    {
                        SetTransportStarted(transport, false);
                    }
                }
            }

            if (unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }
        }

        void CloseSession ()
        {
            lock (state_lock_)
            {
                state_ = State.kUnknown;
            }

            if (session_id_.Length == 0)
                return;

            if (SessionEventCallback != null)
                SessionEventCallback(SessionEventType.kClosed, session_id_);

            InitSession();
        }


        //
        // Transport-related functions
        //
        Transport CreateTransport (TransportProtocol protocol, FunEncoding encoding,
                                         UInt16 port, TransportOption option)
        {
            Transport transport = GetTransport(protocol);
            if (transport != null)
            {
#if !NO_UNITY
                FunDebug.LogWarning("CreateTransport - {0} transport already exists.",
                                    ConvertString(protocol));
#endif
                return transport;
            }

            if (protocol == TransportProtocol.kTcp)
            {
                TcpTransport tcp_transport = new TcpTransport(server_address_, port, encoding);
                transport = tcp_transport;

                TcpTransportOption tcp_option = option as TcpTransportOption;
                tcp_transport.AutoReconnect = tcp_option.AutoReconnect;
                tcp_transport.DisableNagle = tcp_option.DisableNagle;
                tcp_transport.EnablePing = tcp_option.EnablePing;
                tcp_transport.PingIntervalSeconds = tcp_option.PingIntervalSeconds;
                tcp_transport.PingTimeoutSeconds = tcp_option.PingTimeoutSeconds;
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                transport = new UdpTransport(server_address_, port, encoding);
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                HttpTransport http_transport = new HttpTransport(server_address_, port, false, encoding);
                transport = http_transport;

#if !NO_UNITY
                HttpTransportOption http_option = option as HttpTransportOption;
                http_transport.UseWWW = http_option.UseWWW;
#endif
            }
            else
            {
                FunDebug.LogError("Create a {0} transport failed.", ConvertString(protocol));
                return null;
            }

            transport.ConnectTimeout = option.ConnectTimeout;
            transport.SequenceNumberValidation = option.SequenceValidation;

            if (option.EncType != EncryptionType.kDefaultEncryption)
                transport.SetEncryption(option.EncType);

            // Callback functions
            transport.ConnectTimeoutCallback += OnConnectionTimedOut;
            transport.DisconnectedCallback += OnTransportDisconnected;
            transport.ConnectFailureCallback += OnTransportConnectionFailed;
            transport.FailureCallback += OnTransportFailure;

            transport.StartedInternalCallback += OnTransportStarted;
            transport.StoppedCallback += OnTransportStopped;
            transport.ReceivedCallback += OnTransportReceived;

            lock (transports_lock_)
            {
                transports_[protocol] = transport;
            }

            if (default_protocol_ == TransportProtocol.kDefault)
                DefaultProtocol = protocol;

            FunDebug.DebugLog("{0} transport added.", ConvertString(protocol));
            return transport;
        }

        void StartTransport (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            FunDebug.Log("Starting {0} transport.", ConvertString(protocol));

            if (transport.Protocol == TransportProtocol.kHttp)
            {
                ((HttpTransport)transport).mono = mono;
            }

            transport.Start();
        }

        void StopTransport (Transport transport)
        {
            if (transport == null || transport.state == Transport.State.kUnknown)
                return;

            FunDebug.Log("Stopping {0} transport.", ConvertString(transport.Protocol));

            transport.Stop();
        }

        void SetTransportStarted (Transport transport, bool send_unsent = true)
        {
            if (transport == null)
                return;

            transport.session_id_ = session_id_;

            transport.OnStarted();

            OnTransportEvent(transport.Protocol, TransportEventType.kStarted);

            if (send_unsent && unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }
        }

        void CheckTransportStatus (TransportProtocol protocol)
        {
            if (!Started)
                return;

            lock (state_lock_)
            {
                if (state_ == State.kWaitForSession && protocol == first_sending_protocol_)
                {
                    Transport transport = FindConnectedTransport(protocol);
                    if (transport != null)
                    {
                        transport.state = Transport.State.kWaitForSession;
                        SendFirstMessage(transport);
                    }
                    else
                    {
                        state_ = State.kStarted;
                    }
                }
            }

            bool all_stopped = true;
            lock (transports_lock_)
            {
                foreach (Transport t in transports_.Values)
                {
                    if (t.IsReconnecting || t.Started)
                    {
                        all_stopped = false;
                        break;
                    }
                }
            }

            if (all_stopped)
            {
                lock (state_lock_)
                {
                    if (reliable_session_)
                        state_ = State.kStopped;
                    else
                        state_ = State.kUnknown;
                }

                event_list.Add(() => OnClose());
            }
        }

        void StopAllTransports (bool force_stop = false)
        {
            if (!Started)
                return;

            FunDebug.Log("Stopping a network module.");

            if (force_stop)
            {
                // Stops all transport
                lock (transports_lock_)
                {
                    foreach (Transport transport in transports_.Values)
                    {
                        StopTransport(transport);
                    }
                }
            }
            else
            {
                if (mono == null)
                    return;

                lock (transports_lock_)
                {
                    foreach (Transport transport in transports_.Values)
                    {
#if !NO_UNITY
                        mono.StartCoroutine(TryToStopTransport(transport));
#else
                        mono.StartCoroutine(() => TryToStopTransport(transport));
#endif
                    }
                }
            }
        }

#if !NO_UNITY
        IEnumerator TryToStopTransport (Transport transport)
#else
            void TryToStopTransport (Transport transport)
#endif
        {
            if (transport == null)
#if !NO_UNITY
                yield break;
#else
            return;
#endif

            // Checks transport's state.
            while (!transport.CanBeStop())
            {
                lock (state_lock_)
                {
                    FunDebug.Log("{0} Stop waiting... ({1})",
                                 ConvertString(transport.Protocol),
                                 transport.HasUnsentMessages ? "sending" : "0");

#if !NO_UNITY
                    yield return new WaitForSeconds(0.1f);
#else
                    Thread.Sleep(100);
#endif
                }
            }

            StopTransport(transport);
        }

        void OnTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            if (TransportEventCallback != null)
                TransportEventCallback(protocol, type);
        }

        void OnTransportError (TransportProtocol protocol, TransportError.Type type, string message)
        {
            if (TransportErrorCallback == null)
                return;

            TransportError error = new TransportError();
            error.type = type;
            error.message = message;

            TransportErrorCallback(protocol, error);
        }

        bool IsReliableTransport (TransportProtocol protocol)
        {
            return reliable_session_ && protocol == TransportProtocol.kTcp;
        }

        bool IsSendingSequence (Transport transport)
        {
            if (transport == null || transport.Protocol == TransportProtocol.kUdp)
                return false;

            return transport.SequenceNumberValidation;
        }

        Transport GetTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

#if !NO_UNITY
            FunDebug.Log("GetTransport - Can't find '{0}' transport.",
                         ConvertString(protocol));
#endif
            return null;
        }

        Transport FindConnectedTransport (TransportProtocol except_protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.Count <= 0)
                    return null;

                foreach (Transport transport in transports_.Values)
                {
                    if (transport.Protocol != except_protocol && transport.Started)
                    {
                        return transport;
                    }
                }
            }

            return null;
        }


        //
        // Transport-related callback functions
        //
        void OnTransportConnectionFailed (TransportProtocol protocol)
        {
            FunDebug.Log("{0} transport connection failed.", ConvertString(protocol));

            CheckTransportStatus(protocol);
            OnTransportEvent(protocol, TransportEventType.kConnectionFailed);
        }

        void OnConnectionTimedOut (TransportProtocol protocol)
        {
            FunDebug.Log("{0} transport connection timed out.", ConvertString(protocol));

            StopTransport(GetTransport(protocol));

            OnTransportEvent(protocol, TransportEventType.kConnectionTimedOut);
        }

        void OnTransportDisconnected (TransportProtocol protocol)
        {
            FunDebug.Log("{0} transport disconnected.", ConvertString(protocol));

            CheckTransportStatus(protocol);
            OnTransportEvent(protocol, TransportEventType.kDisconnected);
        }

        void OnTransportStarted (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            lock (state_lock_)
            {
                if (session_id_.Length > 0)
                {
                    state_ = State.kConnected;

                    if (IsReliableTransport(protocol) && seq_recvd_ != 0)
                    {
                        transport.state = Transport.State.kWaitForAck;
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
                    transport.state = Transport.State.kWaitForSession;

                    // To get a session id
                    SendFirstMessage(transport);
                }
                else if (state_ == State.kWaitForSession)
                {
                    transport.state = Transport.State.kWaitForSession;
                }
            }
        }

        void OnTransportStopped (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            FunDebug.Log("{0} transport stopped.", ConvertString(protocol));

            CheckTransportStatus(protocol);
            OnTransportEvent(protocol, TransportEventType.kStopped);
        }

        void OnTransportFailure (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            OnTransportError(protocol, transport.LastErrorCode, transport.LastErrorMessage);
        }


        //
        // Sending-related functions
        //
        void SendFirstMessage (Transport transport)
        {
            if (transport == null)
                return;

            first_sending_protocol_ = transport.Protocol;

            FunDebug.DebugLog("{0} sending a empty message for getting to session id.",
                              ConvertString(transport.Protocol));

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

        void SendAck (Transport transport, UInt32 ack)
        {
            if (!Connected || transport == null)
                return;

            FunDebug.DebugLog("{0} send ack message - ack:{1}", ConvertString(transport.Protocol), ack);

            if (transport.Encoding == FunEncoding.kJson)
            {
                object ack_msg = FunapiMessage.Deserialize("{}");
                FunapiMessage.JsonHelper.SetStringField(ack_msg, kSessionIdField, session_id_);
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

        void SendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            FunDebug.Log("SendUnsentMessages - {0} unsent messages.", unsent_queue_.Count);

            foreach (FunapiMessage msg in unsent_queue_)
            {
                Transport transport = GetTransport(msg.protocol);
                if (transport == null || transport.state != Transport.State.kEstablished)
                {
                    FunDebug.Log("SendUnsentMessages - {0} transport is invalid. '{1}' message skipped.",
                                 ConvertString(msg.protocol), msg.msg_type);
                    continue;
                }

                bool reliable_transport = IsReliableTransport(transport.Protocol);
                bool sending_sequence = IsSendingSequence(transport);

                if (transport.Encoding == FunEncoding.kJson)
                {
                    object json = msg.message;

                    // Encodes a messsage type
                    FunapiMessage.JsonHelper.SetStringField(json, kMessageTypeField, msg.msg_type);

                    if (session_id_.Length > 0)
                        FunapiMessage.JsonHelper.SetStringField(json, kSessionIdField, session_id_);

                    if (reliable_transport || sending_sequence)
                    {
                        UInt32 seq = GetNextSeq(transport.Protocol);
                        FunapiMessage.JsonHelper.SetIntegerField(json, kSeqNumberField, seq);

                        if (reliable_transport)
                            send_queue_.Enqueue(msg);

                        FunDebug.Log("{0} send unsent message - msgtype:{1} seq:{2}",
                                     ConvertString(transport.Protocol), msg.msg_type, seq);
                    }
                    else
                    {
                        FunDebug.Log("{0} send unsent message - msgtype:{1}",
                                     ConvertString(transport.Protocol), msg.msg_type);
                    }
                }
                else if (transport.Encoding == FunEncoding.kProtobuf)
                {
                    FunMessage pbuf = msg.message as FunMessage;
                    pbuf.msgtype = msg.msg_type;

                    if (session_id_.Length > 0)
                        pbuf.sid = session_id_;

                    if (reliable_transport || sending_sequence)
                    {
                        pbuf.seq = GetNextSeq(transport.Protocol);

                        if (reliable_transport)
                            send_queue_.Enqueue(msg);

                        FunDebug.Log("{0} send unsent message - msgtype:{1} seq:{2}",
                                     ConvertString(transport.Protocol), msg.msg_type, pbuf.seq);
                    }
                    else
                    {
                        FunDebug.Log("{0} send unsent message - msgtype:{1}",
                                     ConvertString(transport.Protocol), msg.msg_type);
                    }
                }

                transport.SendMessage(msg);
            }

            unsent_queue_.Clear();
        }


        //
        // Receiving-related functions
        //
        void OnTransportReceived (FunapiMessage message)
        {
            FunDebug.DebugLog("OnTransportReceived - type: {0}", message.msg_type);

            lock (message_lock_)
            {
                message_buffer_.Add(message);
            }
        }

        void OnProcessMessage (string msg_type, object message)
        {
            if (msg_type == kSessionOpenedType)
            {
                return;
            }
            else if (msg_type == kSessionClosedType)
            {
                FunDebug.Log("Session timed out. Resetting session id.");

                StopAllTransports();
                CloseSession();
            }
            else
            {
                RemoveResponseTimeout(msg_type);

                if (ReceivedMessageCallback != null)
                    ReceivedMessageCallback(msg_type, message);
            }
        }

        void ProcessMessage (FunapiMessage msg)
        {
            object message = msg.message;
            if (message == null)
            {
                FunDebug.Log("ProcessMessage - '{0}' message is null.", msg.msg_type);
                return;
            }

            Transport transport = GetTransport(msg.protocol);
            if (transport == null)
                return;

            string msg_type = msg.msg_type;
            string session_id = "";

            if (transport.Encoding == FunEncoding.kJson)
            {
                try
                {
                    session_id = FunapiMessage.JsonHelper.GetStringField(message, kSessionIdField) as string;
                    FunapiMessage.JsonHelper.RemoveStringField(message, kSessionIdField);
                    SetSessionId(session_id);

                    if (IsReliableTransport(msg.protocol))
                    {
                        if (FunapiMessage.JsonHelper.HasField(message, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)FunapiMessage.JsonHelper.GetIntegerField(message, kAckNumberField);
                            OnAckReceived(transport, ack);
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
                    FunDebug.LogError("Failure in ProcessMessage: {0}", e.ToString());
                    return;
                }

                if (msg_type.Length > 0)
                {
                    OnProcessMessage(msg_type, message);
                }
            }
            else if (transport.Encoding == FunEncoding.kProtobuf)
            {
                FunMessage funmsg = message as FunMessage;

                try
                {
                    session_id = funmsg.sid;
                    SetSessionId(session_id);

                    if (IsReliableTransport(msg.protocol))
                    {
                        if (funmsg.ackSpecified)
                        {
                            OnAckReceived(transport, funmsg.ack);
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
                    FunDebug.LogError("Failure in ProcessMessage: {0}", e.ToString());
                    return;
                }

                if (msg_type.Length > 0)
                {
                    OnProcessMessage(msg_type, funmsg);
                }
            }
            else
            {
                FunDebug.LogWarning("The encoding type is invalid. type: {0}", transport.Encoding);
                return;
            }

            if (transport.state == Transport.State.kWaitForAck && session_id_.Length > 0)
            {
                SetTransportStarted(transport);
            }
        }


        //
        // Serial-number-related functions
        //
        bool OnSeqReceived (Transport transport, UInt32 seq)
        {
            if (transport == null)
                return false;

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
                    string message = string.Format("Received wrong sequence number {0}. {1} expected.", seq, seq_recvd_ + 1);
                    FunDebug.LogError(message);

                    StopTransport(transport);
                    OnTransportError(transport.Protocol, TransportError.Type.kInvalidSequence, message);
                    return false;
                }
            }

            seq_recvd_ = seq;

            SendAck(transport, seq_recvd_ + 1);

            return true;
        }

        void OnAckReceived (Transport transport, UInt32 ack)
        {
            if (!Connected || transport == null)
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
                    FunDebug.LogWarning("The encoding type is invalid. type: {0}", transport.Encoding);
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

            if (transport.state == Transport.State.kWaitForAck)
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
                            FunDebug.LogWarning("The encoding type is invalid. type: {0}", transport.Encoding);
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

        UInt32 GetNextSeq (TransportProtocol protocol)
        {
            if (protocol == TransportProtocol.kTcp)
            {
                return ++tcp_seq_;
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                return ++http_seq_;
            }

            return 0;
        }

        // Serial-number arithmetic
        bool SeqLess (UInt32 x, UInt32 y)
        {
            // 아래 참고
            //  - http://en.wikipedia.org/wiki/Serial_number_arithmetic
            //  - RFC 1982
            return (Int32)(y - x) > 0;
        }

        // Convert to protocol string
        string ConvertString (TransportProtocol protocol)
        {
            if (protocol == TransportProtocol.kTcp)
                return "TCP";
            else if (protocol == TransportProtocol.kUdp)
                return "UDP";
            else if (protocol == TransportProtocol.kHttp)
                return "HTTP";

            return "";
        }


        // Delegates
        public delegate void SessionEventHandler (SessionEventType type, string session_id);
        public delegate void TransportEventHandler (TransportProtocol protocol, TransportEventType type);
        public delegate void TransportErrorHandler (TransportProtocol protocol, TransportError type);
        public delegate void ReceivedMessageHandler (string msg_type, object message);
        public delegate void ResponseTimeoutHandler (string msg_type);

        // Funapi message-related events.
        public event SessionEventHandler SessionEventCallback;
        public event TransportEventHandler TransportEventCallback;
        public event TransportErrorHandler TransportErrorCallback;
        public event ReceivedMessageHandler ReceivedMessageCallback;
        public event ResponseTimeoutHandler ResponseTimeoutCallback;

        // Message-type-related constants.
        const string kMessageTypeField = "_msgtype";
        const string kSessionIdField = "_sid";
        const string kSeqNumberField = "_seq";
        const string kAckNumberField = "_ack";
        const string kSessionOpenedType = "_session_opened";
        const string kSessionClosedType = "_session_closed";

        class ExpectedResponse
        {
            public ExpectedResponse (string type, float wait_time)
            {
                this.type = type;
                this.wait_time = wait_time;
            }

            public string type = "";
            public float wait_time = 0f;
        }

        enum State
        {
            kUnknown = 0,
            kStarted,
            kConnected,
            kWaitForSession,
            kStopped
        };


        State state_;
        object state_lock_ = new object();
        string server_address_ = "";

        // Session-related variables.
        string session_id_ = "";
        bool reliable_session_ = false;
        TransportProtocol first_sending_protocol_;
        static System.Random rnd_ = new System.Random();

        // Serial-number-related variables.
        UInt32 tcp_seq_ = 0;
        UInt32 http_seq_ = 0;
        UInt32 seq_recvd_ = 0;
        bool first_receiving_ = false;

        // Transport-related variables.
        object transports_lock_ = new object();
        TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        Dictionary<TransportProtocol, Transport> transports_ = new Dictionary<TransportProtocol, Transport>();

        // Message-related variables.
        object message_lock_ = new object();
        object expected_response_lock = new object();
        Queue<FunapiMessage> send_queue_ = new Queue<FunapiMessage>();
        Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();
        List<FunapiMessage> message_buffer_ = new List<FunapiMessage>();
        Dictionary<string, ExpectedResponse> expected_responses_ = new Dictionary<string, ExpectedResponse>();
    }
}
