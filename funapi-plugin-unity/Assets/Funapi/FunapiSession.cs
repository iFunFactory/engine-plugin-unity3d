// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
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

    // Session option
    public class SessionOption
    {
        public bool sessionReliability = false;
        public bool sendSessionIdOnlyOnce = false;
        public int redirectTimeout = 10;
    }


    public partial class FunapiSession : FunapiUpdater
    {
        //
        // Create an instance of FunapiSession.
        //
        public static FunapiSession Create (string hostname_or_ip, SessionOption option = null)
        {
            if (option == null)
                option = new SessionOption();

            return new FunapiSession(hostname_or_ip, option);
        }

        [System.Obsolete("This will be deprecated October 2017. Please use 'FunapiSession.Create(string, SessionOption)' instead.")]
        public static FunapiSession Create (string hostname_or_ip, bool session_reliability)
        {
            SessionOption option = new SessionOption();
            option.sessionReliability = session_reliability;

            return new FunapiSession(hostname_or_ip, option);
        }

        private FunapiSession (string hostname_or_ip, SessionOption option)
        {
            FunDebug.Assert(option != null);

            state_ = State.kUnknown;
            server_address_ = hostname_or_ip;
            option_ = option;

            Log("SessionReliability: {0}, SendSessionIdOnlyOnce: {1}",
                option.sessionReliability, option.sendSessionIdOnlyOnce);

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
                DebugLog1("Starting a session module.");

                lock (state_lock_)
                {
                    state_ = State.kStarted;
                }

                createUpdater();
            }

            Transport transport = GetTransport(protocol);
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
            Transport transport = GetTransport(protocol);
            if (transport == null || mono == null)
                return;

#if !NO_UNITY
            mono.StartCoroutine(tryToStopTransport(transport));
#else
            mono.StartCoroutine(() => tryToStopTransport(transport));
#endif
        }


        public Dictionary<TransportProtocol, HostAddr> GetAddressList()
        {
            Dictionary<TransportProtocol, HostAddr> list = new Dictionary<TransportProtocol, HostAddr>();
            lock (transports_lock_)
            {
                foreach (Transport t in transports_.Values)
                    list.Add(t.protocol, t.address);
            }

            return list;
        }

        public bool HasTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                return transports_.ContainsKey(protocol);
            }
        }

        public Transport GetTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

            return null;
        }


#if PROTOBUF_ENUM_STRING_LEGACY
        public void SendMessage (MessageType msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 EncryptionType enc_type = EncryptionType.kDefaultEncryption)
        {
            SendMessage(MessageTable.Lookup(msg_type), message, protocol, enc_type);
        }
#else
        public void SendMessage (MessageType msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 EncryptionType enc_type = EncryptionType.kDefaultEncryption)
        {
            SendMessage(kIntMessageType + ((int)msg_type).ToString(), message, protocol, enc_type);
        }
#endif

        public void SendMessage (int msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 EncryptionType enc_type = EncryptionType.kDefaultEncryption)
        {
            SendMessage(kIntMessageType + msg_type.ToString(), message, protocol, enc_type);
        }

        public void SendMessage (string msg_type, object message,
                                 TransportProtocol protocol = TransportProtocol.kDefault,
                                 EncryptionType enc_type = EncryptionType.kDefaultEncryption)
        {
            if (protocol == TransportProtocol.kDefault)
                protocol = default_protocol_;

            Transport transport = GetTransport(protocol);
            bool reliable_transport = isReliableTransport(protocol);

            if (transport != null && transport.state == Transport.State.kEstablished &&
                (reliable_transport == false || unsent_queue_.Count <= 0) &&
                (wait_redirect_ == false || msg_type == kRedirectConnectType))
            {
                FunapiMessage fun_msg = null;
                bool sending_sequence = isSendingSequence(transport);

                if (transport.encoding == FunEncoding.kJson)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, json_helper_.Clone(message), enc_type);

                    if (reliable_transport || sending_sequence)
                    {
                        UInt32 seq = getNextSeq(protocol);
                        json_helper_.SetIntegerField(fun_msg.message, kSeqNumberField, seq);

                        if (reliable_transport)
                            send_queue_.Enqueue(fun_msg);

                        DebugLog1("{0} send a message - {1} (seq : {2})", transport.str_protocol, msg_type, seq);
                    }
                    else
                    {
                        DebugLog1("{0} send a message - {1}", transport.str_protocol, msg_type);
                    }
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, message, enc_type);

                    FunMessage pbuf = fun_msg.message as FunMessage;

                    if (reliable_transport || sending_sequence)
                    {
                        pbuf.seq = getNextSeq(protocol);

                        if (reliable_transport)
                            send_queue_.Enqueue(fun_msg);

                        DebugLog1("{0} send a message - {1} (seq : {2})", transport.str_protocol, msg_type, pbuf.seq);
                    }
                    else
                    {
                        DebugLog1("{0} send a message - {1}", transport.str_protocol, msg_type);
                    }
                }
                else
                {
                    LogWarning("The encoding type is invalid. type: {0}", transport.encoding);
                    return;
                }

                transport.SendMessage(fun_msg);
            }
            else if (transport != null && wait_redirect_ == false &&
                     (reliable_transport || transport.state == Transport.State.kEstablished))
            {
                FunapiMessage fun_msg = null;
                bool sending_sequence = isSendingSequence(transport);

                if (transport.encoding == FunEncoding.kJson)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, json_helper_.Clone(message), enc_type);
                    if (reliable_transport || sending_sequence)
                    {
                        UInt32 seq = getNextSeq(protocol);
                        json_helper_.SetIntegerField(fun_msg.message, kSeqNumberField, seq);
                    }
                    unsent_queue_.Enqueue(fun_msg);
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    fun_msg = new FunapiMessage(protocol, msg_type, message, enc_type);

                    if (reliable_transport || sending_sequence)
                    {
                        FunMessage pbuf = fun_msg.message as FunMessage;
                        pbuf.seq = getNextSeq(protocol);
                    }
                    unsent_queue_.Enqueue(fun_msg);
                }

                DebugLog1("SendMessage - '{0}' message queued. state:{1}", msg_type, transport.state);
            }
            else
            {
                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("SendMessage - '{0}' message skipped. ", msg_type);
                if (wait_redirect_)
                    strlog.Append("Now redirecting to another server.");
                else if (transport == null)
                    strlog.AppendFormat("There's no {0} transport.", convertString(protocol));
                else if (transport.state != Transport.State.kEstablished)
                    strlog.AppendFormat("Transport's state is '{0}'.", transport.state);

                LogWarning(strlog.ToString());
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
                DebugLog1("Expected response message added - '{0}' ({1}s)", msg_type, waiting_time);
            }
        }

        public void RemoveResponseTimeout (string msg_type)
        {
            lock (expected_response_lock)
            {
                if (expected_responses_.ContainsKey(msg_type))
                {
                    expected_responses_.Remove(msg_type);
                    DebugLog1("Expected response message removed - {0}", msg_type);
                }
            }
        }


        //
        // Properties
        //
        public bool ReliableSession
        {
            get { return option_.sessionReliability; }
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

        // ping time in milliseconds
        public int PingTime
        {
            get
            {
                Transport transport = GetTransport(TransportProtocol.kTcp);
                if (transport != null)
                    return transport.PingTime;

                return 0;
            }
        }

        int transportCount
        {
            get { lock (transports_lock_) { return transports_.Count; } }
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
                    DebugLog1("Update messages. count: {0}", message_buffer_.Count);

                    foreach (FunapiMessage msg in message_buffer_)
                    {
                        Transport transport = GetTransport(msg.protocol);
                        if (transport != null)
                        {
                            if (msg.message != null)
                            {
                                if (!string.IsNullOrEmpty(msg.msg_type))
                                    DebugLog2("{0} received message - '{1}'", transport.str_protocol, msg.msg_type);

                                processMessage(transport, msg);
                            }

                            if (transport.state == Transport.State.kWaitForAck && session_id_.IsValid)
                            {
                                setTransportStarted(transport);
                            }
                        }
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
                            LogWarning("'{0}' message waiting time has been exceeded.", er.type);
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
                if (option_.sessionReliability || wait_redirect_)
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
        void initSession ()
        {
            session_id_.Clear();

            if (option_.sessionReliability)
            {
                seq_recvd_ = 0;
                send_queue_.Clear();
                first_receiving_ = true;
            }

            tcp_seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            http_seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
        }

        void setSessionId (object session_id)
        {
            if (!session_id_.IsValid)
            {
                session_id_.SetId(session_id);
                Log("New session id: {0}", (string)session_id_);

                onSessionOpened();
            }
            else if (session_id_ != session_id)
            {
                if (session_id is byte[] && session_id_.IsStringArray &&
                    (session_id as byte[]).Length == SessionId.kArrayLength)
                {
                    session_id_.SetId(session_id);
                }
                else
                {
                    Log("Session id changed: {0} => {1}", (string)session_id_, SessionId.ToString(session_id));

                    session_id_.SetId(session_id);
                    onSessionEvent(SessionEventType.kChanged);
                }
            }
        }

        void onSessionOpened ()
        {
            lock (state_lock_)
            {
                state_ = State.kConnected;
            }

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

            onSessionEvent(SessionEventType.kOpened);
        }

        void onSessionClosed ()
        {
            unsent_queue_.Clear();

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.SetAbolish();
                }
            }

            if (!session_id_.IsValid)
                return;

            onSessionEvent(SessionEventType.kClosed);

            initSession();
        }

        void onSessionEvent (SessionEventType type)
        {
            if (wait_redirect_)
            {
                Log("Redirect: Session event ({0}).\nThis event callback is skipped.", type);
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

            // Stopping all transports.
            stopAllTransports();

#if !NO_UNITY
            mono.StartCoroutine(tryToRedirect(host, list));
#else
            mono.StartCoroutine(() => tryToRedirect(host, list));
#endif
            return true;
        }

#if !NO_UNITY
        IEnumerator tryToRedirect (string host, List<RedirectInfo> list)
#else
        void tryToRedirect (string host, List<RedirectInfo> list)
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

            onSessionClosed();

            // Removes transports.
            lock (transports_lock_)
            {
                transports_.Clear();
            }
            default_protocol_ = TransportProtocol.kDefault;

            // Adds transports.
            foreach (RedirectInfo info in list)
            {
                Log("Redirect: {0} - {1}:{2}, {3}", convertString(info.protocol),
                    server_address_, info.port, convertString(info.encoding));

                Connect(info.protocol, info.encoding, info.port, info.option);
            }

            // Convers seconds to ticks.
            long redirect_timeout = DateTime.UtcNow.Ticks + (option_.redirectTimeout * 1000 * 10000);

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

                if (DateTime.UtcNow.Ticks > redirect_timeout)
                {
                    LogWarning("Redirect: Connection timed out. " +
                               "Stops redirecting to another server. ({0})", host);
                    break;
                }

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
                Transport transport = GetTransport(default_protocol_);
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
                json_helper_.SetStringField(msg, "token", token);
                SendMessage(kRedirectConnectType, msg, transport.protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunRedirectConnectMessage msg = new FunRedirectConnectMessage();
                msg.token = token;
                FunMessage funmsg = FunapiMessage.CreateFunMessage(msg, MessageType._cs_redirect_connect);
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
            Transport transport = GetTransport(protocol);
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

                if (wait_redirect_)
                {
                    Log("createTransport - {0} transport use the 'default option'.\n" +
                        "If you want to use your option, please set FunapiSession.TransportOptionCallback function.",
                        convertString(protocol));
                }
                else
                {
                    Log("createTransport - {0} transport use the 'default option'.", convertString(protocol));
                }
            }

            if (protocol == TransportProtocol.kTcp)
            {
                TcpTransport tcp_transport = new TcpTransport(server_address_, port, encoding, option);
                transport = tcp_transport;
            }
            else if (protocol == TransportProtocol.kUdp)
            {
                transport = new UdpTransport(server_address_, port, encoding, option);
            }
            else if (protocol == TransportProtocol.kHttp)
            {
                bool https = ((HttpTransportOption)option).HTTPS;
                HttpTransport http_transport = new HttpTransport(server_address_, port, https, encoding, option);
                transport = http_transport;
            }
            else
            {
                LogError("createTransport - {0} is invalid protocol type.", convertString(protocol));
                return null;
            }

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

            DebugLog1("{0} transport was created.", transport.str_protocol);
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
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            DebugLog1("Starting {0} transport.", transport.str_protocol);

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

            DebugLog1("{0} Stopping transport.", transport.str_protocol);

            transport.Stop();
        }

        void setTransportStarted (Transport transport, bool send_unsent = true)
        {
            if (transport == null)
                return;

            transport.SetEstablish(session_id_, option_.sendSessionIdOnlyOnce);

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
            DebugLog1("Stopping a session module.");

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
                DebugLog1("Waiting for process before {0} transport to stop... ({1})",
                          transport.str_protocol, transport.HasUnsentMessages ? "sending" : "0");

#if !NO_UNITY
                yield return new WaitForSeconds(0.1f);
#else
                Thread.Sleep(100);
#endif
            }

            stopTransport(transport);
        }

        void onTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            if (wait_redirect_)
            {
                Log("Redirect: {0} transport ({1}).\nThis event callback is skipped.",
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
                LogWarning("Redirect: {0} error ({1})\nThis event callback is skipped.\n{2}.",
                           convertString(protocol), type, message);
                return;
            }

            LogWarning("ERROR: {0} transport ({1})\n{2}.", convertString(protocol), type, message);

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
            return option_.sessionReliability && protocol == TransportProtocol.kTcp;
        }

        bool isSendingSequence (Transport transport)
        {
            if (transport == null || transport.protocol == TransportProtocol.kUdp)
                return false;

            return transport.SequenceValidation;
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
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            lock (state_lock_)
            {
                if (session_id_.IsValid)
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
                    // If there are other protocols, UDP dose not send the first session message.
                    if (protocol == TransportProtocol.kUdp && transportCount > 1)
                    {
                        transport.state = Transport.State.kWaitForSessionId;
                    }
                    else
                    {
                        state_ = State.kWaitForSessionId;
                        transport.state = Transport.State.kWaitForSessionId;

                        // To get a session id
                        sendFirstMessage(transport);
                    }
                }
                else if (state_ == State.kWaitForSessionId)
                {
                    transport.state = Transport.State.kWaitForSessionId;
                }
            }
        }

        void onTransportStopped (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            DebugLog1("{0} transport stopped.", transport.str_protocol);

            checkTransportStatus(protocol);
            onTransportEvent(protocol, TransportEventType.kStopped);
        }

        void onTransportError (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            onTransportError(protocol, transport.LastErrorCode, transport.LastErrorMessage);
        }

        void onConnectionFailed (TransportProtocol protocol)
        {
            LogWarning("{0} transport connection failed.", convertString(protocol));

            checkTransportStatus(protocol);
            onTransportEvent(protocol, TransportEventType.kConnectionFailed);
        }

        void onConnectionTimedOut (TransportProtocol protocol)
        {
            LogWarning("{0} transport connection timed out.", convertString(protocol));

            stopTransport(GetTransport(protocol));

            onTransportEvent(protocol, TransportEventType.kConnectionTimedOut);
        }

        void onDisconnected (TransportProtocol protocol)
        {
            LogWarning("{0} transport disconnected.", convertString(protocol));

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

            DebugLog1("{0} sending a empty message for getting to session id.", transport.str_protocol);

            transport.SendMessage(new FunapiMessage(transport.protocol, "_first"));
        }

        void sendAck (Transport transport, UInt32 ack)
        {
            if (!Connected || transport == null)
                return;

            DebugLog1("{0} sending ack - {1}", transport.str_protocol, ack);

            if (transport.encoding == FunEncoding.kJson)
            {
                object ack_msg = FunapiMessage.Deserialize("{}");
                json_helper_.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport.SendMessage(new FunapiMessage(transport.protocol, kAckNumberField, ack_msg));
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.ack = ack;
                transport.SendMessage(new FunapiMessage(transport.protocol, kAckNumberField, ack_msg));
            }
        }

        void sendUnsentMessages ()
        {
            if (unsent_queue_.Count <= 0)
                return;

            DebugLog1("sendUnsentMessages - {0} unsent messages.", unsent_queue_.Count);

            Queue<FunapiMessage> remained_queue = null;

            foreach (FunapiMessage msg in unsent_queue_)
            {
                Transport transport = GetTransport(msg.protocol);
                if (transport == null || transport.state != Transport.State.kEstablished)
                {
                    if (remained_queue == null)
                        remained_queue = new Queue<FunapiMessage>();

                    remained_queue.Enqueue(msg);

                    DebugLog1("sendUnsentMessages - {0} transport is invalid. " +
                              "will try again '{1}' message next time.",
                              convertString(msg.protocol), msg.msg_type);

                    continue;
                }

                bool reliable_transport = isReliableTransport(transport.protocol);
                bool sending_sequence = isSendingSequence(transport);

                if (reliable_transport || sending_sequence)
                {
                    if (reliable_transport)
                        send_queue_.Enqueue(msg);

                    UInt32 seq = 0;
                    if (transport.encoding == FunEncoding.kJson)
                        seq = (UInt32)json_helper_.GetIntegerField(msg.message, kSeqNumberField);
                    else if (transport.encoding == FunEncoding.kProtobuf)
                        seq = (msg.message as FunMessage).seq;

                    DebugLog1("{0} send a unsent message - '{1}' (seq : {2})",
                              transport.str_protocol, msg.msg_type, seq);
                }
                else
                {
                    DebugLog1("{0} send a unsent message - '{1}'", transport.str_protocol, msg.msg_type);
                }

                transport.SendMessage(msg);
            }

            unsent_queue_.Clear();

            if (remained_queue != null)
            {
                unsent_queue_ = remained_queue;
            }
        }


        //
        // Receiving-related functions
        //
        void onTransportReceived (FunapiMessage message)
        {
            lock (message_lock_)
            {
                message_buffer_.Add(message);
            }
        }

        void processMessage (Transport transport, FunapiMessage msg)
        {
            try
            {
                if (transport.encoding == FunEncoding.kJson)
                {
                    if (json_helper_.HasField(msg.message, kSessionIdField))
                    {
                        string session_id = json_helper_.GetStringField(msg.message, kSessionIdField);
                        json_helper_.RemoveField(msg.message, kSessionIdField);
                        setSessionId(session_id);
                    }

                    if (isReliableTransport(msg.protocol))
                    {
                        if (json_helper_.HasField(msg.message, kAckNumberField))
                        {
                            UInt32 ack = (UInt32)json_helper_.GetIntegerField(msg.message, kAckNumberField);
                            onAckReceived(transport, ack);
                            return;
                        }

                        if (json_helper_.HasField(msg.message, kSeqNumberField))
                        {
                            UInt32 seq = (UInt32)json_helper_.GetIntegerField(msg.message, kSeqNumberField);
                            if (!onSeqReceived(transport, seq))
                                return;

                            json_helper_.RemoveField(msg.message, kSeqNumberField);
                        }
                    }
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    FunMessage funmsg = msg.message as FunMessage;

                    if (funmsg.sidSpecified)
                        setSessionId(funmsg.sid);

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
                else
                {
                    LogWarning("The encoding type is invalid. type: {0}", transport.encoding);
                    return;
                }
            }
            catch (Exception e)
            {
                LogError("Failure in Session.processMessage: {0}", e.ToString());
                return;
            }

            if (msg.msg_type.Length > 0)
            {
                if (msg.msg_type == kRedirectType)
                {
                    onRedirectMessage(transport, msg.message);
                    return;
                }
                else if (msg.msg_type == kRedirectConnectType)
                {
                    onRedirectResultMessage(transport, msg.message);
                    return;
                }

                onProcessMessage(transport.encoding, msg.msg_type, msg.message);
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
                FunRedirectMessage redirect = FunapiMessage.GetMessage<FunRedirectMessage>(msg, MessageType._sc_redirect);
                if (redirect == null)
                    return;

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
                FunRedirectConnectMessage redirect = FunapiMessage.GetMessage<FunRedirectConnectMessage>(msg, MessageType._cs_redirect_connect);
                if (redirect == null)
                    return;

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

        void onProcessMessage (FunEncoding encoding, string msg_type, object message)
        {
            if (msg_type == kSessionOpenedType)
            {
                return;
            }
            else if (msg_type == kSessionClosedType)
            {
                Log("Session has been closed by server.");

                if (wait_redirect_)
                    return;

                stopAllTransports(true);
                onSessionClosed();
            }
            else if (msg_type == kMaintenanceType)
            {
                if (MaintenanceCallback != null)
                    MaintenanceCallback(encoding, message);
            }
            else if (msg_type == kMulticastMsgType)
            {
                if (MulticastMessageCallback != null)
                    MulticastMessageCallback(msg_type, message);
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

            DebugLog1("{0} Received sequence number - {1}", transport.str_protocol, seq);

            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                if (!seqLess(seq_recvd_, seq))
                {
                    LogWarning("Last sequence number is {0} but {1} received. Skipping message.", seq_recvd_, seq);
                    return false;
                }
                else if (seq != seq_recvd_ + 1)
                {
                    string message = string.Format("Received wrong sequence number {0}. {1} expected.", seq, seq_recvd_ + 1);
                    LogWarning(message);

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

            DebugLog1("{0} Received ack number - {1}", transport.str_protocol, ack);

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

                    Log("Resending {0} messages.", send_queue_.Count);
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

        static string convertString (FunEncoding encoding)
        {
            if (encoding == FunEncoding.kJson)
                return "Json";
            else if (encoding == FunEncoding.kProtobuf)
                return "Protobuf";

            return "";
        }

        static string convertString (EncryptionType type)
        {
            if (type == EncryptionType.kDummyEncryption)
                return "Dummy";
            else if (type == EncryptionType.kIFunEngine1Encryption)
                return "Ife1";
            else if (type == EncryptionType.kIFunEngine2Encryption)
                return "Ife2";
            else if (type == EncryptionType.kChaCha20Encryption)
                return "ChaCha20";
            else if (type == EncryptionType.kAes128Encryption)
                return "Aes128";

            return "None";
        }


        // Message-type-related constants.
        const string kIntMessageType = "_int#";
        const string kMessageTypeField = "_msgtype";
        const string kSessionIdField = "_sid";
        const string kSeqNumberField = "_seq";
        const string kAckNumberField = "_ack";
        const string kSessionOpenedType = "_session_opened";
        const string kSessionClosedType = "_session_closed";
        const string kMaintenanceType = "_maintenance";
        const string kMulticastMsgType = "_multicast";
        const string kRedirectType = "_sc_redirect";
        const string kRedirectConnectType = "_cs_redirect_connect";

        // Delegates
        public delegate void SessionEventHandler (SessionEventType type, string session_id);
        public delegate TransportOption TransportOptionHandler (string flavor, TransportProtocol protocol);
        public delegate void TransportEventHandler (TransportProtocol protocol, TransportEventType type);
        public delegate void TransportErrorHandler (TransportProtocol protocol, TransportError type);
        public delegate void MaintenanceHandler (FunEncoding encoding, object message);
        public delegate void ReceivedMessageHandler (string msg_type, object message);
        public delegate void ResponseTimeoutHandler (string msg_type);

        // Funapi message-related events.
        public event SessionEventHandler SessionEventCallback;
        public event TransportOptionHandler TransportOptionCallback;
        public event TransportEventHandler TransportEventCallback;
        public event TransportErrorHandler TransportErrorCallback;
        public event ReceivedMessageHandler ReceivedMessageCallback;
        public event ReceivedMessageHandler MulticastMessageCallback;
        public event ResponseTimeoutHandler ResponseTimeoutCallback;
        public event MaintenanceHandler MaintenanceCallback;

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
        SessionId session_id_ = new SessionId();
        SessionOption option_ = null;
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
