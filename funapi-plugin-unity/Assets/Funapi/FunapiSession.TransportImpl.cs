// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WebSocketSharp;
#if !NO_UNITY
using UnityEngine;
#endif


namespace Fun
{
    public partial class FunapiSession
    {
        // TCP transport layer
        class TcpTransport : Transport
        {
            public TcpTransport (string hostname_or_ip, UInt16 port,
                                 FunEncoding type, TransportOption tcp_option)
            {
                protocol_ = TransportProtocol.kTcp;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = tcp_option;

                setAddress(hostname_or_ip, port);
            }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get
                {
                    lock (sock_lock_)
                        return sock_ != null && sock_.Connected && state_ >= State.kConnected;
                }
            }

            void setAddress (string host, UInt16 port)
            {
                addr_ = new HostIP(host, port);

                TcpTransportOption tcp_option = (TcpTransportOption)option_;
                debug.Log("TCP connect - {0}:{1}, {2}, {3}, Compression:{4}, Sequence:{5}, " +
                          "ConnectionTimeout:{6}, AutoReconnect:{7}, Nagle:{8}, Ping:{9}",
                          addr_.ip, addr_.port, convertString(encoding_), convertString(tcp_option.Encryption),
                          tcp_option.CompressionType, tcp_option.SequenceValidation, tcp_option.ConnectionTimeout,
                          tcp_option.AutoReconnect, !tcp_option.DisableNagle, tcp_option.EnablePing);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnecting;

                addr_.refresh();

                lock (sock_lock_)
                {
                    sock_ = new Socket(addr_.inet, SocketType.Stream, ProtocolType.Tcp);

                    bool disable_nagle = (option_ as TcpTransportOption).DisableNagle;
                    if (disable_nagle)
                        sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                    sock_.BeginConnect(addr_.list, addr_.port, new AsyncCallback(this.startCb), this);
                }
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (sock_ != null)
                    {
                        sock_.Close();
                        sock_ = null;
                    }
                }
            }

            protected override void wireSend ()
            {
                List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();

                lock (sending_lock_)
                {
                    int length = 0;

                    foreach (FunapiMessage msg in sending_)
                    {
                        if (msg.header.Count > 0)
                        {
                            list.Add(msg.header);
                            length += msg.header.Count;
                        }

                        if (msg.body.Count > 0)
                        {
                            list.Add(msg.body);
                            length += msg.body.Count;
                        }
                    }

                    debug.DebugLog2("TCP sending {0} message(s). ({1}bytes)", sending_.Count, length);
                }

                try
                {
                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        sock_.BeginSend(list, SocketFlags.None, new AsyncCallback(this.sendBytesCb), this);
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.DebugLog1("TCP BeginSend operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "TCP failure in wireSend: " + e.ToString();
                    onFailure(error);
                }
            }

            void startCb (IAsyncResult ar)
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
                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kStartingFailed;
                            error.message = string.Format("TCP connection failed.");
                            onFailure(error);
                            return;
                        }
                        debug.DebugLog1("TCP transport connected. Starts handshaking..");

                        state_ = State.kHandshaking;

                        lock (receive_lock_)
                        {
                            // Wait for handshaking message.
                            ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                            List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                            buffer.Add(wrapped);
                            sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.receiveBytesCb), this);
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    debug.DebugLog1("TCP BeginConnect operation has been cancelled.");
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kStartingFailed;
                    error.message = "TCP failure in startCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void sendBytesCb (IAsyncResult ar)
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
                    FunDebug.Assert(nSent > 0, "TCP failed to transfer messages.");

                    debug.DebugLog3("TCP sent {0} bytes.", nSent);

                    lock (sending_lock_)
                    {
                        while (nSent > 0)
                        {
                            FunDebug.Assert(sending_.Count > 0,
                                string.Format("TCP couldn't find the sending buffers that sent messages.\n" +
                                              "Sent {0} more bytes but there are no sending buffers.", nSent));

                            // removes a sent message.
                            FunapiMessage msg = sending_[0];
                            int length = msg.header.Count + msg.body.Count;
                            nSent -= length;
                            sending_.RemoveAt(0);
                        }

                        FunDebug.Assert(sending_.Count == 0,
                            string.Format("sendBytesCb - sending buffer has {0} message(s).", sending_.Count));

                        // Sends pending messages
                        checkPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    debug.DebugLog1("TCP BeginSend operation has been cancelled.");
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "TCP failure in sendBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void receiveBytesCb (IAsyncResult ar)
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

                    lock (receive_lock_)
                    {
                        if (nRead > 0)
                        {
                            received_size_ += nRead;
                            debug.DebugLog3("TCP received {0} bytes. Buffer has {1} bytes.",
                                            nRead, received_size_ - next_decoding_offset_);

                            // Parses messages
                            parseMessages();

                            // Checks buffer space
                            checkReceiveBuffer();

                            // Starts another async receive
                            ArraySegment<byte> residual = new ArraySegment<byte>(
                                receive_buffer_, received_size_, receive_buffer_.Length - received_size_);

                            List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                            buffer.Add(residual);

                            lock (sock_lock_)
                            {
                                sock_.BeginReceive(buffer, SocketFlags.None, new AsyncCallback(this.receiveBytesCb), this);
                                debug.DebugLog3("TCP ready to receive more. TCP can receive upto {0} more bytes.",
                                                receive_buffer_.Length - received_size_);
                            }
                        }
                        else
                        {
                            debug.LogWarning("TCP socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                debug.LogWarning("TCP buffer has {0} bytes but they failed to decode. Discarding.",
                                                 received_size_ - next_decoding_offset_);
                            }

                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kDisconnected;
                            error.message = "TCP can't receive messages. Maybe the socket is closed.";
                            onDisconnected(error);
                        }
                    }
                }
                catch (Exception e)
                {
                    // When Stop is called Socket.EndReceive may return a NullReferenceException
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.DebugLog1("TCP BeginReceive operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "TCP failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }


            Socket sock_;
            HostIP addr_;
            object sock_lock_ = new object();
        }


        // UDP transport layer
        class UdpTransport : Transport
        {
            public UdpTransport (string hostname_or_ip, UInt16 port,
                                 FunEncoding type, TransportOption udp_option)
            {
                protocol_ = TransportProtocol.kUdp;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = udp_option;

                setAddress(hostname_or_ip, port);
            }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get
                {
                    lock (sock_lock_)
                        return sock_ != null && state_ >= State.kConnected;
                }
            }

            protected void setAddress (string host, UInt16 port)
            {
                addr_ = new HostIP(host, port);

                debug.Log("UDP connect - {0}:{1}, {2}, {3}, Compression:{4}, Sequence:{5}, " +
                          "ConnectionTimeout:{6}",
                          addr_.ip, addr_.port, convertString(encoding_), convertString(option_.Encryption),
                          option_.CompressionType, option_.SequenceValidation, option_.ConnectionTimeout);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnected;

                lock (sock_lock_)
                {
                    sock_ = new Socket(addr_.inet, SocketType.Dgram, ProtocolType.Udp);
                    sock_.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    int port = 0;
#if FIXED_UDP_LOCAL_PORT
                    port = LocalPort.Next();
#endif
                    if (addr_.inet == AddressFamily.InterNetworkV6)
                        sock_.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                    else
                        sock_.Bind(new IPEndPoint(IPAddress.Any, port));

                    IPEndPoint lep = (IPEndPoint)sock_.LocalEndPoint;
                    debug.DebugLog1("UDP bind - local:{0}:{1}", lep.Address, lep.Port);

                    send_ep_ = new IPEndPoint(addr_.ip, addr_.port);
                    if (addr_.inet == AddressFamily.InterNetworkV6)
                        receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.IPv6Any, addr_.port);
                    else
                        receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, addr_.port);

                    lock (receive_lock_)
                    {
                        sock_.BeginReceiveFrom(receive_buffer_, 0, receive_buffer_.Length, SocketFlags.None,
                                               ref receive_ep_, new AsyncCallback(this.receiveBytesCb), this);
                    }
                }

                onStarted();
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (sock_ != null)
                    {
                        sock_.Close();
                        sock_ = null;
                    }
                }
            }

            // Send a packet.
            protected override void wireSend ()
            {
                int offset = 0;

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count > 0);

                    // Sends one message.
                    FunapiMessage msg = sending_[0];
                    int length = msg.header.Count + msg.body.Count;
                    if (length > kUdpBufferSize)
                    {
                        string error = string.Format("'{0}' message's length is {1} bytes " +
                                                     "but UDP single message can't bigger than {2} bytes.",
                                                     msg.msg_type, length, kUdpBufferSize);
                        FunDebug.Assert(false, error);
                    }

                    if (msg.header.Count > 0)
                    {
                        Buffer.BlockCopy(msg.header.Array, 0, send_buffer_, offset, msg.header.Count);
                        offset += msg.header.Count;
                    }

                    if (msg.body.Count > 0)
                    {
                        Buffer.BlockCopy(msg.body.Array, 0, send_buffer_, offset, msg.body.Count);
                        offset += msg.body.Count;
                    }

                    debug.DebugLog2("UDP sending {0} bytes.", length);
                }

                if (offset > 0)
                {
                    try
                    {
                        lock (sock_lock_)
                        {
                            sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                              send_ep_, new AsyncCallback(this.sendBytesCb), this);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        debug.DebugLog1("UDP BeginSendTo operation has been cancelled.");
                    }
                }
            }

            void sendBytesCb (IAsyncResult ar)
            {
                try
                {
                    int nSent = 0;

                    lock (sock_lock_)
                    {
                        if (sock_ == null)
                            return;

                        nSent = sock_.EndSend(ar);
                        FunDebug.Assert(nSent > 0, "UDP failed to transfer messages.");
                    }
                    debug.DebugLog2("UDP sent {0} bytes.", nSent);

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        FunapiMessage msg = sending_[0];

                        // Removes header and body segment
                        int nLength = msg.header.Count + msg.body.Count;
                        sending_.RemoveAt(0);

                        if (nSent != nLength)
                        {
                            string error = string.Format("UDP failed to sending a whole message. " +
                                                         "buffer:{0} sent:{1}", nLength, nSent);
                            FunDebug.Assert(false, error);
                        }

                        // Checks unsent messages
                        checkPendingMessages();
                    }
                }
                catch (ObjectDisposedException)
                {
                    debug.DebugLog1("UDP BeginSendTo operation has been cancelled.");
                }
                catch (Exception e)
                {
                    onFailedSending();

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "UDP failure in sendBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void receiveBytesCb (IAsyncResult ar)
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

                    lock (receive_lock_)
                    {
                        if (nRead > 0)
                        {
                            received_size_ += nRead;
                            debug.DebugLog3("UDP received {0} bytes. Buffer has {1} bytes.",
                                            nRead, received_size_ - next_decoding_offset_);
                        }

                        // Parses a message
                        parseMessages();

                        if (nRead > 0)
                        {
                            // Resets buffer
                            received_size_ = 0;
                            next_decoding_offset_ = 0;

                            lock (sock_lock_)
                            {
                                // Starts another async receive
                                sock_.BeginReceiveFrom(receive_buffer_, received_size_,
                                                       receive_buffer_.Length - received_size_,
                                                       SocketFlags.None, ref receive_ep_,
                                                       new AsyncCallback(this.receiveBytesCb), this);

                                debug.DebugLog3("UDP ready to receive more. UDP can receive upto {0} more bytes",
                                                receive_buffer_.Length);
                            }
                        }
                        else
                        {
                            debug.LogWarning("UDP socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                debug.LogWarning("UDP buffer has {0} bytes but they failed to decode. Discarding.",
                                                 received_size_ - next_decoding_offset_);
                            }

                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kDisconnected;
                            error.message = "UDP can't receive messages. Maybe the socket is closed.";
                            onDisconnected(error);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.DebugLog1("UDP BeginReceiveFrom operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "UDP failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }

#if FIXED_UDP_LOCAL_PORT
            // This class is to prevent UDP local ports from overlapping.
            static class LocalPort
            {
                static LocalPort ()
                {
                    string path = FunapiUtils.GetLocalDataPath;
                    if (path.Length > 0 && path[path.Length - 1] != '/')
                        path += "/";
                    save_path_ = path + kFileName;

                    if (File.Exists(save_path_))
                    {
                        string text = File.ReadAllText(save_path_);
                        if (!int.TryParse(text, out local_port_))
                            local_port_ = kLocalPortMin;
                    }
                    else
                    {
                        local_port_ = kLocalPortMin;
                        File.WriteAllText(save_path_, local_port_.ToString());
                    }

                    FunDebug.debug.Log("The udp local port start value ({0}) has been loaded.", local_port_);
                }

                public static int Next ()
                {
                    lock (local_port_lock_)
                    {
                        ++local_port_;
                        if (local_port_ > kLocalPortMax)
                            local_port_ = kLocalPortMin;

                        File.WriteAllText(save_path_, local_port_.ToString());

                        return local_port_;
                    }
                }


                const string kFileName = "udp.localport";
                const int kLocalPortMin = 49152;
                const int kLocalPortMax = 65534;

                static object local_port_lock_ = new object();
                static int local_port_ = 0;
                static string save_path_ = "";
            }
#endif


            // member variables
            Socket sock_;
            HostIP addr_;
            IPEndPoint send_ep_;
            EndPoint receive_ep_;
            object sock_lock_ = new object();

            // This length is 64KB minus 8 bytes UDP header and 48 bytes IP header.
            // (IP header size is based on IPV6. IPV4 uses 20 bytes.)
            // https://en.wikipedia.org/wiki/User_Datagram_Protocol
            const int kUdpBufferSize = 65479;

            // Sending buffer
            byte[] send_buffer_ = new byte[kUdpBufferSize];
        }


        // HTTP transport layer
        class HttpTransport : Transport
        {
            public HttpTransport (string hostname_or_ip, UInt16 port, bool https,
                                 FunEncoding type, TransportOption http_option)
            {
                protocol_ = TransportProtocol.kHttp;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = http_option;

                setAddress(hostname_or_ip, port, https);

                if (https)
                    TrustManager.LoadMozRoots();
            }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get { return state_ >= State.kConnected; }
            }

            protected void setAddress (string host, UInt16 port, bool https)
            {
                addr_ = new HostHttp(host, port, https);

                // Sets host url
                host_url_ = string.Format("{0}://{1}:{2}/v{3}/",
                                          (https ? "https" : "http"), host, port,
                                          FunapiVersion.kProtocolVersion);

                HttpTransportOption http_option = (HttpTransportOption)option_;
                using_www_ = http_option.UseWWW;

                debug.Log("HTTP connect - {0}, {1}, {2}, Compression:{3}, Sequence:{4}, " +
                          "ConnectionTimeout:{5}, UseWWW:{6}",
                          host_url_, convertString(encoding_), convertString(http_option.Encryption),
                          http_option.CompressionType, http_option.SequenceValidation,
                          http_option.ConnectionTimeout, using_www_);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnected;
                str_cookie_ = "";

                onStarted();
            }

            protected override void onClose ()
            {
                cancelRequest();
            }

            protected override bool isSendable
            {
                get
                {
                    if (!base.isSendable)
                        return false;

                    if (cur_request_ != null)
                        return false;

                    return true;
                }
            }

            protected override void wireSend ()
            {
                if (cur_request_ != null)
                    return;

                FunapiMessage msg = null;

                lock (sending_lock_)
                {
                    FunDebug.Assert(sending_.Count > 0);
                    msg = sending_[0];
                }

                // Header
                Dictionary<string, string> headers = new Dictionary<string, string>();
                string str_header = System.Text.Encoding.ASCII.GetString(msg.header.Array);
                string[] list = str_header.Split(kHeaderSeparator, StringSplitOptions.None);

                for (int i = 0; i < list.Length; i += 2)
                {
                    if (list[i].Length <= 0)
                        break;

                    if (list[i] == kEncryptionHeaderField)
                        headers.Add(kEncryptionHttpHeaderField, list[i+1]);
                    else if (list[i] == kUncompressedLengthHeaderField)
                        headers.Add(kUncompressedLengthHttpHeaderField, list[i+1]);
                    else
                        headers.Add(list[i], list[i+1]);
                }

                if (str_cookie_.Length > 0)
                    headers.Add(kCookieHeaderField, str_cookie_);

                debug.DebugLog2("HTTP sending {0} bytes.", msg.header.Count + msg.body.Count);

#if !NO_UNITY
                // Sending a message
                if (using_www_)
                {
                    sendWWWRequest(headers, msg);
                }
                else
#endif
                {
                    sendHttpWebRequest(headers, msg);
                }
            }

#if !NO_UNITY
            void sendWWWRequest (Dictionary<string, string> headers, FunapiMessage msg)
            {
                Request request = new Request();

                lock (request_lock_)
                {
                    FunDebug.Assert(cur_request_ == null);
                    cur_request_ = request;
                }

                if (msg.body.Count > 0)
                {
                    request.www = new WWW(host_url_, msg.body.Array, headers);
                    mono.StartCoroutine(wwwPost(request.www));
                }
                else
                {
                    request.www = new WWW(host_url_, null, headers);
                    mono.StartCoroutine(wwwPost(request.www));
                }
            }
#endif

            void sendHttpWebRequest (Dictionary<string, string> headers, FunapiMessage msg)
            {
                // Request
                HttpWebRequest web_request = (HttpWebRequest)WebRequest.Create(host_url_);
                web_request.ConnectionGroupName = session_id_;
                web_request.Method = "POST";
                web_request.ContentType = "application/octet-stream";
                web_request.ContentLength = msg.body.Count;

                foreach (KeyValuePair<string, string> item in headers) {
                    web_request.Headers[item.Key] = item.Value;
                }

                Request request = new Request();
                request.message = msg;
                request.web_request = web_request;

                lock (request_lock_)
                {
                    FunDebug.Assert(cur_request_ == null);
                    cur_request_ = request;
                }

                web_request.BeginGetRequestStream(new AsyncCallback(requestStreamCb), request);
            }

            void onReceiveHeader (string headers)
            {
                StringBuilder buffer = new StringBuilder();
                string[] lines = headers.Replace("\r", "").Split('\n');
                int body_length = 0;

                buffer.AppendFormat("{0}{1}{2}{3}", kVersionHeaderField, kHeaderFieldDelimeter,
                                    FunapiVersion.kProtocolVersion, kHeaderDelimeter);

                foreach (string n in lines)
                {
                    if (n.Length > 0)
                    {
                        string[] tuple = n.Split(kHeaderSeparator, StringSplitOptions.RemoveEmptyEntries);
                        string key = tuple[0].ToLower();
                        string value = "";

                        if (tuple.Length >= 2)
                            value = tuple[1];

                        switch (key)
                        {
                        case "content-type":
                            break;
                        case "set-cookie":
                            str_cookie_ = value;
                            debug.DebugLog3("HTTP set-cookie : {0}", str_cookie_);
                            break;
                        case "content-length":
                            body_length = Convert.ToInt32(value);
                            buffer.AppendFormat("{0}{1}{2}{3}", kLengthHeaderField,
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        case "x-ifun-enc":
                            buffer.AppendFormat("{0}{1}{2}{3}", kEncryptionHeaderField,
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        case "x-ifun-c":
                            buffer.AppendFormat("{0}{1}{2}{3}", kUncompressedLengthHeaderField,
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        default:
                            buffer.AppendFormat("{0}{1}{2}{3}", tuple[0],
                                                kHeaderFieldDelimeter, value, kHeaderDelimeter);
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                buffer.Append(kHeaderDelimeter);

                byte[] header_bytes = System.Text.Encoding.ASCII.GetBytes(buffer.ToString());

                // Checks buffer's space
                received_size_ = 0;
                next_decoding_offset_ = 0;
                checkReceiveBuffer(header_bytes.Length + body_length);

                // Copy to buffer
                Buffer.BlockCopy(header_bytes, 0, receive_buffer_, 0, header_bytes.Length);
                received_size_ += header_bytes.Length;
            }

            void requestStreamCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    Stream stream = request.web_request.EndGetRequestStream(ar);

                    FunapiMessage msg = request.message;
                    if (msg.body.Count > 0)
                        stream.Write(msg.body.Array, 0, msg.body.Count);
                    stream.Close();

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        debug.DebugLog2("HTTP sent {0} bytes.", msg.header.Count + msg.body.Count);

                        sending_.RemoveAt(0);
                    }

                    request.web_request.BeginGetResponse(new AsyncCallback(responseCb), request);
                }
                catch (Exception e)
                {
                    WebException we = e as WebException;
                    if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                        (e is ObjectDisposedException || e is NullReferenceException))
                    {
                        // When Stop is called HttpWebRequest.EndGetRequestStream may return a Exception
                        debug.DebugLog1("HTTP request operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "HTTP failure in requestStreamCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void responseCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    if (request.was_aborted)
                    {
                        debug.Log("HTTP responseCb - request aborted. ({0})", request.message.msg_type);
                        return;
                    }

                    request.web_response = (HttpWebResponse)request.web_request.EndGetResponse(ar);
                    request.web_request = null;

                    if (request.web_response.StatusCode == HttpStatusCode.OK)
                    {
                        lock (receive_lock_)
                        {
                            byte[] header = request.web_response.Headers.ToByteArray();
                            string str_header = System.Text.Encoding.ASCII.GetString(header, 0, header.Length);
                            onReceiveHeader(str_header);

                            request.read_stream = request.web_response.GetResponseStream();
                            request.read_stream.BeginRead(receive_buffer_, received_size_,
                                                          receive_buffer_.Length - received_size_,
                                                          new AsyncCallback(readCb), request);
                        }
                    }
                    else
                    {
                        TransportError error = new TransportError();
                        error.type = TransportError.Type.kReceivingFailed;
                        error.message = string.Format("Failed response. status:{0}",
                                                      request.web_response.StatusDescription);
                        onFailure(error);
                    }
                }
                catch (Exception e)
                {
                    WebException we = e as WebException;
                    if ((we != null && we.Status == WebExceptionStatus.RequestCanceled) ||
                        (e is ObjectDisposedException || e is NullReferenceException))
                    {
                        // When Stop is called HttpWebRequest.EndGetResponse may return a Exception
                        debug.DebugLog1("Http request operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "HTTP failure in responseCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void readCb (IAsyncResult ar)
            {
                try
                {
                    Request request = (Request)ar.AsyncState;
                    int nRead = request.read_stream.EndRead(ar);
                    if (nRead > 0)
                    {
                        lock (receive_lock_)
                        {
                            received_size_ += nRead;
                            request.read_stream.BeginRead(receive_buffer_, received_size_,
                                                          receive_buffer_.Length - received_size_,
                                                          new AsyncCallback(readCb), request);
                        }
                    }
                    else
                    {
                        if (request.web_response == null)
                        {
                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kReceivingFailed;
                            error.message = "Response instance is null.";
                            onFailure(error);
                            return;
                        }

                        debug.DebugLog3("HTTP received {0} bytes.", received_size_);

                        lock (receive_lock_)
                        {
                            // Parses a message
                            parseMessages();
                        }

                        request.read_stream.Close();
                        request.web_response.Close();

                        lock (request_lock_)
                        {
                            cur_request_ = null;
                        }

                        // Checks unsent messages
                        checkPendingMessages();
                    }
                }
                catch (Exception e)
                {
                    if (e is ObjectDisposedException || e is NullReferenceException)
                    {
                        debug.DebugLog1("HTTP request operation has been cancelled.");
                        return;
                    }

                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "HTTP failure in readCb: " + e.ToString();
                    onFailure(error);
                }
            }

#if !NO_UNITY
            IEnumerator wwwPost (WWW www)
            {
                FunDebug.Assert(cur_request_ != null);

                while (!www.isDone && !cur_request_.cancel)
                {
                    yield return null;
                }

                lock (request_lock_)
                {
                    if (cur_request_.cancel)
                    {
                        cur_request_ = null;
                        yield break;
                    }
                }

                try
                {
                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        FunapiMessage msg = sending_[0];
                        debug.DebugLog2("HTTP sent a message - '{0}' ({1}bytes)",
                                        msg.msg_type, msg.body.Count);

                        sending_.RemoveAt(0);
                    }

                    if (www.error != null && www.error.Length > 0)
                    {
                        throw new Exception(www.error);
                    }

                    // Decodes message
                    StringBuilder headers = new StringBuilder();
                    foreach (KeyValuePair<string, string> item in www.responseHeaders)
                    {
                        headers.AppendFormat("{0}{1}{2}{3}",
                            item.Key, kHeaderFieldDelimeter, item.Value, kHeaderDelimeter);
                    }
                    headers.Append(kHeaderDelimeter);

                    lock (receive_lock_)
                    {
                        onReceiveHeader(headers.ToString());

                        Buffer.BlockCopy(www.bytes, 0, receive_buffer_, received_size_, www.bytes.Length);
                        received_size_ += www.bytes.Length;

                        debug.DebugLog3("HTTP received {0} bytes.", received_size_);

                        // Parses a message
                        parseMessages();
                    }

                    lock (request_lock_)
                    {
                        cur_request_ = null;
                    }

                    // Checks unsent messages
                    checkPendingMessages();
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kRequestFailed;
                    error.message = "HTTP failure in wwwPost: " + e.ToString();
                    onFailure(error);
                }
            }
#endif

            void cancelRequest ()
            {
                lock (request_lock_)
                {
                    if (cur_request_ != null)
                    {
                        if (cur_request_.web_request != null)
                        {
                            cur_request_.was_aborted = true;
                            cur_request_.web_request.Abort();
                        }

                        if (cur_request_.web_response != null)
                            cur_request_.web_response.Close();

                        if (cur_request_.read_stream != null)
                            cur_request_.read_stream.Close();

#if !NO_UNITY
                        if (cur_request_.www != null)
                            cur_request_.cancel = true;
#endif
                        cur_request_ = null;
                    }
                }
            }

            protected override void onFailure (TransportError error)
            {
                cancelRequest();
                base.onFailure(error);
            }


            // Request variables collection class
            class Request
            {
                public FunapiMessage message = null;

                // WebRequest-related
                public HttpWebRequest web_request = null;
                public HttpWebResponse web_response = null;
                public Stream read_stream = null;
                public bool was_aborted = false;
#if !NO_UNITY
                // WWW-related
                public WWW www = null;
                public bool cancel = false;
#endif
            }


            // Funapi header-related constants.
            const string kEncryptionHttpHeaderField = "X-iFun-Enc";
            const string kUncompressedLengthHttpHeaderField = "X-iFun-C";
            const string kCookieHeaderField = "Cookie";

            static readonly string[] kHeaderSeparator = { kHeaderFieldDelimeter, kHeaderDelimeter };

            // member variables.
            HostHttp addr_;
            string host_url_;
            string str_cookie_;
            bool using_www_ = false;
            object request_lock_ = new object();
            Request cur_request_ = null;
        }


        // Websocket transport layer
        class WebsocketTransport : Transport
        {
            public WebsocketTransport (string hostname_or_ip, UInt16 port,
                                       FunEncoding type, TransportOption websocket_option)
            {
                protocol_ = TransportProtocol.kWebsocket;
                str_protocol_ = convertString(protocol_);
                encoding_ = type;
                option = websocket_option;

                setAddress(hostname_or_ip, port);
            }

            public override HostAddr address
            {
                get { return addr_; }
            }

            public override bool Connected
            {
                get
                {
                    lock (sock_lock_)
                        return wsock_ != null && state_ >= State.kConnected;
                }
            }

            void setAddress (string host, UInt16 port)
            {
                addr_ = new HostIP(host, port);

                WebsocketTransportOption ws_option = (WebsocketTransportOption)option_;
                wss_ = ws_option.WSS;

                // Sets host url
                host_url_ = string.Format("{0}://{1}:{2}/", wss_ ? "wss" : "ws", host, port);

                debug.Log("Websocket connect - {0}, {1}, {2}, Compression:{3}, ConnectionTimeout:{4}",
                          host_url_, convertString(encoding_), convertString(option_.Encryption),
                          option_.CompressionType, option_.ConnectionTimeout);
            }

            protected override void onStart ()
            {
                base.onStart();

                state_ = State.kConnecting;

                addr_.refresh();

                lock (sock_lock_)
                {
                    wsock_ = new WebSocket(host_url_);
                    wsock_.OnOpen += startCb;
                    wsock_.OnClose += closeCb;
                    wsock_.OnError += errorCb;
                    wsock_.OnMessage += receiveBytesCb;

                    // Callback function for secure connection with SSL/TLS.
                    if (wss_)
                    {
                        TrustManager.LoadMozRoots();
                        wsock_.SslConfiguration.ServerCertificateValidationCallback = TrustManager.CertValidationCallback;
                    }

                    wsock_.ConnectAsync();
                }
            }

            protected override void onClose ()
            {
                lock (sock_lock_)
                {
                    if (wsock_ != null)
                    {
                        wsock_.Close();
                        wsock_ = null;
                    }
                }
            }

            protected override void wireSend ()
            {
                try
                {
                    byte[] buffer = null;

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        FunapiMessage msg = sending_[0];

                        int length = msg.header.Count + msg.body.Count;
                        buffer = new byte[length];

                        if (msg.header.Count > 0)
                            Buffer.BlockCopy(msg.header.Array, 0, buffer, 0, msg.header.Count);

                        if (msg.body.Count > 0)
                            Buffer.BlockCopy(msg.body.Array, 0, buffer, msg.header.Count, msg.body.Count);

                        debug.DebugLog3("Websocket sending {0} bytes.", length);
                    }

                    lock (sock_lock_)
                    {
                        if (wsock_ == null)
                            return;

                        wsock_.SendAsync(buffer, sendBytesCb);
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "Websocket failure in wireSend: " + e.ToString();
                    onFailure(error);
                }
            }

            void startCb (object sender, EventArgs args)
            {
                state_ = State.kHandshaking;

                debug.DebugLog1("Websocket transport connected. Starts handshaking..");
            }

            void closeCb (object sender, CloseEventArgs args)
            {
                debug.Log("Websocket closeCb called. ({0}) {1}", args.Code, args.Reason);
            }

            void errorCb (object sender, WebSocketSharp.ErrorEventArgs args)
            {
                TransportError error = new TransportError();
                error.type = TransportError.Type.kWebsocketError;
                error.message = "Websocket failure: " + args.Message;
                onFailure(error);
            }

            void sendBytesCb (bool completed)
            {
                try
                {
                    FunDebug.Assert(completed, "Websocket failed to transfer messages.");

                    lock (sending_lock_)
                    {
                        FunDebug.Assert(sending_.Count > 0);
                        sending_.RemoveAt(0);

                        debug.DebugLog3("Websocket sent 1 message.");

                        // Sends pending messages
                        checkPendingMessages();
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kSendingFailed;
                    error.message = "Websocket failure in sendBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }

            void receiveBytesCb (object sender, MessageEventArgs args)
            {
                try
                {
                    lock (receive_lock_)
                    {
                        if (args.RawData != null)
                        {
                            // Checks buffer space
                            checkReceiveBuffer(args.RawData.Length);

                            // Copy the recieved messages
                            Buffer.BlockCopy(args.RawData, 0, receive_buffer_, received_size_, args.RawData.Length);
                            received_size_ += args.RawData.Length;

                            debug.DebugLog3("Websocket received {0} bytes. Buffer has {1} bytes.",
                                            args.RawData.Length, received_size_ - next_decoding_offset_);

                            // Parses messages
                            parseMessages();
                        }
                        else
                        {
                            debug.LogWarning("Websocket socket closed");

                            if (received_size_ - next_decoding_offset_ > 0)
                            {
                                debug.LogWarning("Websocket buffer has {0} bytes but they failed to decode. Discarding.",
                                                 received_size_ - next_decoding_offset_);
                            }

                            TransportError error = new TransportError();
                            error.type = TransportError.Type.kDisconnected;
                            error.message = "Websocket can't receive messages. Maybe the socket is closed.";
                            onDisconnected(error);
                        }
                    }
                }
                catch (Exception e)
                {
                    TransportError error = new TransportError();
                    error.type = TransportError.Type.kReceivingFailed;
                    error.message = "Websocket failure in receiveBytesCb: " + e.ToString();
                    onFailure(error);
                }
            }


            HostIP addr_;
            string host_url_;
            bool wss_ = false;
            WebSocket wsock_;
            object sock_lock_ = new object();
        }
    }

}  // namespace Fun
