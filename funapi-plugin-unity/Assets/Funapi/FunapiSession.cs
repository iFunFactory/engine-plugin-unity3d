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
        public int redirectTimeout = 10;    // seconds
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

            debug.SetDebugObject(this);
            response_timeout_.debugLog = debug;
            debug.Log("Starting a session module.");

            state = State.kUnknown;
            server_address_ = hostname_or_ip;
            option_ = option;

            setMonoListener();

            response_timeout_.SetCallbackHandler<int>(onResponseTimeoutCallback);
            response_timeout_.SetCallbackHandler<string>(onResponseTimeoutCallback);

            debug.Log("Plugin:{0} Protocol:{1} Reliability:{2}, SessionIdOnce:{3}",
                      FunapiVersion.kPluginVersion, FunapiVersion.kProtocolVersion,
                      option.sessionReliability, option.sendSessionIdOnlyOnce);
        }

        void OnDestroy ()
        {
            debug.Log("Destroy a session module.");

            if (Started)
            {
                stopAll(true);
            }

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
                return;

            transport.sendSessionIdOnlyOnce = option_.sendSessionIdOnlyOnce;
            transport.delayedAckInterval = option_.delayedAckInterval;

            addCommand(new CmdConnect(this, transport));
        }

        public void Connect (TransportProtocol protocol)
        {
            Transport transport = GetTransport(protocol);
            if (transport == null)
            {
                debug.LogWarning("Session.Connect({0}) called but can't find a {1} transport. " +
                                 "You should call FunapiSession.Connect(protocol, encoding, ...) function.",
                                 transport.str_protocol, transport.str_protocol);
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
                                 transport.str_protocol, transport.str_protocol);
                return;
            }

            addCommand(new CmdStop(this, transport));
        }

        public void Stop ()
        {
            addCommand(new CmdStopAll(this));
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

            Transport transport = GetTransport(protocol);

            if (transport != null &&
                (transport.IsReliable || transport.IsEstablished) &&
                (!wait_for_redirect_ || msg_type == kRedirectConnectType))
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
                    strlog.AppendFormat(" {0}:{1}", transport.str_protocol, transport.state);
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
                debug.DebugLog1("Session.Connect() called but the transport is null.");
                return false;
            }

            if (!Started)
            {
                debug.DebugLog1("Session.Connect({0}) called.", transport.str_protocol);

                state = State.kStarted;
            }

            if (transport.Connected)
            {
                debug.LogWarning("Session.Connect({0}) called but {1} has been already connected.",
                                 transport.str_protocol, transport.str_protocol);
                return false;
            }

            transport.Start();

            return true;
        }

        bool stop (Transport transport)
        {
            if (transport == null)
            {
                debug.DebugLog1("Session.Stop() called but the transport is null.");
                return false;
            }

            if (!Started)
            {
                debug.LogWarning("Session.Stop({0}) called but the session is not connected.",
                                 transport.str_protocol);
                return false;
            }

            if (transport.IsStopped)
            {
                debug.LogWarning("Session.Stop({0}) called but {1} hasn't been connected.",
                                 transport.str_protocol, transport.str_protocol);
                return false;
            }

            debug.DebugLog1("Session.Stop({0}) called. (state:{1})", transport.str_protocol, state_);

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

            debug.DebugLog1("Session.StopAll() called. (state:{0})", state_);

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

        void startRedirect (string host, List<RedirectInfo> list)
        {
            // Notify start to redirect.
            onSessionEventCallback(SessionEventType.kRedirectStarted);

            wait_for_redirect_ = true;

            // Stopping all transports.
            stopAll(true);

            StartCoroutine(onRedirect(host, list));
        }

        IEnumerator onRedirect (string host, List<RedirectInfo> list)
        {
            // Wait for stop.
            while (Started)
            {
                yield return new SleepForSeconds(0.1f);
            }

            lock (transports_lock_)
            {
                transports_.Clear();
                debug.Log("Redirect: Removes all transports.");
            }

            onSessionClosed();

            server_address_ = host;
            default_protocol_ = TransportProtocol.kDefault;

            // Adds transports.
            foreach (RedirectInfo info in list)
            {
                debug.Log("Redirect: {0} - {1}:{2}, {3}", convertString(info.protocol),
                          host, info.port, convertString(info.encoding));

                Connect(info.protocol, info.encoding, info.port, info.option);
            }

            // Converts seconds to ticks.
            long redirect_timeout = DateTime.UtcNow.Ticks + (option_.redirectTimeout * 1000 * 10000);

            // Wait for connect.
            while (true)
            {
                if (Connected)
                    break;

                if (DateTime.UtcNow.Ticks > redirect_timeout)
                {
                    debug.LogWarning("Redirect: Connection timed out. " +
                                     "Stops redirecting to another server. ({0})", host);
                    break;
                }

                yield return new SleepForSeconds(0.2f);
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
            set { default_protocol_ = value;
                  debug.Log("The default protocol is '{0}'", convertString(value)); }
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

                debug.Log("New session id: {0}", (string)session_id_);

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
            if (type == SessionEventType.kStopped)
                state = State.kUnknown;

            if (wait_for_redirect_)
            {
                debug.Log("Redirect: Session event ({0}).\nThis event callback is skipped.", type);
                return;
            }

            event_.Add(() => onSessionEventCallback(type));
        }

        void onSessionEventCallback (SessionEventType type)
        {
            debug.Log("EVENT: Session ({0}).", type);

            if (SessionEventCallback != null)
                SessionEventCallback(type, session_id_);
        }


        //
        // Transport-related functions
        //
        Transport createTransport (TransportProtocol protocol, FunEncoding encoding,
                                   UInt16 port, TransportOption option = null)
        {
            Transport transport = null;

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

                transport = getTransport(protocol, encoding, port, option);
                if (transport != null)
                    return transport;

                if (wait_for_redirect_)
                {
                    debug.Log("createTransport - {0} transport use the 'default option'.\n" +
                              "If you want to use your option, please set FunapiSession.TransportOptionCallback event.",
                              convertString(protocol));
                }
                else
                {
                    debug.Log("createTransport - {0} transport use the 'default option'.",
                              convertString(protocol));
                }
            }
            else
            {
                transport = getTransport(protocol, encoding, port, option);
                if (transport != null)
                    return transport;
            }

            if (option_.sessionReliability && protocol == TransportProtocol.kTcp)
                option.ReliableTransport = true;
            else
                option.ReliableTransport = false;

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
                debug.LogError("createTransport - {0} is invalid protocol type.", convertString(protocol));
                return null;
            }

            // Callback functions
            transport.CreateCompressorCallback += onCreateCompressor;
            transport.EventCallback += onTransportEvent;
            transport.ErrorCallback += onTransportError;
            transport.ReceivedCallback += onTransportMessage;
            transport.mono = this;

            transport.Init();

            if (default_protocol_ == TransportProtocol.kDefault)
                DefaultProtocol = protocol;

            lock (transports_lock_)
                transports_[protocol] = transport;

            debug.DebugLog1("{0} transport has been created.", transport.str_protocol);
            return transport;
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
                    debug.DebugLog1("Waiting for process before {0} transport to stop... ({1})",
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
                // If the session has a TCP protocol, TCP sends the first session message.
                // Priority order : TCP > HTTP > UDP
                if (protocol == TransportProtocol.kTcp ||
                    (protocol == TransportProtocol.kUdp && transportCount == 1) ||
                    (protocol == TransportProtocol.kHttp && !HasTransport(TransportProtocol.kTcp)) ||
                    (protocol == TransportProtocol.kWebsocket && !HasTransport(TransportProtocol.kTcp)))
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
                debug.Log("Redirect: {0} transport ({1}).\nThis event callback is skipped.",
                          convertString(protocol), type);
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

                debug.Log("EVENT: {0} transport ({1}).", convertString(protocol), type);

                if (TransportEventCallback != null)
                    TransportEventCallback(protocol, type);
            });
        }

        void onTransportError (TransportProtocol protocol, TransportError error)
        {
            if (wait_for_redirect_)
            {
                debug.LogWarning("Redirect: {0} error ({1})\nThis event callback is skipped.\n{2}.",
                                 convertString(protocol), error.type, error.message);
                return;
            }

            event_.Add (delegate
            {
                debug.Log("ERROR: {0} transport ({1}).", convertString(protocol), error.type);

                if (TransportErrorCallback != null)
                    TransportErrorCallback(protocol, error);
            });
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

            state = State.kConnected;

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
                    if (t.Connected || t.Connecting || t.Reconnecting)
                        return;
                }
            }

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
                    debug.Log("Session has been closed by server.");

                    if (wait_for_redirect_)
                        return;

                    addCommand(new CmdStopAll(this, true));
                    addCommand(new CmdEvent(onSessionClosed));
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
            List<RedirectInfo> info_list = new List<RedirectInfo>();

            if (encoding == FunEncoding.kJson)
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
            else if (encoding == FunEncoding.kProtobuf)
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
                debug.LogWarning("onRedirectMessage - Invalid host or token.\nhost:{0}, token:{1}",
                                 host, token);
                return;
            }

            if (info_list.Count <= 0)
            {
                debug.LogWarning("onRedirectMessage - Server port list is empty.");
                return;
            }

            redirect_token_ = token;

            addCommand(new CmdEvent(() => startRedirect(host, info_list)));
        }

        void onRedirectResultMessage (FunEncoding encoding, object message)
        {
            wait_for_redirect_ = false;

            if (encoding == FunEncoding.kJson)
            {
                RedirectResult result = (RedirectResult)json_helper_.GetIntegerField(message, "result");
                if (result == RedirectResult.kSucceeded)
                {
                    state = State.kConnected;
                    onSessionEvent(SessionEventType.kRedirectSucceeded);
                }
                else
                {
                    debug.LogWarning("Redirect failed. error code: {0}", result);
                    onRedirectFailed();
                }
            }
            else if (encoding == FunEncoding.kProtobuf)
            {
                FunMessage msg = message as FunMessage;
                FunRedirectConnectMessage redirect = FunapiMessage.GetMessage<FunRedirectConnectMessage>(msg, MessageType._cs_redirect_connect);
                if (redirect == null)
                    return;

                if (redirect.result == FunRedirectConnectMessage.Result.OK)
                {
                    state = State.kConnected;
                    onSessionEvent(SessionEventType.kRedirectSucceeded);
                }
                else
                {
                    debug.LogWarning("Redirect failed. error code: {0}", redirect.result);
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

            public bool keepWaiting
            {
                get { return !failed && !isDone(); }
            }

            public bool canExcute { get; set; }

            protected Func<bool> excute = null;
            protected Func<bool> isDone = null;
            protected bool failed = false;
        }

        class CmdEvent : Command
        {
            public CmdEvent (Action func)
            {
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
                excute = delegate () {
                    transport.OnPaused(isPaused);
                    return true;
                };

                isDone = delegate () {
                    return true;
                };
            }
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

        // Funapi message-related events.
        public event Action<SessionEventType, string> SessionEventCallback;                     // type, session id
        public event Func<string, TransportProtocol, TransportOption> TransportOptionCallback;  // flavor, protocol (return: option)
        public event Func<TransportProtocol, FunapiCompressor> CreateCompressorCallback;        // protocol (return: compressor)
        public event Action<TransportProtocol, TransportEventType> TransportEventCallback;      // protocol, type
        public event Action<TransportProtocol, TransportError> TransportErrorCallback;          // protocol, error
        public event Action<string, object> ReceivedMessageCallback;                            // type, message
        public event Action<string, object> MulticastMessageCallback;                           // type, message
        public event Action<string, object> DroppedMessageCallback;                             // type, message
        public event Action<string> ResponseTimeoutCallback;                                    // type (string)
        public event Action<int> ResponseTimeoutIntCallback;                                    // type (int)
        public event Action<FunEncoding, object> MaintenanceCallback;                           // encoding, message


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
        bool wait_for_redirect_ = false;
        string redirect_token_ = "";

        // Transport-related variables.
        object transports_lock_ = new object();
        TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        Dictionary<TransportProtocol, Transport> transports_ = new Dictionary<TransportProtocol, Transport>();

        // Message-related variables.
        static JsonAccessor json_helper_ = FunapiMessage.JsonHelper;
        ResponseTimeout response_timeout_ = new ResponseTimeout();

        // For debugging
        FunDebugLog debug = new FunDebugLog();
    }
}
