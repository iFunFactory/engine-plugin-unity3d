﻿// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
#if !NO_UNITY
using UnityEngine;
#else
using System.Threading;
#endif

// protobuf
using funapi.network.fun_message;
using funapi.service.redirect_message;


namespace Fun
{
    public enum SessionEventType
    {
        kOpened,
        kChanged,
        kStopped,
        kClosed,
        kRedirectStarted,
        kRedirectSucceeded,
        kRedirectFailed
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

            initSession();
        }

        //
        // Public functions.
        //
        public void Connect (TransportProtocol protocol, FunEncoding encoding,
                             UInt16 port, TransportOption option = null)
        {
            Transport transport = createTransport(protocol, encoding, port, option);
            if (transport == null)
                return;

            Connect(protocol);
        }

        public void Connect (TransportProtocol protocol)
        {
            if (!Started)
            {
                Log("Starting a network module.");

                lock (state_lock_)
                {
                    state_ = State.kStarted;
                }

                createUpdater();
            }

            Transport transport = getTransport(protocol);
            if (transport == null || transport.Started)
                return;

            event_list.Add(() => startTransport(protocol));
        }

        public void Reconnect ()
        {
            lock (transports_lock_)
            {
                foreach (TransportProtocol protocol in transports_.Keys)
                {
                    Connect(protocol);
                }
            }
        }

        public void Stop ()
        {
            if (!Started)
                return;

            stopAllTransports();
        }

        public void Stop (TransportProtocol protocol)
        {
            Transport transport = getTransport(protocol);
            if (transport == null || mono == null)
                return;

#if !NO_UNITY
            mono.StartCoroutine(tryToStopTransport(transport));
#else
            mono.StartCoroutine(() => tryToStopTransport(transport));
#endif
        }

        [System.Obsolete("This will be deprecated January 2017. Use 'FunapiSession.Stop()' instead.")]
        public void Close ()
        {
            Stop();
        }

        [System.Obsolete("This will be deprecated January 2017. Use 'FunapiSession.Stop(TransportProtocol)' instead.")]
        public void Close (TransportProtocol protocol)
        {
            Stop(protocol);
        }

        public void SendMessage (MessageType msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 EncryptionType enc_type = EncryptionType.kDefaultEncryption)
        {
            SendMessage(MessageTable.Lookup(msg_type), message, protocol, enc_type);
        }

        public void SendMessage (string msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 EncryptionType enc_type = EncryptionType.kDefaultEncryption)
        {
            if (protocol == TransportProtocol.kDefault)
                protocol = default_protocol_;

            Transport transport = getTransport(protocol);
            bool reliable_transport = isReliableTransport(protocol);

            if (transport != null && transport.state == Transport.State.kEstablished &&
                (reliable_transport == false || unsent_queue_.Count <= 0))
            {
                FunapiMessage fun_msg = null;
                bool sending_sequence = isSendingSequence(transport);

                if (transport.encoding == FunEncoding.kJson)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, json_helper_.Clone(message), enc_type);

                    // Encodes a messsage type
                    json_helper_.SetStringField(fun_msg.message, kMessageTypeField, msg_type);

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        json_helper_.SetStringField(fun_msg.message, kSessionIdField, session_id_);
                    }

                    if (reliable_transport || sending_sequence)
                    {
                        UInt32 seq = getNextSeq(protocol);
                        json_helper_.SetIntegerField(fun_msg.message, kSeqNumberField, seq);

                        if (reliable_transport)
                            send_queue_.Enqueue(fun_msg);

                        DebugLog("{0} send message - msgtype:{1} seq:{2}",
                                 convertString(protocol), msg_type, seq);
                    }
                    else
                    {
                        DebugLog("{0} send message - msgtype:{1}",
                                 convertString(protocol), msg_type);
                    }
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, message, enc_type);

                    FunMessage pbuf = fun_msg.message as FunMessage;
                    pbuf.msgtype = msg_type;

                    // Encodes a session id, if any.
                    if (session_id_.Length > 0)
                    {
                        pbuf.sid = session_id_;
                    }

                    if (reliable_transport || sending_sequence)
                    {
                        pbuf.seq = getNextSeq(protocol);

                        if (reliable_transport)
                            send_queue_.Enqueue(fun_msg);

                        DebugLog("{0} send message - msgtype:{1} seq:{2}",
                                 convertString(protocol), msg_type, pbuf.seq);
                    }
                    else
                    {
                        DebugLog("{0} send message - msgtype:{1}",
                                 convertString(protocol), msg_type);
                    }
                }
                else
                {
                    LogWarning("The encoding type is invalid. type: {0}", transport.encoding);
                    return;
                }

                transport.SendMessage(fun_msg);
            }
            else if (transport != null &&
                     (reliable_transport || transport.state == Transport.State.kEstablished))
            {
                if (transport.encoding == FunEncoding.kJson)
                {
                    unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, json_helper_.Clone(message), enc_type));
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    unsent_queue_.Enqueue(new FunapiMessage(protocol, msg_type, message, enc_type));
                }

                Log("SendMessage - '{0}' message queued.", msg_type);
            }
            else
            {
                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("SendMessage - '{0}' message skipped. ", msg_type);
                if (transport == null)
                    strlog.AppendFormat("There's no {0} transport.", convertString(protocol));
                else if (transport.state != Transport.State.kEstablished)
                    strlog.AppendFormat("Transport's state is '{0}'.", transport.state);

                Log(strlog.ToString());
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
                    LogWarning("'{0}' expected response type is already added. Ignored.");
                    return;
                }

                expected_responses_[msg_type] = new ExpectedResponse(msg_type, waiting_time);
                DebugLog("Expected response message added - '{0}' ({1}s)", msg_type, waiting_time);
            }
        }

        public void RemoveResponseTimeout (string msg_type)
        {
            lock (expected_response_lock)
            {
                if (expected_responses_.ContainsKey(msg_type))
                {
                    expected_responses_.Remove(msg_type);
                    DebugLog("Expected response message removed - {0}", msg_type);
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
                  Log("The default protocol is '{0}'", convertString(value)); }
        }

        public bool Started
        {
            get { lock (state_lock_) { return state_ != State.kUnknown && state_ != State.kStopped; } }
        }

        public bool Connected
        {
            get { lock (state_lock_) { return state_ == State.kConnected; } }
        }

        public bool HasUnsentMessages
        {
            get
            {
                lock (transports_lock_)
                {
                    foreach (Transport transport in transports_.Values)
                    {
                        if (transport.HasUnsentMessages)
                            return true;
                    }
                }

                return false;
            }
        }


        //
        // Derived function from FunapiUpdater
        //
        protected override bool onUpdate (float deltaTime)
        {
            if (!base.onUpdate(deltaTime))
                return false;

            if (!Started)
                return true;

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.Update(deltaTime);
                }
            }

            updateMessages();
            updateExpectedResponse(deltaTime);

            return true;
        }

        protected override void onQuit ()
        {
            lock (state_lock_)
            {
                state_ = State.kUnknown;
            }

            stopAllTransports(true);
            onSessionEvent(SessionEventType.kStopped);
        }



        //
        // Update-close-related functions
        //
        void updateMessages ()
        {
            lock (message_lock_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugLog("Update messages. count: {0}", message_buffer_.Count);

                    foreach (FunapiMessage message in message_buffer_)
                    {
                        processMessage(message);
                    }

                    message_buffer_.Clear();
                }
            }
        }

        void updateExpectedResponse (float deltaTime)
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
                            Log("'{0}' message waiting time has been exceeded.", er.type);
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

        void onStopped ()
        {
            if (!Started)
                return;

            lock (state_lock_)
            {
                if (reliable_session_ || wait_redirect_)
                    state_ = State.kStopped;
                else
                    state_ = State.kUnknown;
            }

            if (!wait_redirect_)
            {
                releaseUpdater();
            }

            updateMessages();

            lock (expected_response_lock)
            {
                expected_responses_.Clear();
            }

            onSessionEvent(SessionEventType.kStopped);
        }


        //
        // Session-related functions
        //
        void initSession()
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

        void setSessionId (string session_id)
        {
            if (session_id_.Length == 0)
            {
                Log("New session id: {0}", session_id);
                openSession(session_id);
                onSessionEvent(SessionEventType.kOpened);
            }

            if (session_id_ != session_id)
            {
                Log("Session id changed: {0} => {1}", session_id_, session_id);

                session_id_ = session_id;
                onSessionEvent(SessionEventType.kChanged);
            }
        }

        void openSession (string session_id)
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
                    if (transport.state == Transport.State.kWaitForSessionId)
                    {
                        setTransportStarted(transport, false);
                    }
                }
            }

            if (unsent_queue_.Count > 0)
            {
                sendUnsentMessages();
            }
        }

        void closeSession ()
        {
            unsent_queue_.Clear();

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.SetAbolish();
                }
            }

            if (session_id_.Length == 0)
                return;

            onSessionEvent(SessionEventType.kClosed);

            initSession();
        }

        void onSessionEvent (SessionEventType type)
        {
            if (wait_redirect_)
            {
                DebugLog("Session - '{0}' event callback skipped.", type);
                return;
            }

            Log("EVENT: Session ({0}).", type);

            if (SessionEventCallback != null)
                SessionEventCallback(type, session_id_);
        }


        //
        // Redirect-related functions
        //
        bool startRedirect (string host, List<RedirectInfo> list)
        {
            // Notify start to redirect.
            onSessionEvent(SessionEventType.kRedirectStarted);

            wait_redirect_ = true;
            server_address_ = host;

            // Stopping all.
            stopAllTransports(true);
            closeSession();

#if !NO_UNITY
            mono.StartCoroutine(tryToRedirect(list));
#else
            mono.StartCoroutine(() => tryToRedirect(list));
#endif
            return true;
        }

#if !NO_UNITY
        IEnumerator tryToRedirect (List<RedirectInfo> list)
#else
        void tryToRedirect (List<RedirectInfo> list)
#endif
        {
            // Wait for stop.
            while (Started)
            {
#if !NO_UNITY
                yield return new WaitForSeconds(0.1f);
#else
                Thread.Sleep(100);
#endif
            }

            // Removes transports.
            lock (transports_lock_)
            {
                transports_.Clear();
            }
            default_protocol_ = TransportProtocol.kDefault;

            // Adds transports.
            foreach (RedirectInfo info in list)
            {
                Connect(info.protocol, info.encoding, info.port, info.option);
            }

            // Wait for connect.
            while (true)
            {
                bool be_wait = false;
                lock (transports_lock_)
                {
                    foreach (Transport transport in transports_.Values)
                    {
                        if (!transport.Started || transport.Connecting || transport.Reconnecting)
                        {
                            be_wait = true;
                            break;
                        }
                    }
                }

                if (!be_wait)
                    break;
#if !NO_UNITY
                yield return new WaitForSeconds(0.2f);
#else
                Thread.Sleep(200);
#endif
            }

            // Check success.
            bool succeeded = true;
            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    if (transport.state != Transport.State.kEstablished)
                    {
                        succeeded = false;
                        break;
                    }
                }
            }

            if (succeeded)
            {
                // Sending token.
                Transport transport = getTransport(default_protocol_);
                sendRedirectToken(transport, redirect_token_);
            }
            else
            {
                wait_redirect_ = false;
                onRedirectFailed();
            }
        }

        void sendRedirectToken (Transport transport, string token)
        {
            if (transport == null || token.Length <= 0)
                return;

            if (transport.encoding == FunEncoding.kJson)
            {
                object msg = FunapiMessage.Deserialize("{}");
                json_helper_.SetStringField(msg, kMessageTypeField, kRedirectConnectType);
                json_helper_.SetStringField(msg, kSessionIdField, session_id_);
                json_helper_.SetStringField(msg, "token", token);
                SendMessage(kRedirectConnectType, msg, transport.protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunRedirectConnectMessage msg = new FunRedirectConnectMessage();
                msg.token = token;
                FunMessage funmsg = FunapiMessage.CreateFunMessage(msg, MessageType._cs_redirect_connect);
                funmsg.msgtype = kRedirectConnectType;
                funmsg.sid = session_id_;
                SendMessage(kRedirectConnectType, funmsg, transport.protocol);
            }
        }

        void onRedirectFailed ()
        {
            Stop();
            onSessionEvent(SessionEventType.kRedirectFailed);
        }


        //
        // Transport-related functions
        //
        Transport createTransport (TransportProtocol protocol, FunEncoding encoding,
                                   UInt16 port, TransportOption option = null)
        {
            Transport transport = getTransport(protocol);
            if (transport != null)
                return transport;

            if (option == null)
            {
                if (protocol == TransportProtocol.kTcp)
                    option = new TcpTransportOption();
                else if (protocol == TransportProtocol.kUdp)
                    option = new TransportOption();
                else if (protocol == TransportProtocol.kHttp)
                    option = new HttpTransportOption();

                Log("{0} transport use the 'default option'.", convertString(protocol));
            }

            if (protocol == TransportProtocol.kTcp)
            {
                TcpTransport tcp_transport = new TcpTransport(server_address_, port, encoding);
                transport = tcp_transport;
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                transport = new UdpTransport(server_address_, port, encoding);
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                bool https = ((HttpTransportOption)option).Https;
                HttpTransport http_transport = new HttpTransport(server_address_, port, https, encoding);
                transport = http_transport;
            }
            else
            {
                LogError("Create a {0} transport failed.", convertString(protocol));
                return null;
            }

            transport.option = option;

            // Callback functions
            transport.StartedCallback += onTransportStarted;
            transport.StoppedCallback += onTransportStopped;
            transport.ReceivedCallback += onTransportReceived;
            transport.TransportErrorCallback += onTransportError;

            transport.ConnectionFailedCallback += onConnectionFailed;
            transport.ConnectionTimeoutCallback += onConnectionTimedOut;
            transport.DisconnectedCallback += onDisconnected;

            lock (transports_lock_)
            {
                transports_[protocol] = transport;
            }

            if (default_protocol_ == TransportProtocol.kDefault)
                DefaultProtocol = protocol;

            DebugLog("{0} transport added.", convertString(protocol));
            return transport;
        }

        TransportOption getTransportOption (string flavor, TransportProtocol protocol)
        {
            TransportOption option = null;

            // Get option from transport option callback.
            if (TransportOptionCallback != null)
                option = TransportOptionCallback(flavor, protocol);

            if (option == null)
            {
                // Find transport's option.
                lock (transports_lock_)
                {
                    if (transports_.ContainsKey(protocol))
                        option = transports_[protocol].option;
                }
            }

            return option;
        }

        void startTransport (TransportProtocol protocol)
        {
            Transport transport = getTransport(protocol);
            if (transport == null)
                return;

            Log("Starting {0} transport.", convertString(protocol));

            if (transport.protocol == TransportProtocol.kHttp)
            {
                ((HttpTransport)transport).mono = mono;
            }

            transport.Start();
        }

        void stopTransport (Transport transport)
        {
            if (transport == null || transport.state == Transport.State.kUnknown)
                return;

            Log("Stopping {0} transport.", convertString(transport.protocol));

            transport.Stop();
        }

        void setTransportStarted (Transport transport, bool send_unsent = true)
        {
            if (transport == null)
                return;

            transport.SetEstablish(session_id_);

            onTransportEvent(transport.protocol, TransportEventType.kStarted);

            if (send_unsent && unsent_queue_.Count > 0)
            {
                sendUnsentMessages();
            }
        }

        void checkTransportStatus (TransportProtocol protocol)
        {
            if (!Started)
                return;

            lock (state_lock_)
            {
                if (state_ == State.kWaitForSessionId && protocol == first_sending_protocol_)
                {
                    Transport transport = findConnectedTransport(protocol);
                    if (transport != null)
                    {
                        transport.state = Transport.State.kWaitForSessionId;
                        sendFirstMessage(transport);
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
                    if (t.Started || t.Reconnecting)
                    {
                        all_stopped = false;
                        break;
                    }
                }
            }

            if (all_stopped)
            {
                event_list.Add(() => onStopped());
            }
        }

        void stopAllTransports (bool force_stop = false)
        {
            Log("Stopping a network module.");

            if (force_stop)
            {
                // Stops all transport
                lock (transports_lock_)
                {
                    foreach (Transport transport in transports_.Values)
                    {
                        stopTransport(transport);
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
                        mono.StartCoroutine(tryToStopTransport(transport));
#else
                        mono.StartCoroutine(() => tryToStopTransport(transport));
#endif
                    }
                }
            }
        }

#if !NO_UNITY
        IEnumerator tryToStopTransport (Transport transport)
#else
        void tryToStopTransport (Transport transport)
#endif
        {
            if (transport == null)
#if !NO_UNITY
                yield break;
#else
                return;
#endif

            // Checks transport's state.
            while (transport.InProcess)
            {
                lock (state_lock_)
                {
                    Log("{0} Stop waiting... ({1})", convertString(transport.protocol),
                        transport.HasUnsentMessages ? "sending" : "0");

#if !NO_UNITY
                    yield return new WaitForSeconds(0.1f);
#else
                    Thread.Sleep(100);
#endif
                }
            }

            stopTransport(transport);
        }

        void onTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            if (wait_redirect_)
            {
                DebugLog("{0} transport - '{1}' event callback skipped.",
                         convertString(protocol), type);
                return;
            }

            Log("EVENT: {0} transport ({1}).", convertString(protocol), type);

            if (TransportEventCallback != null)
                TransportEventCallback(protocol, type);
        }

        void onTransportError (TransportProtocol protocol, TransportError.Type type, string message)
        {
            if (wait_redirect_)
            {
                DebugLog("{0} transport - '{1}' error callback skipped.\n{2}",
                         convertString(protocol), type, message);
                return;
            }

            Log("ERROR: {0} transport ({1})\n{2}.", convertString(protocol), type, message);

            if (TransportErrorCallback != null)
            {
                TransportError error = new TransportError();
                error.type = type;
                error.message = message;

                TransportErrorCallback(protocol, error);
            }
        }

        bool isReliableTransport (TransportProtocol protocol)
        {
            return reliable_session_ && protocol == TransportProtocol.kTcp;
        }

        bool isSendingSequence (Transport transport)
        {
            if (transport == null || transport.protocol == TransportProtocol.kUdp)
                return false;

            return transport.SequenceValidation;
        }

        Transport getTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

            return null;
        }

        Transport findConnectedTransport (TransportProtocol except_protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.Count <= 0)
                    return null;

                foreach (Transport transport in transports_.Values)
                {
                    if (transport.protocol != except_protocol && transport.Started)
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
        void onTransportStarted (TransportProtocol protocol)
        {
            Transport transport = getTransport(protocol);
            if (transport == null)
                return;

            lock (state_lock_)
            {
                if (session_id_.Length > 0)
                {
                    state_ = State.kConnected;

                    if (isReliableTransport(protocol) && seq_recvd_ != 0)
                    {
                        transport.state = Transport.State.kWaitForAck;
                        sendAck(transport, seq_recvd_ + 1);
                    }
                    else
                    {
                        setTransportStarted(transport);
                    }
                }
                else if (state_ == State.kStarted || state_ == State.kStopped)
                {
                    state_ = State.kWaitForSessionId;
                    transport.state = Transport.State.kWaitForSessionId;

                    // To get a session id
                    sendFirstMessage(transport);
                }
                else if (state_ == State.kWaitForSessionId)
                {
                    transport.state = Transport.State.kWaitForSessionId;
                }
            }
        }

        void onTransportStopped (TransportProtocol protocol)
        {
            Transport transport = getTransport(protocol);
            if (transport == null)
                return;

            Log("{0} transport stopped.", convertString(protocol));

            checkTransportStatus(protocol);
            onTransportEvent(protocol, TransportEventType.kStopped);
        }

        void onTransportError (TransportProtocol protocol)
        {
            Transport transport = getTransport(protocol);
            if (transport == null)
                return;

            onTransportError(protocol, transport.LastErrorCode, transport.LastErrorMessage);
        }

        void onConnectionFailed (TransportProtocol protocol)
        {
            Log("{0} transport connection failed.", convertString(protocol));

            checkTransportStatus(protocol);
            onTransportEvent(protocol, TransportEventType.kConnectionFailed);
        }

        void onConnectionTimedOut (TransportProtocol protocol)
        {
            Log("{0} transport connection timed out.", convertString(protocol));

            stopTransport(getTransport(protocol));

            onTransportEvent(protocol, TransportEventType.kConnectionTimedOut);
        }

        void onDisconnected (TransportProtocol protocol)
        {
            Log("{0} transport disconnected.", convertString(protocol));

            checkTransportStatus(protocol);
            onTransportEvent(protocol, TransportEventType.kDisconnected);
        }


        //
        // Sending-related functions
        //
        void sendFirstMessage (Transport transport)
        {
            if (transport == null)
                return;

            first_sending_protocol_ = transport.protocol;

            DebugLog("{0} sending a empty message for getting to session id.",
                     convertString(transport.protocol));

            if (transport.encoding == FunEncoding.kJson)
            {
                object msg = FunapiMessage.Deserialize("{}");
                transport.SendMessage(new FunapiMessage(transport.protocol, "", msg));
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = new FunMessage();
                transport.SendMessage(new FunapiMessage(transport.protocol, "", msg));
            }
        }

        void sendAck (Transport transport, UInt32 ack)
        {
            if (!Connected || transport == null)
                return;

            DebugLog("{0} send ack message - ack:{1}",
                     convertString(transport.protocol), ack);

            if (transport.encoding == FunEncoding.kJson)
            {
                object ack_msg = FunapiMessage.Deserialize("{}");
                json_helper_.SetStringField(ack_msg, kSessionIdField, session_id_);
                json_helper_.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport.SendMessage(new FunapiMessage(transport.protocol, "", ack_msg));
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.sid = session_id_;
                ack_msg.ack = ack;
                transport.SendMessage(new FunapiMessage(transport.protocol, "", ack_msg));
            }
        }

        void sendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            Log("sendUnsentMessages - {0} unsent messages.", unsent_queue_.Count);

            foreach (FunapiMessage msg in unsent_queue_)
            {
                Transport transport = getTransport(msg.protocol);
                if (transport == null || transport.state != Transport.State.kEstablished)
                {
                    Log("sendUnsentMessages - {0} transport is invalid. '{1}' message skipped.",
                        convertString(msg.protocol), msg.msg_type);
                    continue;
                }

                bool reliable_transport = isReliableTransport(transport.protocol);
                bool sending_sequence = isSendingSequence(transport);

                if (transport.encoding == FunEncoding.kJson)
                {
                    object json = msg.message;

                    // Encodes a messsage type
                    json_helper_.SetStringField(json, kMessageTypeField, msg.msg_type);

                    if (session_id_.Length > 0)
                        json_helper_.SetStringField(json, kSessionIdField, session_id_);

                    if (reliable_transport || sending_sequence)
                    {
                        UInt32 seq = getNextSeq(transport.protocol);
                        json_helper_.SetIntegerField(json, kSeqNumberField, seq);

                        if (reliable_transport)
                            send_queue_.Enqueue(msg);

                        Log("{0} send unsent message - msgtype:{1} seq:{2}",
                            convertString(transport.protocol), msg.msg_type, seq);
                    }
                    else
                    {
                        Log("{0} send unsent message - msgtype:{1}",
                            convertString(transport.protocol), msg.msg_type);
                    }
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    FunMessage pbuf = msg.message as FunMessage;
                    pbuf.msgtype = msg.msg_type;

                    if (session_id_.Length > 0)
                        pbuf.sid = session_id_;

                    if (reliable_transport || sending_sequence)
                    {
                        pbuf.seq = getNextSeq(transport.protocol);

                        if (reliable_transport)
                            send_queue_.Enqueue(msg);

                        Log("{0} send unsent message - msgtype:{1} seq:{2}",
                            convertString(transport.protocol), msg.msg_type, pbuf.seq);
                    }
                    else
                    {
                        Log("{0} send unsent message - msgtype:{1}",
                            convertString(transport.protocol), msg.msg_type);
                    }
                }

                transport.SendMessage(msg);
            }

            unsent_queue_.Clear();
        }


        //
        // Receiving-related functions
        //
        void onTransportReceived (FunapiMessage message)
        {
            DebugLog("onTransportReceived - type: {0}", message.msg_type);

            lock (message_lock_)
            {
                message_buffer_.Add(message);
            }
        }

        void processMessage (FunapiMessage msg)
        {
            object message = msg.message;
            if (message == null)
            {
                Log("processMessage - '{0}' message is null.", msg.msg_type);
                return;
            }

            Transport transport = getTransport(msg.protocol);
            if (transport == null)
                return;

            string msg_type = msg.msg_type;
            string session_id = "";

            if (transport.encoding == FunEncoding.kJson)
            {
                try
                {
                    session_id = json_helper_.GetStringField(message, kSessionIdField);
                    json_helper_.RemoveField(message, kSessionIdField);
                    setSessionId(session_id);

                    if (isReliableTransport(msg.protocol))
                    {
                        if (json_helper_.HasField(message, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)json_helper_.GetIntegerField(message, kAckNumberField);
                            onAckReceived(transport, ack);
                            return;
                        }

                        if (json_helper_.HasField(message, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)json_helper_.GetIntegerField(message, kSeqNumberField);
                            if (!onSeqReceived(transport, seq))
                                return;

                            json_helper_.RemoveField(message, kSeqNumberField);
                        }
                    }
                }
                catch (Exception e)
                {
                    LogError("Failure in processMessage: {0}", e.ToString());
                    return;
                }
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunMessage funmsg = message as FunMessage;

                try
                {
                    session_id = funmsg.sid;
                    setSessionId(session_id);

                    if (isReliableTransport(msg.protocol))
                    {
                        if (funmsg.ackSpecified)
                        {
                            onAckReceived(transport, funmsg.ack);
                            return;
                        }

                        if (funmsg.seqSpecified)
                        {
                            if (!onSeqReceived(transport, funmsg.seq))
                                return;
                        }
                    }
                }
                catch (Exception e)
                {
                    LogError("Failure in processMessage: {0}", e.ToString());
                    return;
                }
            }
            else
            {
                LogWarning("The encoding type is invalid. type: {0}", transport.encoding);
                return;
            }

            if (msg_type.Length > 0)
            {
                if (msg_type == kRedirectType)
                {
                    onRedirectMessage(transport, message);
                    return;
                }
                else if (msg_type == kRedirectConnectType)
                {
                    onRedirectResultMessage(transport, message);
                    return;
                }

                onProcessMessage(msg_type, message);
            }

            if (transport.state == Transport.State.kWaitForAck && session_id_.Length > 0)
            {
                setTransportStarted(transport);
            }
        }

        void onRedirectMessage (Transport transport, object message)
        {
            string host = "";
            string token = "";
            string flavor = "";
            List<RedirectInfo> info_list = new List<RedirectInfo>();

            if (transport.encoding == FunEncoding.kJson)
            {
                host = json_helper_.GetStringField(message, "host");
                token = json_helper_.GetStringField(message, "token");
                flavor = json_helper_.GetStringField(message, "flavor");

                object list = json_helper_.GetObject(message, "ports");
                int count = json_helper_.GetArrayCount(list);
                for (int i = 0; i < count; ++i)
                {
                    object item = json_helper_.GetArrayObject(list, i);
                    RedirectInfo info = new RedirectInfo();
                    info.protocol = (TransportProtocol)json_helper_.GetIntegerField(item, "protocol");
                    info.encoding = (FunEncoding)json_helper_.GetIntegerField(item, "encoding");
                    info.port = (ushort)json_helper_.GetIntegerField(item, "port");
                    info.option = getTransportOption(flavor, info.protocol);
                    info_list.Add(info);
                }
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = message as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType._sc_redirect);
                if (obj == null)
                    return;

                FunRedirectMessage redirect = obj as FunRedirectMessage;
                host = redirect.host;
                token = redirect.token;
                flavor = redirect.flavor;

                foreach (FunRedirectMessage.ServerPort item in redirect.ports)
                {
                    RedirectInfo info = new RedirectInfo();
                    info.protocol = (TransportProtocol)item.protocol;
                    info.encoding = (FunEncoding)item.encoding;
                    info.port = (ushort)item.port;
                    info.option = getTransportOption(flavor, info.protocol);
                    info_list.Add(info);
                }
            }

            if (host.Length <= 0 || token.Length <= 0)
            {
                LogWarning("onRedirectMessage - Invalid host or token.\nhost:{0}, token:{1}",
                           host, token);
                return;
            }

            if (info_list.Count <= 0)
            {
                LogWarning("onRedirectMessage - Server port list is empty.");
                return;
            }

            redirect_token_ = token;

            startRedirect(host, info_list);
        }

        void onRedirectResultMessage (Transport transport, object message)
        {
            wait_redirect_ = false;

            if (transport.encoding == FunEncoding.kJson)
            {
                RedirectResult result = (RedirectResult)json_helper_.GetIntegerField(message, "result");
                if (result == RedirectResult.kSucceeded)
                {
                    onSessionEvent(SessionEventType.kRedirectSucceeded);
                }
                else
                {
                    LogWarning("Redirect failed. error code: {0}", result);
                    onRedirectFailed();
                }
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = message as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType._cs_redirect_connect);
                if (obj == null)
                    return;

                FunRedirectConnectMessage redirect = obj as FunRedirectConnectMessage;
                if (redirect.result == FunRedirectConnectMessage.Result.OK)
                {
                    onSessionEvent(SessionEventType.kRedirectSucceeded);
                }
                else
                {
                    LogWarning("Redirect failed. error code: {0}", redirect.result);
                    onRedirectFailed();
                }
            }
        }

        void onProcessMessage (string msg_type, object message)
        {
            if (msg_type == kSessionOpenedType)
            {
                return;
            }
            else if (msg_type == kSessionClosedType)
            {
                Log("Session timed out. Resetting session id.");

                stopAllTransports(true);
                closeSession();
            }
            else
            {
                RemoveResponseTimeout(msg_type);

                if (ReceivedMessageCallback != null)
                    ReceivedMessageCallback(msg_type, message);
            }
        }


        //
        // Serial-number-related functions
        //
        bool onSeqReceived (Transport transport, UInt32 seq)
        {
            if (transport == null)
                return false;

            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                if (!seqLess(seq_recvd_, seq))
                {
                    Log("Last sequence number is {0} but {1} received. Skipping message.", seq_recvd_, seq);
                    return false;
                }
                else if (seq != seq_recvd_ + 1)
                {
                    string message = string.Format("Received wrong sequence number {0}. {1} expected.", seq, seq_recvd_ + 1);
                    LogError(message);

                    stopTransport(transport);
                    onTransportError(transport.protocol, TransportError.Type.kInvalidSequence, message);
                    return false;
                }
            }

            seq_recvd_ = seq;

            sendAck(transport, seq_recvd_ + 1);

            return true;
        }

        void onAckReceived (Transport transport, UInt32 ack)
        {
            if (!Connected || transport == null)
                return;

            DebugLog("received ack message - ack:{0}", ack);

            UInt32 seq = 0;

            while (send_queue_.Count > 0)
            {
                FunapiMessage last_msg = send_queue_.Peek();
                if (transport.encoding == FunEncoding.kJson)
                {
                    seq = (UInt32)json_helper_.GetIntegerField(last_msg.message, kSeqNumberField);
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    seq = (last_msg.message as FunMessage).seq;
                }
                else
                {
                    LogWarning("The encoding type is invalid. type: {0}", transport.encoding);
                    seq = 0;
                }

                if (seqLess(seq, ack))
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
                        if (transport.encoding == FunEncoding.kJson)
                        {
                            seq = (UInt32)json_helper_.GetIntegerField(msg.message, kSeqNumberField);
                        }
                        else if (transport.encoding == FunEncoding.kProtobuf)
                        {
                            seq = (msg.message as FunMessage).seq;
                        }
                        else
                        {
                            LogWarning("The encoding type is invalid. type: {0}", transport.encoding);
                            seq = 0;
                        }

                        if (seq == ack || seqLess(ack, seq))
                        {
                            transport.SendMessage(msg);
                        }
                        else
                        {
                            LogWarning("onAckReceived({0}) - wrong sequence number {1}. ", ack, seq);
                        }
                    }

                    Log("Resend {0} messages.", send_queue_.Count);
                }

                setTransportStarted(transport);
            }
        }

        UInt32 getNextSeq (TransportProtocol protocol)
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
        static bool seqLess (UInt32 x, UInt32 y)
        {
            // 아래 참고
            //  - http://en.wikipedia.org/wiki/Serial_number_arithmetic
            //  - RFC 1982
            return (Int32)(y - x) > 0;
        }

        // Convert to protocol string
        static string convertString (TransportProtocol protocol)
        {
            if (protocol == TransportProtocol.kTcp)
                return "TCP";
            else if (protocol == TransportProtocol.kUdp)
                return "UDP";
            else if (protocol == TransportProtocol.kHttp)
                return "HTTP";

            return "";
        }


        // Message-type-related constants.
        const string kMessageTypeField = "_msgtype";
        const string kSessionIdField = "_sid";
        const string kSeqNumberField = "_seq";
        const string kAckNumberField = "_ack";
        const string kSessionOpenedType = "_session_opened";
        const string kSessionClosedType = "_session_closed";
        const string kRedirectType = "_sc_redirect";
        const string kRedirectConnectType = "_cs_redirect_connect";

        // Delegates
        public delegate void SessionEventHandler (SessionEventType type, string session_id);
        public delegate TransportOption TransportOptionHandler (string flavor, TransportProtocol protocol);
        public delegate void TransportEventHandler (TransportProtocol protocol, TransportEventType type);
        public delegate void TransportErrorHandler (TransportProtocol protocol, TransportError type);
        public delegate void ReceivedMessageHandler (string msg_type, object message);
        public delegate void ResponseTimeoutHandler (string msg_type);

        // Funapi message-related events.
        public event SessionEventHandler SessionEventCallback;
        public event TransportOptionHandler TransportOptionCallback;
        public event TransportEventHandler TransportEventCallback;
        public event TransportErrorHandler TransportErrorCallback;
        public event ReceivedMessageHandler ReceivedMessageCallback;
        public event ResponseTimeoutHandler ResponseTimeoutCallback;

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

        class RedirectInfo
        {
            public TransportProtocol protocol;
            public FunEncoding encoding;
            public TransportOption option;
            public ushort port;
        }

        enum State
        {
            kUnknown = 0,
            kStarted,
            kConnected,
            kWaitForSessionId,
            kStopped
        };

        enum RedirectResult
        {
            kSucceeded = 0,
            kInvalidToken,
            kExpiredToken,
            kAuthFailed
        }


        State state_;
        string server_address_ = "";
        object state_lock_ = new object();

        // Session-related variables.
        string session_id_ = "";
        bool reliable_session_ = false;
        TransportProtocol first_sending_protocol_;
        static System.Random rnd_ = new System.Random();

        // Redirect-related variables.
        bool wait_redirect_ = false;
        string redirect_token_ = "";

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
        static JsonAccessor json_helper_ = FunapiMessage.JsonHelper;
        Queue<FunapiMessage> send_queue_ = new Queue<FunapiMessage>();
        Queue<FunapiMessage> unsent_queue_ = new Queue<FunapiMessage>();
        List<FunapiMessage> message_buffer_ = new List<FunapiMessage>();
        Dictionary<string, ExpectedResponse> expected_responses_ = new Dictionary<string, ExpectedResponse>();
    }
}
