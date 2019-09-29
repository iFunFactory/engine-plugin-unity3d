// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;


namespace Fun
{
    public class HostAddr
    {
        public HostAddr (string host, UInt16 port)
        {
            this.host = host;
            this.port = port;
        }

        public string host;
        public UInt16 port;
    }

    public class HostHttp : HostAddr
    {
        public HostHttp (string host, UInt16 port, bool https = false)
            : base(host, port)
        {
            this.https = https;
        }

        public bool https;
    }

    public class HostIP : HostAddr
    {
        public HostIP (string host, UInt16 port)
            : base(host, port)
        {
            refresh();
        }

        public bool refresh ()
        {
            ip_list_ = Dns.GetHostAddresses(host);
            if (ip_list_ == null || ip_list_.Length == 0)
            {
                FunDebug.LogWarning("HostIP - Can't get any host address from '{0}'.", host);
                return false;
            }

            index_ = 0;
            inet_ = ip_list_[0].AddressFamily;

            return true;
        }

        public AddressFamily inet
        {
            get { return inet_; }
        }

        public IPAddress ip
        {
            get
            {
                if (ip_list_ == null || ip_list_.Length == 0)
                    return null;

                if (index_ >= ip_list_.Length)
                    index_ = 0;

                return ip_list_[index_++];
            }
        }

        public IPAddress[] list
        {
            get { return ip_list_; }
        }


        IPAddress[] ip_list_ = null;
        AddressFamily inet_;
        int index_ = 0;
    }

}  // namespace Fun
