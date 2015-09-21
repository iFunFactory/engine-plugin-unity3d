// Copyright (C) 2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
#if !NO_UNITY
using UnityEngine;
#endif

namespace Fun
{
    public enum AnnounceResult
    {
        kSuccess,
        kInvalidUrl,
        kInvalidJson,
        kListIsNullOrEmpty,
        kExceptionError
    }


    public class FunapiAnnouncement
    {
        public void Init (string url)
        {
            // Url
            host_url_ = url;

            // Check resource directory
            local_path_ = FunapiUtils.GetLocalDataPath + kLocalPath;
            if (!Directory.Exists(local_path_))
                Directory.CreateDirectory(local_path_);

            // Download handler
            web_client_.DownloadDataCompleted += new DownloadDataCompletedEventHandler(DownloadDataCompleteCb);
            web_client_.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCompleteCb);
        }

        public void UpdateList ()
        {
            if (string.IsNullOrEmpty(host_url_))
            {
                Debug.Log("url is null or empty.");
                OnResultCallback(AnnounceResult.kInvalidUrl);
                return;
            }

            // Request a list of announcements.
            web_client_.DownloadDataAsync(new Uri(host_url_ + kAnnouncementsUrl));
        }

        public Dictionary<string, object> GetAnnouncement (int index)
        {
            if (index < 0 || index >= announce_list_.Count)
                return null;

            return announce_list_[index];
        }

        public int ListCount
        {
            get { return announce_list_.Count; }
        }

        private void DownloadDataCompleteCb (object sender, DownloadDataCompletedEventArgs ar)
        {
            try
            {
                if (ar.Error != null)
                {
                    Debug.Log("Exception Error: " + ar.Error);
                    OnResultCallback(AnnounceResult.kExceptionError);
                    DebugUtils.Assert(false);
                }
                else
                {
                    // Parse json
                    string data = Encoding.ASCII.GetString(ar.Result);
                    Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;
                    if (json == null)
                    {
                        Debug.Log("Deserialize json failed. json: " + data);
                        OnResultCallback(AnnounceResult.kInvalidJson);
                        return;
                    }

                    DebugUtils.Assert(json.ContainsKey("list"));
                    List<object> list = json["list"] as List<object>;
                    if (list == null || list.Count <= 0)
                    {
                        Debug.Log("Invalid announcement list. list: " + list);
                        OnResultCallback(AnnounceResult.kListIsNullOrEmpty);
                        return;
                    }

                    announce_list_.Clear();

                    foreach (Dictionary<string, object> node in list)
                    {
                        announce_list_.Add(node);

                        // download image
                        if (node.ContainsKey("image_url") && node.ContainsKey("image_md5"))
                        {
                            CheckDownloadImage(node["image_url"] as string, node["image_md5"] as string);
                        }
                    }

                    Debug.Log("Announcement has been updated. total count: " + announce_list_.Count);

                    if (image_list_.Count > 0)
                    {
                        // Request a file.
                        KeyValuePair<string, string> item = image_list_[0];
                        web_client_.DownloadFileAsync(new Uri(item.Key), item.Value);
                        Debug.Log("Download url: " + item.Key);
                        Debug.Log("Image path: " + item.Value);
                    }
                    else
                    {
                        OnResultCallback(AnnounceResult.kSuccess);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in DownloadDataCompleteCb: " + e.ToString());
                OnResultCallback(AnnounceResult.kExceptionError);
            }
        }

        private void DownloadFileCompleteCb (object sender, System.ComponentModel.AsyncCompletedEventArgs ar)
        {
            try
            {
                if (ar.Error != null)
                {
                    Debug.Log("Exception Error: " + ar.Error);
                    OnResultCallback(AnnounceResult.kExceptionError);
                    DebugUtils.Assert(false);
                }
                else
                {
                    image_list_.RemoveAt(0);
                    if (image_list_.Count > 0)
                    {
                        KeyValuePair<string, string> item = image_list_[0];
                        web_client_.DownloadFileAsync(new Uri(item.Key), item.Value);
                        Debug.Log("Download url: " + item.Key);
                        Debug.Log("Image path: " + item.Value);
                    }
                    else
                    {
                        Debug.Log("Download file completed.");
                        OnResultCallback(AnnounceResult.kSuccess);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in DownloadFileCompleteCb: " + e.ToString());
                OnResultCallback(AnnounceResult.kExceptionError);
            }
        }

        private void CheckDownloadImage (string url, string imgmd5)
        {
            if (url.Length <= 0 || imgmd5.Length <= 0)
                return;

            string path = local_path_;
            int offset = url.LastIndexOf('/');
            if (offset >= 0)
                path += url.Substring(offset, url.Length - offset);

            if (File.Exists(path))
            {
                // Check md5
                using (MD5 md5 = MD5.Create())
                {
                    byte[] buffer = File.ReadAllBytes(path);
                    md5.ComputeHash(buffer);

                    string hash = "";
                    foreach (byte n in md5.Hash)
                        hash += n.ToString("x2");

                    if (hash != imgmd5)
                        image_list_.Add(new KeyValuePair<string, string>(url, path));
                }
            }
            else
            {
                url = host_url_ + kImagesUrl + url;
                image_list_.Add(new KeyValuePair<string, string>(url, path));
            }
        }

        private void OnResultCallback (AnnounceResult result)
        {
            if (ResultCallback != null)
            {
                ResultCallback(result);
            }
        }


        // Url-related constants.
        private static readonly string kLocalPath = "/announce";
        private static readonly string kImagesUrl = "/images";
        private static readonly string kAnnouncementsUrl = "/announcements";

        // member variables.
        private string host_url_ = "";
        private string local_path_ = "";
        private WebClient web_client_ = new WebClient();
        private List<Dictionary<string, object>> announce_list_ = new List<Dictionary<string, object>>();
        private List<KeyValuePair<string, string>> image_list_ = new List<KeyValuePair<string, string>>();

        // Result callback delegate
        public delegate void EventHandler(AnnounceResult result);
        public event EventHandler ResultCallback;
    }
}
