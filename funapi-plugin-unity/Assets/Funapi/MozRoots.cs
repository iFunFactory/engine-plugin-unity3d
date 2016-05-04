//
// Import the Mozilla's trusted root certificates
// from https://github.com/mono/mono/blob/master/mcs/tools/security/mozroots.cs
//

using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using UnityEngine;


namespace Fun
{
    public class MozRoots
    {
        const string trustedCertificatesPath = "Funapi/MozRoots";
        static X509Certificate2Collection trustedCerificates = null;

        static byte[] DecodeOctalString (string s)
        {
            string[] pieces = s.Split('\\');
            byte[] data = new byte[pieces.Length - 1];
            for (int i = 1; i < pieces.Length; i++)
            {
                data[i - 1] = (byte)((pieces[i][0] - '0' << 6) + (pieces[i][1] - '0' << 3) + (pieces[i][2] - '0'));
            }
            return data;
        }

        static X509Certificate2 DecodeCertificate (string s)
        {
            byte[] rawdata = DecodeOctalString(s);
            return new X509Certificate2(rawdata);
        }

        public static void LoadRootCertificates ()
        {
            if (trustedCerificates != null)
                return;

            try
            {
                TextAsset zippedMozRootsRawData = Resources.Load<TextAsset>(trustedCertificatesPath);
                MemoryStream zippedMozRootsRawDataStream = new MemoryStream(zippedMozRootsRawData.bytes);
                trustedCerificates = new X509Certificate2Collection();
                using (ZipInputStream zipInputStream = new ZipInputStream(zippedMozRootsRawDataStream))
                {
                    ZipEntry zipEntry = zipInputStream.GetNextEntry();
                    if (zipEntry == null)
                        throw new System.Exception("Certificates file does not exist!");
                    using (StreamReader streamReader = new StreamReader(zipInputStream))
                    {
                        StringBuilder sb = new StringBuilder();
                        bool processing = false;
                        while (true)
                        {
                            string line = streamReader.ReadLine();
                            if (line == null)
                                break;
                            if (processing)
                            {
                                if (line.StartsWith("END"))
                                {
                                    processing = false;
                                    X509Certificate root = DecodeCertificate(sb.ToString());
                                    trustedCerificates.Add(root);

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

                        FunDebug.Log("LoadRootCertificates succeeded: " + trustedCerificates.Count);
                    }
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("LoadRootCertificates failed: " + e.Message);
            }
        }

        public static bool CheckRootCertificate (X509Chain chain)
        {
            if (trustedCerificates == null)
                return false;

            for (int i = chain.ChainElements.Count - 1; i >= 0; i--)
            {
                X509ChainElement chainElement = chain.ChainElements[i];
                foreach (X509Certificate2 trusted in trustedCerificates)
                {
                    if (chainElement.Certificate.Equals(trusted))
                        return true;
                }
            }

            return false;
        }
    }
}
