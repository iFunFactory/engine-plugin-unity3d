// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// protobuf
using funapi.network.fun_message;
using funapi.service.redirect_message;


namespace Fun
{
    public enum SessionEventType
    {
        kOpened,                // Session opened
        kConnected,             // All transports connected
        kStopped,               // Session stopped
        kClosed,                // Session closed
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
        public int redirectTimeout = 10;         // seconds
        public bool useRedirectQueue = false;

        // Encryption-related variables
        public string encryptionPublicKey = null;
    }


    public partial class FunapiSession : FunapiMono.Listener
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

        public static void Destroy (FunapiSession session)
        {
            if (session == null)
                return;

            session.OnDestroy();
        }

        private FunapiSession (string hostname_or_ip, SessionOption option)
        {
            FunDebug.Assert(option != null);

            // Sets the debug log instance.
            debug.SetDebugObject(this);
            response_timeout_.debugLog = debug;

            // Sets member variables.
            state = State.kUnknown;
            server_address_ = hostname_or_ip;
            option_ = option;

            // Sets response callback handlers.
            response_timeout_.SetCallbackHandler<int>(onResponseTimeoutCallback);
            response_timeout_.SetCallbackHandler<string>(onResponseTimeoutCallback);

            // Adds this by mono listener.
            setMonoListener();

            debug.Log("[Session] Plugin:v{0}/v{1} Reliability:{2}, SessionIdOnce:{3}",
                      FunapiVersion.kPluginVersion, FunapiVersion.kProtocolVersion,
                      option.sessionReliability, option.sendSessionIdOnlyOnce);
        }

        void OnDestroy ()
        {
            if (Started)
            {
                stopAll(true);
            }

            cmd_list_.Clear();
            event_.Add(releaseMonoListener);
        }


        //
        // Public functions.
        //
        public void Connect (TransportProtocol protocol, FunEncoding encoding,
                             UInt16 port, TransportOption option = null)
        {
            Transport transport = createTransport(protocol, encoding, port, option);
            if (transport == null)
            {
                debug.LogWarning("Failed to create the {0} transport.", convertString(protocol));
                return;
            }

            transport.sendSessionIdOnlyOnce = option_.sendSessionIdOnlyOnce;
            transport.delayedAckInterval = option_.delayedAckInterval;
            transport.encryptionPublicKey = option_.encryptionPublicKey;

            addCommand(new CmdConnect(this, transport));
        }

        public void Connect (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
            {
                debug.LogWarning("Session.Connect({0}) called but can't find a {1} transport. " +
                                 "You should call FunapiSession.Connect(protocol, encoding, ...) function.",
                                 convertString(protocol), convertString(protocol));
                return;
            }

            addCommand(new CmdConnect(this, transport));
        }

        public void Reconnect ()
        {
            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    addCommand(new CmdConnect(this, transport));
                }
            }
        }

        public void Stop (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
            {
                debug.LogWarning("Session.Stop({0}) called but can't find a {1} transport.",
                                 convertString(protocol), convertString(protocol));
                return;
            }

            addCommand(new CmdStop(this, transport));
        }

        public void Stop ()
        {
            addCommand(new CmdStopAll(this));
        }

        public void CloseRequest ()
        {
            if (!Connected)
            {
                debug.Log("Session.CloseRequest() is called but the session is not connected.");
                return;
            }

            Transport transport = GetTransport();
            if (transport == null)
            {
                debug.LogWarning("Session.CloseRequest() is called but can't find a default transport.");
                return;
            }

            if (transport.encoding == FunEncoding.kJson)
            {
                SendMessage(kCloseSessionType, FunapiMessage.Deserialize("{}"), transport.protocol);
            }
            else if (transport.encoding == FunEncoding.kProtobuf)
            {
                SendMessage(kCloseSessionType, new FunMessage(), transport.protocol);
            }
        }


        //
        // FunapiMono.Listener-related functions
        //
        public override void OnUpdate (float deltaTime)
        {
            cmd_list_.Update();

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.Update(deltaTime);
                }
            }

            response_timeout_.Update(deltaTime);
        }

        public override void OnPause (bool isPaused)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPaused)
                return;
#endif
            if (!Started)
                return;

            debug.Log("Session {0}. (state:{1})", (isPaused ? "paused" : "resumed"), state_);

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    addCommand(new CmdPause(transport, isPaused));
                }
            }
        }

        public override void OnQuit ()
        {
            OnDestroy();

            // Calls Update temporarily.
            // For notification of event related to stop.
            Update(0f);
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

            // Stores messages while on the redirect.
            if (wait_for_redirect_ && option_.useRedirectQueue)
            {
                if (msg_type != kRedirectConnectType)
                {
                    addUnsentMessageQueue(msg_type, message, protocol, enc_type);
                    return;
                }
            }

            Transport transport = GetTransport(protocol);

            // 메시지를 전송 가능한 상태인지 검사
            // session reliabiliby 를 사용할 경우 연결이 끊긴 상태에서도 SendMessage를 호출하면 메시지를 저장해두었다가 다시 연결됐을 때 전송

            if (transport != null &&                                                    // 해당 프로토콜 타입의 Transport 가 있음
                (transport.IsReliable || transport.IsEstablished) &&                    // session reliabiliby 를 사용중이거나 연결된 상태
                (!wait_for_redirect_ || msg_type == kRedirectConnectType) &&            // 서버 이동 중이 아니거나 서버 이동 메시지
                (transport.encoding == FunEncoding.kJson || message is FunMessage))     // Protobuf 일 경우 메시지 타입이 FunMessage 인지 확인
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
                if (wait_for_redirect_)
                    strlog.Append("Now redirecting to another server.");
                else if (transport == null)
                    strlog.AppendFormat("There's no {0} transport.", convertString(protocol));
                else if (!transport.IsEstablished)
                    strlog.AppendFormat("{0}:{1}", transport.str_protocol, transport.state);
                else if (transport.encoding == FunEncoding.kProtobuf && message is FunMessage == false)
                    strlog.Append("The message type is not FunMessage.");
                strlog.AppendFormat(" session:{0}", state_);

                debug.LogWarning(strlog.ToString());

                if (DroppedMessageCallback != null)
                    DroppedMessageCallback(msg_type, message);
            }
        }


        //
        // Response Timeout-related functions
        //
#if PROTOBUF_ENUM_STRING_LEGACY
        public bool SetResponseTimeout (MessageType msg_type, float waiting_time)
        {
            return response_timeout_.Add(MessageTable.Lookup(msg_type), waiting_time);
        }
#else
        public bool SetResponseTimeout (MessageType msg_type, float waiting_time)
        {
            return response_timeout_.Add((int)msg_type, waiting_time);
        }
#endif

        public bool SetResponseTimeout (int msg_type, float waiting_time)
        {
            return response_timeout_.Add(msg_type, waiting_time);
        }

        public bool SetResponseTimeout (string msg_type, float waiting_time)
        {
            return response_timeout_.Add(msg_type, waiting_time);
        }

#if PROTOBUF_ENUM_STRING_LEGACY
        public bool RemoveResponseTimeout (MessageType msg_type)
        {
            return response_timeout_.Remove(MessageTable.Lookup(msg_type));
        }
#else
        public bool RemoveResponseTimeout (MessageType msg_type)
        {
            return response_timeout_.Remove((int)msg_type);
        }
#endif

        public bool RemoveResponseTimeout (int msg_type)
        {
            return response_timeout_.Remove(msg_type);
        }

        public bool RemoveResponseTimeout (string msg_type)
        {
            return response_timeout_.Remove(msg_type);
        }

        void onResponseTimeoutCallback (int msg_type)
        {
            if (ResponseTimeoutIntCallback != null)
                ResponseTimeoutIntCallback(msg_type);
        }

        void onResponseTimeoutCallback (string msg_type)
        {
            if (ResponseTimeoutCallback != null)
                ResponseTimeoutCallback(msg_type);
        }


        //
        // Command-related functions
        //
        void addCommand (Command cmd)
        {
            cmd_list_.Add(cmd);
        }

        bool connect (Transport transport)
        {
            if (transport == null)
            {
                debug.LogDebug("[Session] Connect - transport is null.");
                return false;
            }

            if (!Started)
            {
                state = State.kStarted;
                debug.LogDebug("[Session] Starting the session.");
            }

            if (transport.Connected)
            {
                debug.LogWarning("[Session] Connect - {0} has been already connected.",
                                 transport.str_protocol);
                return false;
            }

            transport.Start();

            return true;
        }

        bool stop (Transport transport)
        {
            if (transport == null)
            {
                debug.LogDebug("[Session] Stop - transport is null.");
                return false;
            }

            if (!Started)
            {
                return false;
            }

            if (transport.IsStopped)
            {
                debug.LogWarning("[Session] Stop - {0} has been already stopped.",
                                 transport.str_protocol);
                return false;
            }

            debug.LogDebug("[Session] Stopping {0} transport. (state:{1})",
                           transport.str_protocol, state_);

            if (!transport.Connected)
            {
                transport.Stop();
            }
            else
            {
                StartCoroutine(tryToStopTransport(transport));
            }

            return true;
        }

        bool stopAll (bool immediately = false)
        {
            if (!Started)
            {
                debug.LogWarning("Session.StopAll() called but the session is not connected. (state:{0})", state_);
                return false;
            }

            if (state == State.kWaitForStop)
                return false;

            debug.LogDebug("[Session] Stopping all. (state:{0})", state_);

            state = State.kWaitForStop;

            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    if (immediately || !transport.Connected)
                    {
                        transport.Stop();
                    }
                    else
                    {
                        StartCoroutine(tryToStopTransport(transport));
                    }
                }
            }

            return true;
        }

        IEnumerator onRedirect (string host)
        {
            // Wait for stop.
            while (Started)
            {
                yield return new SleepForSeconds(0.1f);
            }

            lock (transports_lock_)
            {
                transports_.Clear();
                debug.LogDebug("[Redirect] Removes all transports.");
            }

            onSessionClosed();

            server_address_ = host;
            default_protocol_ = TransportProtocol.kDefault;

            // Applying the required options to create the transport first.
            if (redirect_option_ != null)
            {
                option_.sessionReliability = redirect_option_.sessionReliability;
                option_.sendSessionIdOnlyOnce = redirect_option_.sendSessionIdOnlyOnce;
                option_.delayedAckInterval = redirect_option_.delayedAckInterval;
                option_.encryptionPublicKey = redirect_option_.encryptionPublicKey;
            }

            // Adds transports.
            foreach (RedirectInfo info in redirect_list_.Values)
            {
                Connect(info.protocol, info.encoding, info.port, info.option);
            }

            // Waits for starting connect by update.
            yield return new SleepForSeconds(0.1f);

            // Converts seconds to ticks.
            long redirect_timeout = DateTime.UtcNow.Ticks + (option_.redirectTimeout * 1000 * 10000);

            // Wait for connect.
            while (true)
            {
                if (Connected)
                    break;

                if (DateTime.UtcNow.Ticks > redirect_timeout)
                {
                    debug.LogWarning("[Redirect] Connection timed out. " +
                                     "Stops redirecting to another server. ({0})", host);
                    break;
                }

                yield return new SleepForSeconds(0.2f);
            }

            // Success check
            if (Connected)
            {
                // Sending token.
                Transport transport = GetTransport(reliable_protocol_);
                sendRedirectToken(transport, redirect_token_);
            }
            else
            {
                onRedirectFailed();
            }
        }

        void sendRedirectToken (Transport transport, string token)
        {
            if (transport == null || token.Length <= 0)
                return;

            debug.LogDebug("[Redirect] {0} sending the token to the other server.",
                           transport.str_protocol);
            debug.LogDebug("[Redirect] token: {0}", token);

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
            debug.Log("[Redirect] Failed to redirect.");

            wait_for_redirect_ = false;
            cmd_list_.Clear();

            stopAll(true);
            onSessionEvent(SessionEventType.kRedirectFailed);
        }



        //
        // Properties
        //
        public override string name
        {
            get { return "FunapiSession"; }
        }

        State state
        {
            get { lock (state_lock_) { return state_; } }
            set { lock (state_lock_) { state_ = value; } }
        }

        public bool ReliableSession
        {
            get { return option_.sessionReliability; }
        }

        public TransportProtocol DefaultProtocol
        {
            get { return default_protocol_; }
        }

        public SessionId Id
        {
            get { return session_id_; }
        }

        public string GetSessionId ()
        {
            return (string)session_id_;
        }

        public bool Started
        {
            get { return state != State.kUnknown; }
        }

        public bool Connected
        {
            get { return state == State.kConnected; }
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

        public bool HasTransport (TransportProtocol protocol = TransportProtocol.kDefault)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                if (default_protocol_ == TransportProtocol.kDefault)
                    return false;

                protocol = default_protocol_;
            }

            lock (transports_lock_)
            {
                return transports_.ContainsKey(protocol);
            }
        }

        public Transport GetTransport (TransportProtocol protocol = TransportProtocol.kDefault)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                if (default_protocol_ == TransportProtocol.kDefault)
                    return null;

                protocol = default_protocol_;
            }

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

            return null;
        }

        public FunEncoding GetEncoding (TransportProtocol protocol = TransportProtocol.kDefault)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                if (default_protocol_ == TransportProtocol.kDefault)
                    return FunEncoding.kNone;

                protocol = default_protocol_;
            }

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol].encoding;
            }

            return FunEncoding.kNone;
        }

        public TransportError.Type GetLastError (TransportProtocol protocol = TransportProtocol.kDefault)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                if (default_protocol_ == TransportProtocol.kDefault)
                    return TransportError.Type.kNone;

                protocol = default_protocol_;
            }

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol].LastErrorCode;
            }

            return TransportError.Type.kNone;
        }

        int transportCount
        {
            get { lock (transports_lock_) { return transports_.Count; } }
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

                onSessionOpened();
            }
            else if (session_id_ != new_id)
            {
                if (option_.sendSessionIdOnlyOnce && transport.protocol == TransportProtocol.kUdp)
                {
                    transport.SendSessionId = true;
                    debug.LogWarning("UDP received a wrong session id. " +
                                     "Sends the previous session id again. current:{0} received:{1}",
                                     (string)session_id_, SessionId.ToString(new_id));
                    return false;
                }

                if (new_id is byte[] && session_id_.IsStringArray &&
                    (new_id as byte[]).Length == SessionId.kArrayLength)
                {
                    session_id_.SetId(new_id);
                    prev_session_id_.SetId(new_id);

                    debug.Log("Change the session id string to bytes: {0}", (string)session_id_);
                }
                else
                {
                    debug.LogWarning("Received a wrong session id. This message will be ignored.\n" +
                                     "current:{0} received:{1}",
                                     (string)session_id_, SessionId.ToString(new_id));
                    return false;
                }
            }

            return true;
        }

        void onSessionOpened ()
        {
            onSessionEvent(SessionEventType.kOpened);

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
            if (wait_for_redirect_)
            {
                debug.Log("[Redirect] Session Event ({0}).\nThis event callback is skipped.",
                          type.ToString().Substring(1));
                return;
            }

            event_.Add(() => onSessionEventCallback(type));
        }

        void onSessionEventCallback (SessionEventType type)
        {
            debug.Log("[Session] Event ({0}).", type.ToString().Substring(1));

            if (SessionEventCallback != null)
                SessionEventCallback(type, session_id_);
        }

        SessionOption getSessionOption (string flavor)
        {
            SessionOption option = null;

            // Get option from session option callback.
            if (SessionOptionCallback != null)
                option = SessionOptionCallback(flavor);

            return option;
        }


        //
        // Transport-related functions
        //
        Transport createTransport (TransportProtocol protocol, FunEncoding encoding,
                                   UInt16 port, TransportOption option = null)
        {
            if (option == null)
            {
                if (protocol == TransportProtocol.kTcp)
                    option = new TcpTransportOption();
                else if (protocol == TransportProtocol.kHttp)
                    option = new HttpTransportOption();
                else if (protocol == TransportProtocol.kWebsocket)
                    option = new WebsocketTransportOption();
                else
                    option = new TransportOption();
            }

            if (option_.sessionReliability && protocol == TransportProtocol.kTcp)
                option.ReliableTransport = true;
            else
                option.ReliableTransport = false;

            Transport transport = getTransport(protocol, encoding, port, option);
            if (transport != null)
                return transport;

            try
            {
                if (protocol == TransportProtocol.kTcp)
                {
                    transport = new TcpTransport(server_address_, port, encoding, option);
                }
                else if (protocol == TransportProtocol.kUdp)
                {
                    transport = new UdpTransport(server_address_, port, encoding, option);
                }
                else if (protocol == TransportProtocol.kHttp)
                {
                    bool https = ((HttpTransportOption)option).HTTPS;
                    transport = new HttpTransport(server_address_, port, https, encoding, option);
                }
                else if (protocol == TransportProtocol.kWebsocket)
                {
                    transport = new WebsocketTransport(server_address_, port, encoding, option);
                }
                else
                {
                    debug.LogWarning("createTransport - {0} is invalid protocol type.",
                                     convertString(protocol));
                    return null;
                }

                // Callback functions
                transport.CreateCompressorCallback += onCreateCompressor;
                transport.EventCallback += onTransportEvent;
                transport.ErrorCallback += onTransportError;
                transport.ReceivedCallback += onTransportMessage;
                transport.mono = this;
                transport.session = this;

                transport.Init();

                lock (transports_lock_)
                    transports_[protocol] = transport;
            }
            catch (Exception e)
            {
                debug.LogWarning("Failure in createTransport: {0}", e.ToString());
                return null;
            }

            reliable_protocol_ = getTheMostReliableProtocol();

            if (default_protocol_ == TransportProtocol.kDefault ||
                default_protocol_ != reliable_protocol_)
            {
                default_protocol_ = reliable_protocol_;
            }

            return transport;
        }

        TransportProtocol getTheMostReliableProtocol ()
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(TransportProtocol.kTcp))
                    return TransportProtocol.kTcp;

                if (transports_.ContainsKey(TransportProtocol.kWebsocket))
                    return TransportProtocol.kWebsocket;

                if (transports_.ContainsKey(TransportProtocol.kHttp))
                    return TransportProtocol.kHttp;

                if (transports_.ContainsKey(TransportProtocol.kUdp))
                    return TransportProtocol.kUdp;
            }

            debug.LogWarning("Couldn't find any transport for reliable protocol.");
            return TransportProtocol.kDefault;
        }

        FunapiCompressor onCreateCompressor (TransportProtocol protocol)
        {
            if (CreateCompressorCallback != null)
                return CreateCompressorCallback(protocol);

            return null;
        }

        Transport getTransport (TransportProtocol protocol, FunEncoding encoding,
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

            onTransportEventCallback(transport.protocol, TransportEventType.kStarted);
        }

        IEnumerator tryToStopTransport (Transport transport)
        {
            yield return null;

            if (transport == null)
                yield break;

            if (transport.Connected)
            {
                // Converts seconds to ticks.
                long wait_timeout = DateTime.UtcNow.Ticks + (kWaitForStopTimeout * 1000 * 10000);

                // Checks transport's state.
                while (transport.InProcess)
                {
                    debug.LogDebug("Waiting for process before {0} transport to stop... ({1})",
                                   transport.str_protocol, transport.HasUnsentMessages ? "sending" : "0");

                    if (DateTime.UtcNow.Ticks > wait_timeout)
                    {
                        debug.LogWarning("Timed out to stop the {0} transport. state:{1} unsent:{2}",
                                         transport.str_protocol, transport.state, transport.HasUnsentMessages);
                        break;
                    }

                    yield return new SleepForSeconds(0.1f);
                }
            }

            transport.Stop();
        }


        //
        // Transport-related callback functions
        //
        void onTransportEvent (TransportProtocol protocol, TransportEventType type)
        {
            switch (type)
            {
            case TransportEventType.kStarted:
                onTransportStarted(protocol);
                break;

            case TransportEventType.kStopped:
                onTransportStopped(protocol);
                break;

            case TransportEventType.kReconnecting:
                onTransportEventCallback(protocol, type);
                break;

            default:
                debug.LogWarning("onTransportEvent - may need to handle this type '{0}'", type);
                break;
            }
        }

        void onTransportStarted (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            if (session_id_.IsValid)
            {
                if (transport.Connected && transport.IsStandby)
                {
                    setTransportStarted(transport);
                }

                checkAllTransportConnected();
            }
            else if (state != State.kWaitForSessionId)
            {
                // Sends the first message with reliable protocol to get session id.
                if (protocol == reliable_protocol_ || protocol != TransportProtocol.kUdp)
                {
                    state = State.kWaitForSessionId;
                    transport.SendMessage(new FunapiMessage(protocol, kEmptyMessageType), true);
                }
            }
        }

        void onTransportStopped (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
                return;

            onTransportEventCallback(protocol, TransportEventType.kStopped);
            checkAllTransportStopped();
        }

        void onTransportEventCallback (TransportProtocol protocol, TransportEventType type)
        {
            if (wait_for_redirect_)
            {
                debug.Log("[Redirect] {0} Event ({1}).\nThis event callback is skipped.",
                          convertString(protocol), type.ToString().Substring(1));
                return;
            }

            if (type == TransportEventType.kReconnecting)
                state = State.kReconnecting;

            event_.Add (delegate
            {
                if (!Started)
                {
                    if (type == TransportEventType.kStarted)
                    {
                        Transport transport = GetTransport(protocol);
                        if (transport != null)
                        {
                            debug.Log("{0} event callback called. But the transport has been stopped." +
                                      " state: {1} event: {2}", transport.str_protocol, transport.state, type);
                        }
                        return;
                    }
                }

                debug.Log("[{0}] Event ({1}).", convertString(protocol), type.ToString().Substring(1));

                if (TransportEventCallback != null)
                    TransportEventCallback(protocol, type);
            });
        }

        void onTransportError (TransportProtocol protocol, TransportError error)
        {
            if (wait_for_redirect_)
            {
                debug.LogWarning("[Redirect] {0} error ({1})\nThis event callback is skipped.\n{2}.",
                                 convertString(protocol), error.type, error.message);
                return;
            }

            event_.Add (delegate
            {
                debug.LogWarning("[{0}] Error ({1}).", convertString(protocol), error.type);

                if (TransportErrorCallback != null)
                    TransportErrorCallback(protocol, error);
            });
        }


        void checkAllTransportConnected ()
        {
            if (Connected)
                return;

            // Checks that all transport have been connected.
            lock (transports_lock_)
            {
                foreach (Transport t in transports_.Values)
                {
                    if (!t.IsEstablished)
                        return;
                }
            }

            state = State.kConnected;

            onSessionEvent(SessionEventType.kConnected);

            lock (transports_lock_)
            {
                if (transports_.Count > 1)
                {
                    debug.Log("[Session] default protocol: {0}", convertString(default_protocol_));
                }
            }
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
                    if (t.Connected || t.Connecting || t.Reconnecting)
                        return;
                }
            }

            state = State.kUnknown;

            response_timeout_.Clear();

            onSessionEvent(SessionEventType.kStopped);
        }


        //
        // Receiving-related functions
        //
        void onTransportMessage (Transport transport, string msg_type, object message)
        {
            // Checks session id
            if (transport.encoding == FunEncoding.kJson)
            {
                if (json_helper_.HasField(message, kSessionIdField))
                {
                    string session_id = json_helper_.GetStringField(message, kSessionIdField);
                    if (!setSessionId(transport, session_id))
                        return;
                }
            }
            else
            {
                FunMessage funmsg = message as FunMessage;
                if (funmsg.sidSpecified)
                {
                    if (!setSessionId(transport, funmsg.sid))
                        return;
                }
            }

            if (string.IsNullOrEmpty(msg_type))
                return;

            // Processes a message
            switch (msg_type)
            {
            case kSessionOpenedType:
                return;

            case kSessionClosedType:
                {
                    debug.LogWarning("[Session] Closed by server.");

                    if (wait_for_redirect_)
                        return;

                    addCommand(new CmdStopAll(this, true));
                    addCommand(new CmdEvent("onSessionClosed", onSessionClosed));
                }
                break;

            case kRedirectType:
                onRedirectMessage(transport.encoding, message);
                return;

            case kRedirectConnectType:
                onRedirectResultMessage(transport.encoding, message);
                return;

            case kMaintenanceType:
                if (MaintenanceCallback != null)
                    MaintenanceCallback(transport.encoding, message);
                break;

            case kMulticastMsgType:
                if (MulticastMessageCallback != null)
                    MulticastMessageCallback(msg_type, message);
                break;

            default:
                {
                    if (transport.encoding == FunEncoding.kProtobuf)
                    {
                        FunMessage funmsg = message as FunMessage;
                        if (funmsg.msgtype2Specified)
                            RemoveResponseTimeout(funmsg.msgtype2);
                    }
                    else
                    {
                        RemoveResponseTimeout(msg_type);
                    }

                    if (ReceivedMessageCallback != null)
                        ReceivedMessageCallback(msg_type, message);
                }
                break;
            }
        }

        void onRedirectMessage (FunEncoding encoding, object message)
        {
            string host = "";
            string token = "";
            string flavor = "";

            redirect_list_.Clear();
            redirect_cur_tags_.Clear();
            redirect_target_tags_.Clear();

            if (encoding == FunEncoding.kJson)
            {
                host = json_helper_.GetStringField(message, "host");
                token = json_helper_.GetStringField(message, "token");
                flavor = json_helper_.GetStringField(message, "flavor");
                debug.Log("[Redirect] host:{0}, flavor:{1}", host, flavor);

                if (json_helper_.HasField(message, "current_tags"))
                {
                    object tags = json_helper_.GetObject(message, "current_tags");
                    int length = json_helper_.GetArrayCount(tags);
                    StringBuilder log = new StringBuilder();
                    log.Append("[Redirect] Current tags [");

                    for (int i = 0; i < length; ++i)
                    {
                        string tag = json_helper_.GetArrayObject(tags, i) as string;
                        bool add_comma = i < length - 1;
                        redirect_cur_tags_.Add(tag);
                        log.AppendFormat(add_comma ? "{0}, " : "{0}", tag);
                    }

                    log.Append("] ");
                    debug.Log(log.ToString());
                }

                if (json_helper_.HasField(message, "target_tags"))
                {
                    object tags = json_helper_.GetObject(message, "target_tags");
                    int length = json_helper_.GetArrayCount(tags);
                    StringBuilder log = new StringBuilder();
                    log.Append("[Redirect] Target tags [");

                    for (int i = 0; i < length; ++i)
                    {
                        string tag = json_helper_.GetArrayObject(tags, i) as string;
                        bool add_comma = i < length - 1;
                        redirect_target_tags_.Add(tag);
                        log.AppendFormat(add_comma ? "{0}, " : "{0}", tag);
                    }

                    log.Append("] ");
                    debug.Log(log.ToString());
                }

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
                    redirect_list_.Add(info.protocol, info);

                    debug.Log("[Redirect] {0}, {1}:{2}, {3}",
                              convertString(info.protocol), host, info.port, convertString(info.encoding));
                }
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = message as FunMessage;
                FunRedirectMessage redirect = FunapiMessage.GetMessage<FunRedirectMessage>(msg, MessageType._sc_redirect);
                if (redirect == null)
                    return;

                host = redirect.host;
                token = redirect.token;
                flavor = redirect.flavor;
                debug.Log("[Redirect] host:{0}, flavor:{1}", host, flavor);

                if (redirect.current_tags.Count > 0)
                {
                    redirect_cur_tags_.AddRange(redirect.current_tags);

                    StringBuilder log = new StringBuilder();
                    log.AppendFormat("[Redirect] Current tags [");

                    int count = redirect_cur_tags_.Count;
                    for (int i = 0; i < count; ++i)
                    {
                        string tag = redirect_cur_tags_[i];
                        bool add_comma = i < count - 1;
                        log.AppendFormat(add_comma ? "{0}, " : "{0}", tag);
                    }

                    log.Append("] ");
                    debug.Log(log.ToString());
                }

                if (redirect.target_tags.Count > 0)
                {
                    redirect_target_tags_.AddRange(redirect.target_tags);

                    StringBuilder log = new StringBuilder();
                    log.AppendFormat("[Redirect] Target tags [");

                    int count = redirect_target_tags_.Count;
                    for (int i = 0; i < count; ++i)
                    {
                        string tag = redirect_target_tags_[i];
                        bool add_comma = i < count - 1;
                        log.AppendFormat(add_comma ? "{0}, " : "{0}", tag);

                    }

                    log.Append("] ");
                    debug.Log(log.ToString());
                }

                foreach (FunRedirectMessage.ServerPort item in redirect.ports)
                {
                    RedirectInfo info = new RedirectInfo();
                    info.protocol = (TransportProtocol)item.protocol;
                    info.encoding = (FunEncoding)item.encoding;
                    info.port = (ushort)item.port;
                    info.option = getTransportOption(flavor, info.protocol);
                    redirect_list_.Add(info.protocol, info);

                    debug.Log("[Redirect] {0}, {1}:{2}, {3}",
                              convertString(info.protocol), host, info.port, convertString(info.encoding));
                }
            }

            if (host.Length <= 0 || token.Length <= 0)
            {
                debug.LogWarning("onRedirectMessage - Invalid host or token.\nhost:{0}, token:{1}",
                                 host, token);
                return;
            }

            if (redirect_list_.Count <= 0)
            {
                debug.LogWarning("onRedirectMessage - Server port list is empty.");
                return;
            }

            redirect_token_ = token;
            redirect_option_ = getSessionOption(flavor);
            wait_for_redirect_ = true;

            // Stopping all transports.
            lock (transports_lock_)
            {
                foreach (Transport transport in transports_.Values)
                {
                    transport.IsRedirecting = true;

                    if (!option_.sessionReliability)
                    {
                        transport.Stop();
                    }
                    else
                    {
                        StartCoroutine(tryToStopTransport(transport));
                    }
                }
            }

            // Notify start to redirect.
            onSessionEventCallback(SessionEventType.kRedirectStarted);

            addCommand(new CmdEvent("onRedirect", () => StartCoroutine(onRedirect(host))));
        }

        void onRedirectResultMessage (FunEncoding encoding, object message)
        {
            bool succeeded = true;

            if (encoding == FunEncoding.kJson)
            {
                RedirectResult result = (RedirectResult)json_helper_.GetIntegerField(message, "result");
                if (result != RedirectResult.kSucceeded)
                {
                    succeeded = false;
                    debug.LogWarning("Redirect failed. error code: {0}", result);
                }
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = message as FunMessage;
                FunRedirectConnectMessage redirect = FunapiMessage.GetMessage<FunRedirectConnectMessage>(msg, MessageType._cs_redirect_connect);
                if (redirect == null)
                    return;

                if (redirect.result != FunRedirectConnectMessage.Result.OK)
                {
                    succeeded = false;
                    debug.LogWarning("Redirect failed. error code: {0}", redirect.result);
                }
            }

            if (succeeded)
            {
                sendUnsentQueueMessages();

                state = State.kConnected;
                wait_for_redirect_ = false;

                if (redirect_option_ != null)
                {
                    option_ = redirect_option_;
                }

                onSessionEventCallback(SessionEventType.kRedirectSucceeded);
            }
            else
            {
                onRedirectFailed();
            }
        }

        //
        // Redirect-related functions
        //
        void addUnsentMessageQueue (string msg_type, object message, TransportProtocol protocol, EncryptionType enc_type)
        {
            // Checks the protocol of the server to be moved
            if (!redirect_list_.ContainsKey(protocol))
            {
                debug.LogWarning("[Redirect] There's no {0} transport. '{1}' message skipped.",
                                 convertString(protocol), msg_type);
                return;
            }

            // Checks encoding type of the server to be moved
            FunEncoding encoding = message is FunMessage ? FunEncoding.kProtobuf : FunEncoding.kJson;
            if (redirect_list_[protocol].encoding != encoding)
            {
                debug.LogWarning("[Redirect] '{0}' message skipped. This message's encoding type is {1}. (expected type: {2})",
                                 msg_type, convertString(encoding), convertString(redirect_list_[protocol].encoding));
                return;
            }

            // Queueing a message
            debug.Log("[Redirect] {0} adds '{1}' message to the queue.", convertString(protocol), msg_type);

            lock (unsent_message_lock_)
            {
                if (!unsent_messages_.ContainsKey(protocol))
                    unsent_messages_[protocol] = new Queue<UnsentMessage>();

                unsent_messages_[protocol].Enqueue(new UnsentMessage(msg_type, message, enc_type));
            }
        }

        void sendUnsentQueueMessages ()
        {
            lock (unsent_message_lock_)
            {
                if (unsent_messages_.Count <= 0)
                    return;

                foreach (TransportProtocol protocol in unsent_messages_.Keys)
                {
                    Queue<UnsentMessage> queue = unsent_messages_[protocol];
                    if (queue.Count <= 0)
                        continue;

                    debug.Log("[Redirect] {0} has {1} unsent message(s).", convertString(protocol), queue.Count);

                    Transport transport = GetTransport(protocol);
                    if (transport == null)
                    {
                        debug.Log("[Redirect] There's no {0} transport. Deletes {1} unsent message(s).",
                                  convertString(protocol), queue.Count);
                        queue.Clear();
                    }
                    else
                    {
                        // Fowards to user to check for queueing messages.
                        if (RedirectQueueCallback != null)
                        {
                            debug.Log("[Redirect] {0} calls queue event callback.", transport.str_protocol);
                            RedirectQueueCallback(protocol, redirect_cur_tags_, redirect_target_tags_, queue);
                        }

                        // Sends unsent messages.
                        int sending_count = 0;

                        while (queue.Count > 0)
                        {
                            UnsentMessage msg = queue.Dequeue();
                            if (msg.discard)
                                continue;

                            FunapiMessage message = null;
                            if (transport.encoding == FunEncoding.kJson)
                            {
                                message = new FunapiMessage(transport.protocol, msg.msg_type, json_helper_.Clone(msg.message), msg.enc_type);
                            }
                            else if (transport.encoding == FunEncoding.kProtobuf)
                            {
                                message = new FunapiMessage(transport.protocol, msg.msg_type, msg.message, msg.enc_type);
                            }

                            transport.SendMessage(message);
                            ++sending_count;
                        }

                        if (sending_count > 0)
                            debug.LogDebug("{0} sent {1} unsent message(s).", transport.str_protocol, sending_count);
                    }
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
            else if (protocol == TransportProtocol.kWebsocket)
                return "Websocket";

            return "Protocol-required";
        }

        static string convertString (FunEncoding encoding)
        {
            if (encoding == FunEncoding.kJson)
                return "Json";
            else if (encoding == FunEncoding.kProtobuf)
                return "Protobuf";

            return "Encoding-required";
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

            return "Enc-None";
        }

        static string convertString (FunCompressionType type)
        {
            if (type == FunCompressionType.kDeflate)
                return "Deflate";
            else if (type == FunCompressionType.kZstd)
                return "Zstd";

            return "None";
        }


        // Command-related classes
        abstract class Command : ICommand
        {
            public void Excute ()
            {
                if (excute != null)
                {
                    if (!excute())
                        failed = true;
                }
            }

            public string name { get { return name_; } }

            public bool keepWaiting
            {
                get { return !failed && !isDone(); }
            }

            public bool canExcute { get; set; }

            protected string name_ = "";
            protected Func<bool> excute = null;
            protected Func<bool> isDone = null;
            protected bool failed = false;
        }

        class CmdEvent : Command
        {
            public CmdEvent (string name, Action func)
            {
                name_ = string.Format("Event({0})", name);

                excute = delegate () {
                    func();
                    return true;
                };

                isDone = delegate () {
                    return true;
                };
            }
        }

        class CmdConnect : Command
        {
            public CmdConnect (FunapiSession session, Transport transport)
            {
                name_ = string.Format("Connect({0})", transport.str_protocol);

                excute = delegate () {
                    return session.connect(transport);
                };

                isDone = delegate () {
                    if (transport.IsStopped)
                        failed = true;

                    return transport.IsEstablished;
                };
            }
        }

        class CmdStop : Command
        {
            public CmdStop (FunapiSession session, Transport transport)
            {
                name_ = string.Format("Stop({0})", transport.str_protocol);

                excute = delegate () {
                    return session.stop(transport);
                };

                isDone = delegate () {
                    return transport.IsStopped;
                };
            }
        }

        class CmdStopAll : Command
        {
            public CmdStopAll (FunapiSession session, bool immediately = false)
            {
                name_ = "StopAll";

                excute = delegate () {
                    return session.stopAll(immediately);
                };

                isDone = delegate () {
                    return !session.Started;
                };
            }
        }

        class CmdPause : Command
        {
            public CmdPause (Transport transport, bool isPaused)
            {
                name_ = string.Format("Pause({0})", isPaused);

                excute = delegate () {
                    transport.OnPaused(isPaused);
                    return true;
                };

                isDone = delegate () {
                    return true;
                };
            }
        }


        class RedirectInfo
        {
            public TransportProtocol protocol;
            public FunEncoding encoding;
            public TransportOption option;
            public ushort port;
        }


        // constants
        const int kWaitForStopTimeout = 3;

        // Message-type-related constants.
        const string kIntMessageType = "_int#";
        const string kMessageTypeField = "_msgtype";
        const string kSessionIdField = "_sid";
        const string kUdpHandshakeIdField = "_udp_handshake_id";
        const string kSeqNumberField = "_seq";
        const string kAckNumberField = "_ack";
        const string kEmptyMessageType = "_empty";
        const string kSessionOpenedType = "_session_opened";
        const string kUdpHandShakeType = "_udp_handshake";
        const string kUdpAttachedType = "_udp_attached";
        const string kSessionClosedType = "_session_closed";
        const string kCloseSessionType = "_close_session";
        const string kMaintenanceType = "_maintenance";
        const string kMulticastMsgType = "_multicast";
        const string kRedirectType = "_sc_redirect";
        const string kRedirectConnectType = "_cs_redirect_connect";

        // Funapi message-related events.
        public event Action<SessionEventType, string> SessionEventCallback;                     // type, session id
        public event Func<string, SessionOption> SessionOptionCallback;                         // flavor, (return: session option)
        public event Func<string, TransportProtocol, TransportOption> TransportOptionCallback;  // flavor, protocol (return: transport option)
        public event Func<TransportProtocol, FunapiCompressor> CreateCompressorCallback;        // protocol (return: compressor)
        public event Action<TransportProtocol, TransportEventType> TransportEventCallback;      // protocol, type
        public event Action<TransportProtocol, TransportError> TransportErrorCallback;          // protocol, error
        public event Action<string, object> ReceivedMessageCallback;                            // type, message
        public event Action<string, object> MulticastMessageCallback;                           // type, message
        public event Action<string, object> DroppedMessageCallback;                             // type, message
        public event Action<string> ResponseTimeoutCallback;                                    // type (string)
        public event Action<int> ResponseTimeoutIntCallback;                                    // type (int)
        public event Action<FunEncoding, object> MaintenanceCallback;                           // encoding, message

        // protocol, tag list of the previous server, tag list of the moved server, unsent messages
        public event Action<TransportProtocol, List<string>, List<string>, Queue<UnsentMessage>> RedirectQueueCallback;


        enum State
        {
            kUnknown = 0,
            kStarted,
            kConnected,
            kWaitForSessionId,
            kWaitForStop,
            kReconnecting
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
        CommandList cmd_list_ = new CommandList();

        // Session-related variables.
        SessionId session_id_ = new SessionId();
        SessionId prev_session_id_ = new SessionId();
        SessionOption option_ = null;

        // Redirect-related variables.
        SessionOption redirect_option_ = null;
        bool wait_for_redirect_ = false;
        string redirect_token_ = "";
        List<string> redirect_cur_tags_ = new List<string>();
        List<string> redirect_target_tags_ = new List<string>();
        Dictionary<TransportProtocol, RedirectInfo> redirect_list_ = new Dictionary<TransportProtocol, RedirectInfo>();
        Dictionary<TransportProtocol, Queue<UnsentMessage>> unsent_messages_ = new Dictionary<TransportProtocol, Queue<UnsentMessage>>();
        object unsent_message_lock_ = new object();

        // Transport-related variables.
        object transports_lock_ = new object();
        TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        TransportProtocol reliable_protocol_ = TransportProtocol.kDefault;
        Dictionary<TransportProtocol, Transport> transports_ = new Dictionary<TransportProtocol, Transport>();

        // Message-related variables.
        static JsonAccessor json_helper_ = FunapiMessage.JsonHelper;
        ResponseTimeout response_timeout_ = new ResponseTimeout();

        // For debugging
        FunDebugLog debug = new FunDebugLog();
    }


    // This is used to save unsent messages during redirection.
    public class UnsentMessage
    {
        public UnsentMessage (string type, object msg, EncryptionType enc)
        {
            msg_type = type;
            message = msg;
            enc_type = enc;
        }

        public string msg_type;
        public object message;
        public EncryptionType enc_type;
        public bool discard = false;
    }
}
