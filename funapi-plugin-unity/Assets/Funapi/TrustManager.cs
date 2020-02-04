// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.
//
// Import the Mozilla's trusted root certificates
// from https://github.com/mono/mono/blob/master/mcs/tools/security/mozroots.cs
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
#if !NO_UNITY
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;
#endif


namespace Fun
{
    public class TrustManager
    {
#if !NO_UNITY
        [RuntimeInitializeOnLoadMethod]
        static void OnRuntimeLoad()
        {
#if !NO_LOAD_MOZROOTS
            TrustManager.LoadMozRoots();
#endif
        }
#endif

        public static bool DownloadMozRoots ()
        {
#if !NO_UNITY
            LoadMozRoots();
            string tempPath = FunapiUtils.GetLocalDataPath + "/" + Path.GetRandomFileName();
            try
            {
                // Try to download the Mozilla's root certificates file.
                WebClient webClient = new WebClient();
                webClient.DownloadFile(kMozillaCertificatesUrl, tempPath);
            }
            catch (WebException we)
            {
                FunDebug.LogError("DownloadMozRoots - Download certificates failed.\n{0}", we.Message);
                File.Delete(tempPath);
                return false;
            }

            try
            {
                using (FileStream readStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[readStream.Length];
                    readStream.Read(buffer, 0, buffer.Length);
                    readStream.Close();
                    File.Delete(tempPath);

                    string targetPath = FunapiUtils.GetAssetsPath + kDownloadCertificatesPath;

                    // If there's certificates file, delete the file.
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);

                    using (FileStream writeStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Write))
                    {
                        using (ZipOutputStream zipOutputStream = new ZipOutputStream(writeStream))
                        {
                            ZipEntry zipEntry = new ZipEntry("MozRoots");
                            zipEntry.Size = buffer.Length;
                            zipEntry.DateTime = DateTime.Now;

                            zipOutputStream.PutNextEntry(zipEntry);
                            zipOutputStream.Write(buffer, 0, buffer.Length);
                            zipOutputStream.Close();
                        }
                        writeStream.Close();
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("DownloadMozRoots - The creation of the zip file of certificates failed.\n{0}", e.Message);
                return false;
            }
#endif
            return true;
        }


        public static bool LoadMozRoots ()
        {
#if !NO_UNITY
            if (trusted_cerificates_ != null)
                return true;

            try
            {
                TextAsset zippedMozRootsRawData = Resources.Load<TextAsset>(kResourceCertificatesPath);
                if (zippedMozRootsRawData == null)
                    throw new System.Exception("LoadMozRoots - Certificates file does not exist!");
                if (zippedMozRootsRawData.bytes == null || zippedMozRootsRawData.bytes.Length <= 0)
                    throw new System.Exception("LoadMozRoots - The certificates file is corrupted!");

                trusted_cerificates_ = new X509Certificate2Collection();

                using (MemoryStream zippedMozRootsRawDataStream = new MemoryStream(zippedMozRootsRawData.bytes))
                {
                    using (ZipInputStream zipInputStream = new ZipInputStream(zippedMozRootsRawDataStream))
                    {
                        ZipEntry zipEntry = zipInputStream.GetNextEntry();
                        if (zipEntry != null)
                        {
                            using (StreamReader stream = new StreamReader(zipInputStream))
                            {
                                StringBuilder sb = new StringBuilder();
                                bool processing = false;

                                while (true)
                                {
                                    string line = stream.ReadLine();
                                    if (line == null)
                                        break;
                                    if (processing)
                                    {
                                        if (line.StartsWith("END"))
                                        {
                                            processing = false;
                                            X509Certificate root = decodeCertificate(sb.ToString());
                                            trusted_cerificates_.Add(root);

                                            sb = new StringBuilder();
                                            continue;
                                        }
                                        sb.Append(line);
                                    }
                                    else
                                    {
                                        processing = line.StartsWith("CKA_VALUE MULTILINE_OCTAL");
                                    }
                                }
                                stream.Close();

                                FunDebug.Log("LoadMozRoots - {0} certificates have been loaded.",
                                             trusted_cerificates_.Count);
                            }
                        }
                        zipInputStream.Close();
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("LoadMozRoots - Failed to load certificate files.\n{0}", e.Message);
                return false;
            }

            ServicePointManager.ServerCertificateValidationCallback = CertValidationCallback;
#endif
            return true;
        }


        public static bool CertValidationCallback (System.Object sender, X509Certificate certificate,
                                                   X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            // The server address to connect is different from the server address in the certificate
            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                return false;
            }

            foreach (X509ChainStatus status in chain.ChainStatus)
            {
                if (!allowed_chain_status_.Contains(status.Status))
                {
                    return false;
                }
            }

            // Verify that server certificate can be built from the root certificate in mozroots
            X509Chain ch = new X509Chain();
            for (int i = chain.ChainElements.Count - 1; i > 0; i--)
            {
                X509ChainElement chainElement = chain.ChainElements[i];
                ch.ChainPolicy.ExtraStore.Add(chainElement.Certificate);
            }
            ch.ChainPolicy.ExtraStore.AddRange(trusted_cerificates_);
            ch.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            ch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            ch.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
            ch.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            X509Certificate2 cert = new X509Certificate2(certificate);
            if (!ch.Build(cert))
            {
                FunDebug.LogWarning("Invalid certificate : {0}", certificate);
                return false;
            }
            return true;
        }


        static X509Certificate2 decodeCertificate (string cert)
        {
            byte[] rawdata = decodeOctalString(cert);
            return new X509Certificate2(rawdata);
        }

        static byte[] decodeOctalString (string cert)
        {
            string[] pieces = cert.Split('\\');
            byte[] data = new byte[pieces.Length - 1];

            for (int i = 1; i < pieces.Length; i++)
            {
                data[i - 1] = (byte)((pieces[i][0] - '0' << 6) + (pieces[i][1] - '0' << 3) + (pieces[i][2] - '0'));
            }

            return data;
        }


        const string kMozillaCertificatesUrl = "https://hg.mozilla.org/mozilla-central/raw-file/tip/security/nss/lib/ckfw/builtins/certdata.txt";
        const string kDownloadCertificatesPath = "/Resources/Funapi/MozRoots.bytes";
        const string kResourceCertificatesPath = "Funapi/MozRoots";

        static List<X509ChainStatusFlags> allowed_chain_status_ =
            new List<X509ChainStatusFlags>(new X509ChainStatusFlags[] { X509ChainStatusFlags.OfflineRevocation,
                                                                        X509ChainStatusFlags.PartialChain,
                                                                        X509ChainStatusFlags.RevocationStatusUnknown,
                                                                        X509ChainStatusFlags.UntrustedRoot } );

        static X509Certificate2Collection trusted_cerificates_ = null;
    }
}
