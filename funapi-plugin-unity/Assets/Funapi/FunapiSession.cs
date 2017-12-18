﻿// Copyright 2013 iFunFactory Inc. All Rights Reserved.
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
        kOpened,                // Session opened
        kStopped,               // Session stopped
        kClosed,                // Session closed
        kConnected,             // All transports connected
        kRedirectStarted,       // Server move started
        kRedirectSucceeded,     // Server move successful
        kRedirectFailed         // Server move failed
    };

    // Session option
    public class SessionOption
    {
        // Session-reliability-related variables
        public bool sessionReliability = false;
        public float delayedAckInterval = 0f;    // seconds

        // Session-Id-related variables
        public bool sendSessionIdOnlyOnce = false;

        // Redirect-related variables
        public int redirectTimeout = 10;    // seconds
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

            Log("Plugin:{0} Protocol:{1} Reliability: {2}, SessionIdOnce: {3}",
                FunapiVersion.kPluginVersion, FunapiVersion.kProtocolVersion,
                option.sessionReliability, option.sendSessionIdOnlyOnce);
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

            transport.sendSessionIdOnlyOnce = option_.sendSessionIdOnlyOnce;
            transport.delayedAckInterval = option_.delayedAckInterval;

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

            string str_protocol = convertString(protocol);

            lock (connect_lock_)
            {
                if (connect_queue_.Contains(protocol))
                {
                    LogWarning("{0} is already waiting for a connection.", str_protocol);
                    return;
                }
            }

            Transport transport = GetTransport(protocol);
            if (transport == null)
            {
                LogWarning("Session.Connect({0}) - There's no {1} transport. " +
                           "You should call FunapiSession.Connect(protocol, encoding, ...) function.",
                           str_protocol, str_protocol);
                return;
            }

            if (transport.Connected)
            {
                LogWarning("Session.Connect({0}) - {1} has been already connected.",
                           str_protocol, str_protocol);
                return;
            }

            DebugLog1("Session.Connect({0}) called. Ready to connect {1} transport.",
                      str_protocol, str_protocol);

            lock (connect_lock_)
            {
                connect_queue_.Enqueue(protocol);
            }
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
            DebugLog1("Session.Stop() called. (state:{0})", state_);

            if (!Started)
            {
                LogWarning("Session.Stop() - The session is not connected.");
                return;
            }

            stopAllTransports();
        }

        public void Stop (TransportProtocol protocol)
        {
            string str_protocol = convertString(protocol);
            DebugLog1("Session.Stop({0}) called. (state:{1})", str_protocol, state_);

            if (!Started)
            {
                LogWarning("Session.Stop({0}) - The session is not connected.", str_protocol);
                return;
            }

            Transport transport = GetTransport(protocol);
            if (transport == null)
            {
                LogWarning("Session.Stop({0}) - Can't find the {1} transport.", str_protocol, str_protocol);
                return;
            }

            if (transport.state == Transport.State.kUnknown)
            {
                LogWarning("Session.Stop({0}) - {1} has been already stopped.", str_protocol, str_protocol);
                return;
            }

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

            if (transport != null &&
                (transport.IsReliable || transport.IsEstablished) &&
                (wait_redirect_ == false || msg_type == kRedirectConnectType))
            {
                FunapiMessage msg = null;

                if (transport.encoding == FunEncoding.kJson)
                {
                    msg = new FunapiMessage(protocol, msg_type, json_helper_.Clone(message), enc_type);
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    msg = new FunapiMessage(protocol, msg_type, message, enc_type);
                }

                transport.SendMessage(msg);
            }
            else
            {
                StringBuilder strlog = new StringBuilder();
                strlog.AppendFormat("SendMessage - '{0}' message skipped. ", msg_type);
                if (wait_redirect_)
                    strlog.Append("Now redirecting to another server.");
                else if (transport == null)
                    strlog.AppendFormat("There's no {0} transport.", convertString(protocol));
                else if (!transport.IsEstablished)
                    strlog.AppendFormat(" {0}:{1}", transport.str_protocol, transport.state);
                strlog.AppendFormat(" session:{0}", state_);

                LogWarning(strlog.ToString());

                if (DroppedMessageCallback != null)
                    DroppedMessageCallback(msg_type, message);
            }
        }

        public void SetResponseTimeout (string msg_type, float waiting_time)
        {
            if (msg_type == null || msg_type.Length <= 0)
                return;

            lock (expected_responses_)
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
            lock (expected_responses_)
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

        public string GetSessionId ()
        {
            return (string)session_id_;
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

            checkConnectionRequest();

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.Update(deltaTime);
                }
            }

            if (!Started)
                return true;

            updateMessages();
            updateExpectedResponse(deltaTime);

            checkDisconnectionRequest();

            return true;
        }

        protected override void onPaused (bool paused)
        {
            Log("Session {0}.", paused ? "paused" : "resumed");

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.OnPaused(paused);
                }
            }
        }

        protected override void onQuit ()
        {
            stopAllTransports(true);
            onSessionEvent(SessionEventType.kStopped);
        }



        //
        // Update-related functions
        //
        void checkConnectionRequest ()
        {
            lock (connect_lock_)
            {
                if (connect_queue_.Count <= 0)
                    return;

                Queue<TransportProtocol> list = new Queue<TransportProtocol>(connect_queue_);
                connect_queue_.Clear();

                foreach (TransportProtocol protocol in list)
                {
                    Transport transport = GetTransport(protocol);
                    if (transport != null)
                    {
                        if (transport.protocol == TransportProtocol.kHttp)
                        {
                            ((HttpTransport)transport).mono = mono;
                        }

                        transport.Start();
                    }
                }
            }
        }

        void checkDisconnectionRequest ()
        {
            lock (connect_lock_)
            {
                if (disconnect_queue_.Count <= 0)
                    return;

                Queue<TransportProtocol> list = new Queue<TransportProtocol>(disconnect_queue_);
                disconnect_queue_.Clear();

                foreach (TransportProtocol protocol in list)
                {
                    Transport transport = GetTransport(protocol);
                    if (transport != null)
                    {
                        transport.Stop();
                    }
                }
            }
        }

        void updateMessages ()
        {
            lock (message_buffer_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugLog1("Update messages. count: {0}", message_buffer_.Count);

                    foreach (ReceivedMessage msg in message_buffer_)
                    {
                        Transport transport = GetTransport(msg.protocol);
                        if (transport != null)
                        {
                            if (!string.IsNullOrEmpty(msg.msg_type))
                                DebugLog2("{0} received message - '{1}'", transport.str_protocol, msg.msg_type);

                            onProcessMessage(transport, msg);
                        }
                    }

                    message_buffer_.Clear();
                }
            }
        }

        void updateExpectedResponse (float deltaTime)
        {
            lock (expected_responses_)
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

            lock (expected_responses_)
            {
                expected_responses_.Clear();
            }

            onSessionEvent(SessionEventType.kStopped);
        }


        //
        // Session-related functions
        //
        bool setSessionId (Transport transport, object new_id)
        {
            if (!session_id_.IsValid)
            {
                if (prev_session_id_.Equals(new_id))
                    return false;

                session_id_.SetId(new_id);
                prev_session_id_.SetId(new_id);

                Log("New session id: {0}", (string)session_id_);

                onSessionOpened();
            }
            else if (session_id_ != new_id)
            {
                if (option_.sendSessionIdOnlyOnce && transport.protocol == TransportProtocol.kUdp)
                {
                    transport.SendSessionId = true;
                    LogWarning("UDP received a wrong session id. Sends the previous session id again. current:{0} received:{1}",
                               (string)session_id_, SessionId.ToString(new_id));
                    return false;
                }

                if (new_id is byte[] && session_id_.IsStringArray &&
                    (new_id as byte[]).Length == SessionId.kArrayLength)
                {
                    session_id_.SetId(new_id);
                    prev_session_id_.SetId(new_id);
                    Log("Change the session id string to bytes: {0}", (string)session_id_);
                }
                else
                {
                    LogWarning("Received a wrong session id. This message is ignored.\ncurrent:{0} received:{1}",
                               (string)session_id_, SessionId.ToString(new_id));
                    return false;
                }
            }

            return true;
        }

        void onSessionOpened ()
        {
            lock (transports_lock_)
            {
                foreach (Transport t in transports_.Values)
                {
                    if (t.Connected && t.IsStandby)
                    {
                        setTransportStarted(t);
                    }
                }
            }

            onSessionEvent(SessionEventType.kOpened);

            checkAllTransportConnected();
        }

        void onSessionClosed ()
        {
            session_id_.Clear();

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.SetAbolish();
                }
            }

            onSessionEvent(SessionEventType.kClosed);
        }

        void onSessionEvent (SessionEventType type)
        {
            if (wait_redirect_)
            {
                Log("Redirect: Session event ({0}).\nThis event callback is skipped.", type);
                return;
            }

            Log("EVENT: Session ({0}).", type);

            event_list.Add (delegate
            {
                if (SessionEventCallback != null)
                    SessionEventCallback(type, session_id_);
            });
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
#if !NO_UNITY
            yield return null;
#endif

            // Wait for stop.
            while (Started)
            {
#if !NO_UNITY
                yield return new WaitForSeconds(0.1f);
#else
                Thread.Sleep(100);
#endif
            }

            lock (transports_lock_)
            {
                transports_.Clear();
                Log("Redirect: Removes all transports.");
            }

            onSessionClosed();

            default_protocol_ = TransportProtocol.kDefault;

            // Adds transports.
            foreach (RedirectInfo info in list)
            {
                Log("Redirect: {0} - {1}:{2}, {3}", convertString(info.protocol),
                    server_address_, info.port, convertString(info.encoding));

                Connect(info.protocol, info.encoding, info.port, info.option);
            }

            // Converts seconds to ticks.
            long redirect_timeout = DateTime.UtcNow.Ticks + (option_.redirectTimeout * 1000 * 10000);

            // Wait for connect.
            while (true)
            {
                bool be_wait = false;
                lock (transports_lock_)
                {
                    foreach (Transport transport in transports_.Values)
                    {
                        if (!transport.Connected || transport.Connecting || transport.Reconnecting)
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
                    if (!transport.IsEstablished)
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
            Transport transport = getTransport(protocol, encoding, port, option);
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

            if (option_.sessionReliability && protocol == TransportProtocol.kTcp)
                option.ReliableTransport = true;
            else
                option.ReliableTransport = false;

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

        public Transport getTransport (TransportProtocol protocol, FunEncoding encoding,
                                       UInt16 port, TransportOption option)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return null;

            if (transport.address.host != server_address_ || transport.address.port != port)
                return null;

            if (transport.encoding != encoding || !transport.option.Equals(option))
                return null;

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

        void setTransportStarted (Transport transport)
        {
            if (transport == null)
                return;

            transport.SetEstablish(session_id_);

            onTransportEvent(transport.protocol, TransportEventType.kStarted);
        }

        void stopAllTransports (bool force_stop = false)
        {
            DebugLog1("Stopping a session module.");

            if (force_stop)
            {
                // Stops all transport
                lock (connect_lock_)
                {
                    lock (transports_lock_)
                    {
                        foreach (TransportProtocol protocol in transports_.Keys)
                        {
                            if (!disconnect_queue_.Contains(protocol))
                                disconnect_queue_.Enqueue(protocol);
                        }
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
#if !NO_UNITY
            yield return null;
#endif

            if (transport == null || !transport.Connected)
#if !NO_UNITY
                yield break;
#else
                return;
#endif

            lock (connect_lock_)
            {
                if (disconnect_queue_.Contains(transport.protocol))
                {
                    LogWarning("{0} is already waiting to be stopped.", transport.str_protocol);
#if !NO_UNITY
                    yield break;
#else
                    return;
#endif
                }
            }

            // Converts seconds to ticks.
            long wait_timeout = DateTime.UtcNow.Ticks + (kWaitForStopTimeout * 1000 * 10000);

            // Checks transport's state.
            while (transport.InProcess)
            {
                DebugLog1("Waiting for process before {0} transport to stop... ({1})",
                          transport.str_protocol, transport.HasUnsentMessages ? "sending" : "0");

                if (DateTime.UtcNow.Ticks > wait_timeout)
                {
                    LogWarning("Timed out to stop the {0} transport. state:{1} unsent:{2}",
                               transport.str_protocol, transport.state, transport.HasUnsentMessages);
                    break;
                }

#if !NO_UNITY
                yield return new WaitForSeconds(0.1f);
#else
                Thread.Sleep(100);
#endif
            }

            lock (connect_lock_)
            {
                disconnect_queue_.Enqueue(transport.protocol);
            }
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

            event_list.Add (delegate
            {
                if (TransportEventCallback != null)
                    TransportEventCallback(protocol, type);
            });
        }

        void onTransportError (TransportProtocol protocol, TransportError.Type type, string message)
        {
            if (wait_redirect_)
            {
                LogWarning("Redirect: {0} error ({1})\nThis event callback is skipped.\n{2}.",
                           convertString(protocol), type, message);
                return;
            }

            event_list.Add (delegate
            {
                if (TransportErrorCallback != null)
                {
                    TransportError error = new TransportError();
                    error.type = type;
                    error.message = message;

                    TransportErrorCallback(protocol, error);
                }
            });
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
                    if (transport.Connected && transport.IsStandby)
                    {
                        setTransportStarted(transport);
                    }

                    checkAllTransportConnected();
                }
                else if (state_ != State.kWaitForSessionId)
                {
                    // If there is TCP protocol, then TCP send the first session message.
                    // Priority order : TCP > HTTP > UDP
                    if (protocol == TransportProtocol.kTcp ||
                        (protocol == TransportProtocol.kUdp && transportCount == 1) ||
                        (protocol == TransportProtocol.kHttp && !HasTransport(TransportProtocol.kTcp)))
                    {
                        state_ = State.kWaitForSessionId;
                        transport.SendMessage(new FunapiMessage(protocol, kEmptyMessageType), true);
                    }
                }
            }
        }

        void onTransportStopped (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            DebugLog1("{0} transport stopped.", transport.str_protocol);
            onTransportEvent(protocol, TransportEventType.kStopped);

            checkAllTransportStopped();
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
            onTransportEvent(protocol, TransportEventType.kConnectionFailed);

            checkAllTransportStopped();
        }

        void onConnectionTimedOut (TransportProtocol protocol)
        {
            LogWarning("{0} transport connection timed out.", convertString(protocol));

            lock (connect_lock_)
            {
                if (disconnect_queue_.Contains(protocol))
                {
                    LogWarning("{0} is already waiting to be stopped.", convertString(protocol));
                    return;
                }

                disconnect_queue_.Enqueue(protocol);
            }

            onTransportEvent(protocol, TransportEventType.kConnectionTimedOut);
        }

        void onDisconnected (TransportProtocol protocol)
        {
            LogWarning("{0} transport disconnected.", convertString(protocol));
            onTransportEvent(protocol, TransportEventType.kDisconnected);

            checkAllTransportStopped();
        }

        void checkAllTransportConnected ()
        {
            if (Connected)
                return;

            // Checks that all transport have been stopped.
            lock (transports_lock_)
            {
                foreach (Transport t in transports_.Values)
                {
                    if (!t.IsEstablished)
                        return;
                }
            }

            lock (state_lock_)
            {
                state_ = State.kConnected;
            }

            onSessionEvent(SessionEventType.kConnected);
        }

        void checkAllTransportStopped ()
        {
            if (!Started)
                return;

            // Checks that all transport have been stopped.
            lock (transports_lock_)
            {
                foreach (Transport t in transports_.Values)
                {
                    if (t.Connected || t.Reconnecting)
                        return;
                }
            }

            onStopped();
        }


        //
        // Receiving-related functions
        //
        void onTransportReceived (TransportProtocol protocol, string msg_type, object message)
        {
            lock (message_buffer_)
            {
                message_buffer_.Add(new ReceivedMessage(protocol, msg_type, message));
            }
        }

        void onProcessMessage (Transport transport, ReceivedMessage msg)
        {
            // Checks session id
            try
            {
                if (transport.encoding == FunEncoding.kJson)
                {
                    if (json_helper_.HasField(msg.message, kSessionIdField))
                    {
                        string session_id = json_helper_.GetStringField(msg.message, kSessionIdField);
                        if (!setSessionId(transport, session_id))
                            return;
                    }
                }
                else if (transport.encoding == FunEncoding.kProtobuf)
                {
                    FunMessage funmsg = msg.message as FunMessage;
                    if (funmsg.sidSpecified)
                    {
                        if (!setSessionId(transport, funmsg.sid))
                            return;
                    }
                }
            }
            catch (Exception e)
            {
                LogError("Failure in Session.onProcessMessage: {0}", e.ToString());
                return;
            }

            // Processes a message
            if (msg.msg_type.Length > 0)
            {
                switch (msg.msg_type)
                {
                case kSessionOpenedType:
                    return;

                case kSessionClosedType:
                    {
                        Log("Session has been closed by server.");

                        if (wait_redirect_)
                            return;

                        stopAllTransports(true);
                        onSessionClosed();
                    }
                    break;

                case kRedirectType:
                    onRedirectMessage(transport, msg.message);
                    return;

                case kRedirectConnectType:
                    onRedirectResultMessage(transport, msg.message);
                    return;

                case kMaintenanceType:
                    if (MaintenanceCallback != null)
                        MaintenanceCallback(transport.encoding, msg.message);
                    break;

                case kMulticastMsgType:
                    if (MulticastMessageCallback != null)
                        MulticastMessageCallback(msg.msg_type, msg.message);
                    break;

                default:
                    {
                        RemoveResponseTimeout(msg.msg_type);

                        if (ReceivedMessageCallback != null)
                            ReceivedMessageCallback(msg.msg_type, msg.message);
                    }
                    break;
                }
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


        // constants
        const int kWaitForStopTimeout = 3;

        // Message-type-related constants.
        const string kIntMessageType = "_int#";
        const string kMessageTypeField = "_msgtype";
        const string kSessionIdField = "_sid";
        const string kSeqNumberField = "_seq";
        const string kAckNumberField = "_ack";
        const string kEmptyMessageType = "_empty";
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
        public delegate void DroppedMessageHandler (string msg_type, object message);
        public delegate void ResponseTimeoutHandler (string msg_type);

        // Funapi message-related events.
        public event SessionEventHandler SessionEventCallback;
        public event TransportOptionHandler TransportOptionCallback;
        public event TransportEventHandler TransportEventCallback;
        public event TransportErrorHandler TransportErrorCallback;
        public event ReceivedMessageHandler ReceivedMessageCallback;
        public event ReceivedMessageHandler MulticastMessageCallback;
        public event DroppedMessageHandler DroppedMessageCallback;
        public event ResponseTimeoutHandler ResponseTimeoutCallback;
        public event MaintenanceHandler MaintenanceCallback;

        class ReceivedMessage
        {
            public TransportProtocol protocol;
            public string msg_type;
            public object message;

            public ReceivedMessage (TransportProtocol protocol, string msg_type, object message)
            {
                this.protocol = protocol;
                this.msg_type = msg_type;
                this.message = message;
            }
        }

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
        SessionId prev_session_id_ = new SessionId();
        SessionOption option_ = null;

        // Redirect-related variables.
        bool wait_redirect_ = false;
        string redirect_token_ = "";

        // Transport-related variables.
        object connect_lock_ = new object();
        object transports_lock_ = new object();
        TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        Queue<TransportProtocol> connect_queue_ = new Queue<TransportProtocol>();
        Queue<TransportProtocol> disconnect_queue_ = new Queue<TransportProtocol>();
        Dictionary<TransportProtocol, Transport> transports_ = new Dictionary<TransportProtocol, Transport>();

        // Message-related variables.
        static JsonAccessor json_helper_ = FunapiMessage.JsonHelper;
        List<ReceivedMessage> message_buffer_ = new List<ReceivedMessage>();
        Dictionary<string, ExpectedResponse> expected_responses_ = new Dictionary<string, ExpectedResponse>();
    }
}
