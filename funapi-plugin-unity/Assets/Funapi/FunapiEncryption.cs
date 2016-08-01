// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;


namespace Fun
{
    public enum EncryptionType
    {
        kNoneEncryption = 0,
        kDefaultEncryption = 100,
        kDummyEncryption,
        kIFunEngine1Encryption,
        kIFunEngine2Encryption
    }


    public class FunapiEncryptor
    {
        protected void setEncryption (EncryptionType type)
        {
        }

        protected EncryptionType getEncryption (FunapiMessage message)
        {
            return EncryptionType.kNoneEncryption;
        }

        protected void parseEncryptionHeader (ref string encryption_type, ref string encryption_header)
        {
        }

        protected bool handshake (string encryption_type, string encryption_header)
        {
            // Do nothing
            return true;
        }

        protected bool encryptMessage (FunapiMessage message, EncryptionType type, ref string header)
        {
            FunDebug.LogWarning("This plugin is not support encryption.");
            return false;
        }

        protected bool decryptMessage (ArraySegment<byte> buffer, string encryption_type, string encryption_header)
        {
            FunDebug.LogWarning("This plugin is not support encryption.");
            return false;
        }
    }
}
