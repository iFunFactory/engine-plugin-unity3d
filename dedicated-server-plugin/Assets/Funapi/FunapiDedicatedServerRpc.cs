// Copyright 2019 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

// protobuf
using funapi.distribution.fun_dedicated_server_rpc_message;


namespace Fun
{
    // Session option
    public class DSRpcOption
    {
        public void AddHost (string hostname_or_ip, ushort port)
        {
            Addrs.Add(new KeyValuePair<string, ushort>(hostname_or_ip, port));
        }

        public string Tag = "";
        public bool DisableNagle = true;
        public List<KeyValuePair<string, ushort>> Addrs = new List<KeyValuePair<string, ushort>>();
    }

    public enum PeerEventType
    {
        kConnected,
        kDisconnected,
        kConnectionFailed,
        kConnectionTimedOut,
        kPeerId
    }

    // This is called when a peer receives a message from a server.
    // You must make a response message and return it from this callback.
    public delegate FunDedicatedServerRpcMessage RpcEventHandler (string type, FunDedicatedServerRpcMessage request);

    // Peer-related event handlers
    public delegate void PeerEventHandler (string peer_id, PeerEventType type);
    delegate void PeerRecvHandler (FunapiRpcPeer peer, FunDedicatedServerRpcMessage message);


    // FunapiDedicatedServerRpc
    public class FunapiDedicatedServerRpc: FunapiMono.Listener
    {
        public FunapiDedicatedServerRpc (DSRpcOption option)
        {
            option_ = option;

            addSystemHandler();
            setMonoListener();
        }

        public void Start ()
        {
            active_ = true;

            onConnect(0);
        }

        public void SetHandler (string type, RpcEventHandler handler)
        {
            lock (handler_lock_)
                handlers_[type] = handler;
        }

        public void SetPeerEventHandler (PeerEventHandler handler)
        {
            lock (peer_event_lock_)
                peer_event_handler_ = handler;
        }


        // FunapiMono.Listener-related functions
        public override string name
        {
            get { return "DedicatedServerRpc"; }
        }

        public override void OnUpdate (float deltaTime)
        {
            ahead_list_.Update();

            if (ahead_list_.Count > 0)
            {
                foreach (FunapiRpcPeer peer in ahead_list_.List)
                {
                    peer.Update(deltaTime);
                }
            }

            if (peer_list_.Count > 0)
            {
                peer_list_.Update();

                foreach (FunapiRpcPeer peer in peer_list_.Container.Values)
                {
                    peer.Update(deltaTime);
                }
            }

            event_.Update(deltaTime);
        }

        public override void OnQuit ()
        {
            active_ = false;

            foreach (FunapiRpcPeer peer in peer_list_.Container.Values)
            {
                peer.Close(true);
            }

            event_.Clear();

#if ENABLE_SAVE_LOG
            FunDebug.SaveLogs();
#endif
        }


        void addSystemHandler ()
        {
            system_handlers_[kRpcInfoMessageType] = onSystemInfo;
            system_handlers_[kRpcMasterMessageType] = onSystemMaster;
            system_handlers_[kRpcAddMessageType] = onSystemAddServer;
            system_handlers_[kRpcDelMessageType] = onSystemRemoveServer;
        }


        void onConnect (int index)
        {
            if (index >= option_.Addrs.Count)
            {
                FunDebug.Log("[RPC] Invalid connect index. index:{0} list size:{1}",
                             index, option_.Addrs.Count);
                return;
            }

            cur_index_ = index;

            FunapiRpcPeer peer = new FunapiRpcPeer(option_.DisableNagle);
            KeyValuePair<string, ushort> addr = option_.Addrs[index];
            peer.SetAddr(addr.Key, addr.Value);
            peer.SetEventHandler(onPeerEventBeforeConnect);
            peer.SetMessageHandler(onPeerMessage);

            ahead_list_.Add(peer);

            peer.Connect();
        }

        void onConnect (string peer_id, string hostname_or_ip, ushort port)
        {
            if (peer_list_.ContainsKey(peer_id))
            {
                FunapiRpcPeer p = peer_list_.GetValue(peer_id);
                if (p.state != FunapiRpcPeer.State.kConnecting &&
                    p.state != FunapiRpcPeer.State.kConnected)
                {
                    p.Connect();
                }
                return;
            }

            FunapiRpcPeer peer = new FunapiRpcPeer(option_.DisableNagle);
            peer.SetAddr(hostname_or_ip, port);
            peer.SetEventHandler(onPeerEvent);
            peer.SetMessageHandler(onPeerMessage);
            peer.SetPeerId(peer_id);

            ahead_list_.Add(peer);

            peer.Connect();
        }


        void setMaster (FunapiRpcPeer peer)
        {
            if (peer == null)
                return;

            master_peer_ = peer;

            FunDebug.Log("[RPC] Set Master: {0}", peer.peer_id);

            FunDedicatedServerRpcSystemMessage sysmsg = new FunDedicatedServerRpcSystemMessage();
            if (!string.IsNullOrEmpty(option_.Tag))
            {
                sysmsg.data = string.Format("{{ \"tag\" : \"{0}\" }}", option_.Tag);
            }

            FunDedicatedServerRpcMessage response = FunapiDSRpcMessage.CreateMessage(sysmsg, MessageType.ds_rpc_sys);
            string xid = Guid.NewGuid().ToString("N");
            response.type = kRpcMasterMessageType;
            response.xid = Encoding.UTF8.GetBytes(xid);
            response.is_request = true;

            peer.Send(response);
        }


        void onSystemMaster (FunapiRpcPeer peer, FunDedicatedServerRpcMessage msg)
        {
        }

        void onSystemInfo (FunapiRpcPeer peer, FunDedicatedServerRpcMessage msg)
        {
            FunDedicatedServerRpcSystemMessage sysmsg = new FunDedicatedServerRpcSystemMessage();
            if (!string.IsNullOrEmpty(option_.Tag))
            {
                sysmsg.data = string.Format("{{ \"tag\" : \"{0}\" }}", option_.Tag);
            }
            FunDedicatedServerRpcMessage response = FunapiDSRpcMessage.CreateMessage(sysmsg, MessageType.ds_rpc_sys);
            response.type = msg.type;
            response.xid = msg.xid;
            response.is_request = false;
            peer.Send(response);

            sysmsg = FunapiDSRpcMessage.GetMessage<FunDedicatedServerRpcSystemMessage>(msg, MessageType.ds_rpc_sys);
            if (string.IsNullOrEmpty(sysmsg.data))
                return;

            Dictionary<string, object> data = FunapiDSRpcMessage.ParseJson(sysmsg.data) as Dictionary<string, object>;
            string peer_id = "";
            if (data.ContainsKey("id"))
                peer_id = data["id"] as string;

            if (peer_id.Length > 0)
            {
                peer_list_.Add(peer_id, peer);
                ahead_list_.Remove(peer);

                peer.SetEventHandler(onPeerEvent);
                peer.SetPeerId(peer_id);

                if (master_peer_ == null)
                {
                    setMaster(peer);
                }
            }
        }

        void onSystemAddServer (FunapiRpcPeer peer, FunDedicatedServerRpcMessage msg)
        {
            FunDedicatedServerRpcMessage response = new FunDedicatedServerRpcMessage();
            response.type = msg.type;
            response.xid = msg.xid;
            response.is_request = false;
            peer.Send(response);

            FunDedicatedServerRpcSystemMessage sysmsg = FunapiDSRpcMessage.GetMessage<FunDedicatedServerRpcSystemMessage>(msg, MessageType.ds_rpc_sys);
            if (string.IsNullOrEmpty(sysmsg.data))
                return;

            Dictionary<string, object> data = FunapiDSRpcMessage.ParseJson(sysmsg.data) as Dictionary<string, object>;
            if (data.ContainsKey("id") && data.ContainsKey("ip") && data.ContainsKey("port"))
            {
                string peer_id = data["id"] as string;
                string ip = data["ip"] as string;
                ushort port = Convert.ToUInt16(data["port"]);

                if (!peer_list_.ContainsKey(peer_id))
                    FunDebug.Log("[Peer:{0}:{1}] Added. ({2})", ip, port, peer_id);

                onConnect(peer_id, ip, port);
            }
        }

        void onSystemRemoveServer (FunapiRpcPeer peer, FunDedicatedServerRpcMessage msg)
        {
            FunDedicatedServerRpcMessage response = new FunDedicatedServerRpcMessage();
            response.type = msg.type;
            response.xid = msg.xid;
            response.is_request = false;
            peer.Send(response);

            FunDedicatedServerRpcSystemMessage sysmsg = FunapiDSRpcMessage.GetMessage<FunDedicatedServerRpcSystemMessage>(msg, MessageType.ds_rpc_sys);
            if (string.IsNullOrEmpty(sysmsg.data))
                return;

            Dictionary<string, object> data = FunapiDSRpcMessage.ParseJson(sysmsg.data) as Dictionary<string, object>;
            string peer_id = "";
            if (data.ContainsKey("id"))
                peer_id = data["id"] as string;

            if (string.IsNullOrEmpty(peer_id))
                return;

            if (peer_list_.ContainsKey(peer_id))
            {
                FunapiRpcPeer del_peer = peer_list_.GetValue(peer_id);
                FunDebug.Log("[Peer:{0}:{1}] Removed. ({2})", del_peer.addr.host, del_peer.addr.port, peer_id);

                if (del_peer == master_peer_)
                {
                    FunapiRpcPeer master = null;
                    if (peer_list_.Count > 0)
                    {
                        foreach (FunapiRpcPeer p in peer_list_.Container.Values)
                        {
                            if (p != del_peer)
                            {
                                master = p;
                                break;
                            }
                        }
                    }

                    if (master != null)
                    {
                        setMaster(master);
                    }
                    else
                    {
                        onConnect(0);
                    }
                }

                peer_list_.Remove(peer_id);
            }
        }

        void onPeerEventBeforeConnect (FunapiRpcPeer peer, PeerEventType type)
        {
            onPeerEventCallback(peer, type);

            if (type == PeerEventType.kConnectionFailed ||
                type == PeerEventType.kDisconnected)
            {
                int index = 0;
                if ((cur_index_ + 1) < option_.Addrs.Count)
                    index = cur_index_ + 1;

                if (index == cur_index_)
                {
                    peer.Connect();
                    return;
                }

                ahead_list_.Remove(peer);

                event_.Add(delegate {
                    onConnect(index);
                });
            }
        }

        void onPeerEvent (FunapiRpcPeer peer, PeerEventType type)
        {
            onPeerEventCallback(peer, type);

            if (!active_)
                return;

            if (type == PeerEventType.kDisconnected)
            {
                if (peer == master_peer_)
                {
                    FunapiRpcPeer master = null;
                    if (peer_list_.Count > 0)
                    {
                        foreach (FunapiRpcPeer p in peer_list_.Container.Values)
                        {
                            if (p != peer)
                            {
                                master = p;
                                break;
                            }
                        }
                    }

                    if (master != null)
                    {
                        setMaster(master);
                    }
                    else
                    {
                        onConnect(0);
                    }

                    return;
                }
            }

            if (type == PeerEventType.kConnectionFailed ||
                type == PeerEventType.kConnectionTimedOut ||
                type == PeerEventType.kDisconnected)
            {
                if (peer_list_.ContainsKey(peer.peer_id))
                {
                    onConnect(peer.peer_id, peer.addr.host, peer.addr.port);
                }
            }
        }

        void onPeerEventCallback (FunapiRpcPeer peer, PeerEventType type)
        {
            lock (peer_event_lock_)
            {
                if (peer_event_handler_ != null)
                    peer_event_handler_(peer.peer_id, type);
            }
        }

        void onPeerMessage (FunapiRpcPeer peer, FunDedicatedServerRpcMessage msg)
        {
            if (!msg.is_request)
                return;

            string type = msg.type;

            lock (handler_lock_)
            {
                if (handlers_.ContainsKey(type))
                {
                    FunDedicatedServerRpcMessage response = handlers_[type](type, msg);
                    if (response != null)
                    {
                        response.type = type;
                        response.xid = msg.xid;
                        response.is_request = false;

                        peer.Send(response);
                    }
                    return;
                }
            }

            if (system_handlers_.ContainsKey(type))
            {
                system_handlers_[type](peer, msg);
                return;
            }

            FunDebug.Log("[RPC] handler not found '{0}'", type);
        }


        const string kRpcInfoMessageType = "_sys_ds_info";
        const string kRpcMasterMessageType = "_sys_ds_master";
        const string kRpcAddMessageType = "_sys_ds_add_server";
        const string kRpcDelMessageType = "_sys_ds_del_server";

        delegate void RpcSysEventHandler (FunapiRpcPeer peer, FunDedicatedServerRpcMessage request);


        DSRpcOption option_;
        int cur_index_ = 0;
        bool active_ = false;

        FunapiRpcPeer master_peer_ = null;
        ConcurrentSimpleList<FunapiRpcPeer> ahead_list_ = new ConcurrentSimpleList<FunapiRpcPeer>();
        ConcurrentDictionary<string, FunapiRpcPeer> peer_list_ = new ConcurrentDictionary<string, FunapiRpcPeer>();

        object peer_event_lock_ = new object();
        PeerEventHandler peer_event_handler_;

        object handler_lock_ = new object();
        Dictionary<string, RpcEventHandler> handlers_ = new Dictionary<string, RpcEventHandler>();
        Dictionary<string, RpcSysEventHandler> system_handlers_ = new Dictionary<string, RpcSysEventHandler>();
    }


    class FunapiRpcPeer
    {
        public FunapiRpcPeer (bool disable_nagle = true)
        {
            disable_nagle_ = disable_nagle;
        }

        public void SetAddr (string hostname_or_ip, ushort port)
        {
            addr_ = new HostIP(hostname_or_ip, port);
        }

        public void SetPeerId (string peer_id)
        {
            peer_id_ = peer_id;
        }

        public void Connect ()
        {
            event_.Add(delegate {
                onConnect();
            }, exponential_time_);

            if (exponential_time_ > 0f)
                logDebug("Reconnect after {0} seconds..", exponential_time_);
        }

        public void Close (bool immediately = false)
        {
            if (immediately)
            {
                onDisconnect(PeerEventType.kDisconnected);
            }
            else
            {
                event_.Add(delegate {
                    onDisconnect(PeerEventType.kDisconnected);
                });
            }
        }

        public void Update (float deltaTime)
        {
            decodeMessages();

            event_.Update(deltaTime);
            timer_.Update(deltaTime);
        }


        public void SetEventHandler (EventHandler handler)
        {
            event_handler_ = handler;
        }

        public void SetMessageHandler (PeerRecvHandler handler)
        {
            recv_handler_ = handler;
        }

        public void Send (FunDedicatedServerRpcMessage message)
        {
            lock (send_lock_)
            {
                send_queue_.Enqueue(message);
            }

            sendPendingMessages();
        }


        public string peer_id
        {
            get { return peer_id_; }
        }

        public HostIP addr
        {
            get { return addr_; }
        }

        public State state
        {
            get { return state_; }
        }


        void onConnect ()
        {
            if (state_ == State.kDisconnected)
            {
                state_ = State.kConnecting;

                timer_.Add(new FunapiTimeoutTimer("connection", kConnectionTimeout, onTimedout), true);
                logInfo("Connecting..");

                try
                {
                    lock (sock_lock_)
                    {
                        sock_ = new Socket(addr_.inet, SocketType.Stream, ProtocolType.Tcp);

                        if (disable_nagle_)
                            sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                        sock_.BeginConnect(addr_.host, addr_.port, new AsyncCallback(this.onConnectCb), this);
                    }
                }
                catch (Exception e)
                {
                    logWarning("Connection failed. {0}", e.ToString());
                    onDisconnect(PeerEventType.kConnectionFailed);
                }

                if (exponential_time_ == 0f)
                {
                    exponential_time_ = 1f;
                }
                else if (exponential_time_ < kReconnectDelayMax)
                {
                    exponential_time_ *= 2f;
                    if (exponential_time_ > kReconnectDelayMax)
                        exponential_time_ = kReconnectDelayMax;
                }
            }
        }

        void onTimedout ()
        {
            if (state_ == State.kConnected || state_ == State.kDisconnected)
                return;

            logWarning("Connection timed out.");

            onClose();
            onEvent(PeerEventType.kConnectionTimedOut);
        }

        void onDisconnect (PeerEventType type)
        {
            if (state_ == State.kDisconnected)
                return;

            onClose();

            onEvent(type);
        }

        void onClose ()
        {
            state_ = State.kDisconnected;

            decodeMessages();

            event_.Clear();
            timer_.Clear();

            lock (sock_lock_)
            {
                if (sock_ != null)
                {
                    sock_.Close();
                    sock_ = null;
                }
            }

            lock (send_lock_)
            {
                send_queue_.Clear();
            }
        }

        void onEvent (PeerEventType type)
        {
            logInfo("Event: {0}", type);

            lock (event_lock)
            {
                if (event_handler_ != null)
                {
                    event_handler_(this, type);
                }
            }
        }


        void onConnectCb (IAsyncResult ar)
        {
            try
            {
                lock (sock_lock_)
                {
                    if (sock_ == null)
                        return;

                    sock_.EndConnect(ar);
                    if (sock_.Connected == false)
                    {
                        onDisconnect(PeerEventType.kConnectionFailed);
                        return;
                    }

                    lock (recv_lock_)
                    {
                        ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                        List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                        buffer.Add(wrapped);

                        sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.onReceiveCb), this);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // do nothing.
            }
            catch (Exception e)
            {
                logWarning("Connection failed. {0}", e.ToString());
                onDisconnect(PeerEventType.kConnectionFailed);
            }
        }

        void sendPendingMessages ()
        {
            lock (send_lock_)
            {
                if (state_ != State.kConnected || sending_.Count != 0)
                    return;

                onSend();
            }
        }

        void onSend ()
        {
            try
            {
                lock (send_lock_)
                {
                    if (send_queue_.Count <= 0 || sending_.Count > 0)
                        return;

                    Queue<FunDedicatedServerRpcMessage> list = send_queue_;
                    send_queue_ = new Queue<FunDedicatedServerRpcMessage>();

                    foreach (FunDedicatedServerRpcMessage msg in list)
                    {
                        byte[] buf = FunapiDSRpcMessage.Serialize(msg);
                        if (buf == null)
                            continue;

                        logDebug("[C->S] type={0}", msg.type);

                        sending_.Add(new ArraySegment<byte>(buf));
                    }

                    if (sending_.Count <= 0)
                        return;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        sock_.BeginSend(sending_, SocketFlags.None, new AsyncCallback(this.sendCb), this);
                    }
                }
            }
            catch (Exception e)
            {
                logWarning("Sending failed. {0}", e.ToString());
                onDisconnect(PeerEventType.kDisconnected);
            }
        }

        void sendCb (IAsyncResult ar)
        {
            try
            {
                int nSent = 0;

                lock (sock_lock_)
                {
                    if (sock_ == null)
                        return;

                    nSent = sock_.EndSend(ar);
                }

                if (nSent > 0)
                {
                    lock (send_lock_)
                    {
                        while (nSent > 0)
                        {
                            if (sending_.Count > 0)
                            {
                                // removes a sent message.
                                ArraySegment<byte> msg = sending_[0];
                                nSent -= msg.Count;

                                sending_.RemoveAt(0);
                            }
                            else
                            {
                                logWarning("Sent {0} more bytes but couldn't find the sending buffer.", nSent);
                                break;
                            }
                        }

                        if (sending_.Count != 0)
                        {
                            logWarning("{0} message(s) left in the sending buffer.", sending_.Count);
                        }

                        // Sends pending messages.
                        sendPendingMessages();
                    }
                }
                else
                {
                    logWarning("socket closed.");
                }
            }
            catch (ObjectDisposedException)
            {
                // do nothing.
            }
            catch (Exception e)
            {
                logWarning("Sending failed. {0}", e.ToString());
                onDisconnect(PeerEventType.kDisconnected);
            }
        }

        void onReceiveCb (IAsyncResult ar)
        {
            try
            {
                int nRead = 0;

                lock (sock_lock_)
                {
                    if (sock_ == null)
                        return;

                    nRead = sock_.EndReceive(ar);
                }

                lock (recv_lock_)
                {
                    if (nRead > 0)
                    {
                        if (state_ == State.kConnecting)
                        {
                            logInfo("Connected.");

                            state_ = State.kConnected;
                            onEvent(PeerEventType.kConnected);
                        }

                        received_size_ += nRead;

                        // Parses messages
                        parseMessages();

                        // Checks buffer space
                        checkReceiveBuffer();

                        // Starts another async receive
                        lock (sock_lock_)
                        {
                            ArraySegment<byte> residual = new ArraySegment<byte>(
                                receive_buffer_, received_size_, receive_buffer_.Length - received_size_);

                            List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                            buffer.Add(residual);

                            sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.onReceiveCb), this);
                        }
                    }
                    else
                    {
                        logWarning("socket closed.");
                        onDisconnect(PeerEventType.kDisconnected);
                    }
                }
            }
            catch (Exception e)
            {
                // When Stop is called Socket.EndReceive may return a NullReferenceException
                if (e is ObjectDisposedException || e is NullReferenceException)
                {
                    // do nothing.
                    return;
                }

                logWarning("Receiving failed. {0}", e.ToString());
                onDisconnect(PeerEventType.kDisconnected);
            }
        }

        void parseMessages ()
        {
            lock (message_lock_)
            {
                while (true)
                {
                    if (next_decoding_offset_ >= received_size_ - 4)
                    {
                        // Not enough bytes. Wait for more bytes to come.
                        break;
                    }

                    int length = 0;
                    if (BitConverter.IsLittleEndian)
                    {
                        byte[] bytes = new byte [4];
                        Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, bytes, 0, 4);
                        Array.Reverse(bytes); // gets big endian
                        length = (int)BitConverter.ToUInt32(bytes, 0);
                    }
                    else
                    {
                        length = (int)BitConverter.ToUInt32(receive_buffer_, next_decoding_offset_);
                    }

                    if (length > 0)
                    {
                        int offset = next_decoding_offset_ + 4;
                        if (received_size_ - offset < length)
                        {
                            // Need more bytes for a message body. Waiting.
                            break;
                        }

                        object obj = FunapiDSRpcMessage.Deserialize(new ArraySegment<byte>(receive_buffer_, offset, length));
                        if (obj != null)
                        {
                            FunDedicatedServerRpcMessage msg = obj as FunDedicatedServerRpcMessage;
                            messages_.Enqueue(msg);

                            logDebug("[S->C] type={0}", msg.type);
                        }

                        next_decoding_offset_ = offset + length;
                    }
                    else
                    {
                        next_decoding_offset_ += 4;
                    }
                }
            }
        }

        // Checking the buffer space before starting another async receive.
        void checkReceiveBuffer ()
        {
            int remaining_size = receive_buffer_.Length - received_size_;
            if (remaining_size > 0)
                return;

            int retain_size = received_size_ - next_decoding_offset_;
            int new_length = receive_buffer_.Length;
            while (new_length <= retain_size)
                new_length += kUnitBufferSize;

            byte[] new_buffer = new byte[new_length];

            // If there are spaces that can be collected, compact it first.
            // Otherwise, increase the receiving buffer size.
            if (next_decoding_offset_ > 0)
            {
                // fit in the receive buffer boundary.
                logDebug("Compacting the receive buffer to save {0} bytes.", next_decoding_offset_);
                Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0,
                                 received_size_ - next_decoding_offset_);
                receive_buffer_ = new_buffer;
                received_size_ -= next_decoding_offset_;
                next_decoding_offset_ = 0;
            }
            else
            {
                logDebug("Increasing the receive buffer to {0} bytes.", new_length);
                Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                receive_buffer_ = new_buffer;
            }
        }

        void decodeMessages ()
        {
            lock (message_lock_)
            {
                while (messages_.Count > 0)
                {
                    FunDedicatedServerRpcMessage msg = messages_.Dequeue();
                    recv_handler_(this, msg);
                }
            }
        }

        string makeLogText (string format, params object[] args)
        {
            string text = string.Format(format, args);

            if (string.IsNullOrEmpty(peer_id_))
                return string.Format("[Peer:{0}:{1}] {2}", addr_.host, addr_.port, text);
            else
                return string.Format("[Peer:{0}] {1}", peer_id_, text);
        }

        void logInfo (string format, params object[] args)
        {
            FunDebug.Log(makeLogText(format, args));
        }

        void logWarning (string format, params object[] args)
        {
            FunDebug.LogWarning(makeLogText(format, args));
        }

        void logDebug (string format, params object[] args)
        {
            FunDebug.LogDebug(makeLogText(format, args));
        }


        public enum State
        {
            kConnecting,
            kConnected,
            kDisconnected
        }


        public delegate void EventHandler (FunapiRpcPeer peer, PeerEventType type);

        const float kConnectionTimeout = 5f;
        const float kReconnectDelayMax = 30f;
        const int kUnitBufferSize = 65536;

        string peer_id_ = "";
        HostIP addr_;
        bool disable_nagle_;

        State state_ = State.kDisconnected;
        float exponential_time_ = 0f;

        object sock_lock_ = new object();
        Socket sock_;

        object recv_lock_ = new object();
        byte[] receive_buffer_ = new byte[kUnitBufferSize];
        int received_size_ = 0;
        int next_decoding_offset_ = 0;
        object message_lock_ = new object();
        Queue<FunDedicatedServerRpcMessage> messages_ = new Queue<FunDedicatedServerRpcMessage>();

        object send_lock_ = new object();
        Queue<FunDedicatedServerRpcMessage> send_queue_ = new Queue<FunDedicatedServerRpcMessage>();
        List<ArraySegment<byte>> sending_ = new List<ArraySegment<byte>>();

        object event_lock = new object();
        EventHandler event_handler_ = null;
        PeerRecvHandler recv_handler_ = null;
        PostEventList event_ = new PostEventList();
        protected FunapiTimerList timer_ = new FunapiTimerList();
    }
}
