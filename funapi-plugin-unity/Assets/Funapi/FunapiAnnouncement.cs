// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;


namespace Fun
{
    public enum AnnounceResult
    {
        kSucceeded,
        kNeedInitialize,
        kInvalidJson,
        kListIsNullOrEmpty,
        kExceptionError
    }


    public class FunapiAnnouncement
    {
        public void Init (string url)
        {
            // Sets host url
            host_url_ = url;

            // Checks resource directory
            local_path_ = FunapiUtils.GetLocalDataPath + kLocalPath;
            if (!Directory.Exists(local_path_))
                Directory.CreateDirectory(local_path_);

            // Download handler
            web_client_ = new WebClient();
            web_client_.DownloadDataCompleted += downloadDataCompleteCb;
            web_client_.DownloadFileCompleted += downloadFileCompleteCb;
        }

        public void UpdateList (int max_count)
        {
            if (web_client_ == null || string.IsNullOrEmpty(host_url_))
            {
                FunDebug.LogWarning("Announcement.UpdateList - You must call Init() function first.");
                onResult(AnnounceResult.kNeedInitialize);
                return;
            }

            // Request a list of announcements.
            string url = host_url_ + kAnnouncementsUrl + "?count=" + max_count;
            web_client_.DownloadDataAsync(new Uri(url));
        }

        public int ListCount
        {
            get { return announce_list_.Count; }
        }

        public Dictionary<string, object> GetAnnouncement (int index)
        {
            if (index < 0 || index >= announce_list_.Count)
                return null;

            return announce_list_[index];
        }

        public string GetImagePath (int index)
        {
            Dictionary<string, object> item = GetAnnouncement(index);
            if (item == null || !item.ContainsKey(kImageUrlKey))
                return null;

            string path = item[kImageUrlKey] as string;
            return local_path_ + Path.GetFileName(path);
        }

        void downloadDataCompleteCb (object sender, DownloadDataCompletedEventArgs ar)
        {
            try
            {
                if (ar.Error != null)
                {
                    throw ar.Error;
                }

                // Parse json
                string data = Encoding.UTF8.GetString(ar.Result);
                Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;
                if (json == null)
                {
                    FunDebug.LogWarning("Announcement - Deserialize json failed. json: {0}", data);
                    onResult(AnnounceResult.kInvalidJson);
                    return;
                }

                FunDebug.Assert(json.ContainsKey("list"));
                List<object> list = json["list"] as List<object>;
                if (list == null || list.Count <= 0)
                {
                    if (list == null)
                        FunDebug.LogWarning("Announcement - Announcement list is null.");
                    else if (list.Count <= 0)
                        FunDebug.LogWarning("Announcement - There is no announcement list.");

                    onResult(AnnounceResult.kListIsNullOrEmpty);
                    return;
                }

                announce_list_.Clear();

                foreach (Dictionary<string, object> node in list)
                {
                    announce_list_.Add(node);

                    // download image
                    if (node.ContainsKey(kImageUrlKey) && node.ContainsKey(kImageMd5Key))
                    {
                        checkDownloadImage(node[kImageUrlKey] as string, node[kImageMd5Key] as string);
                    }
                }

                FunDebug.Log("Announcement list has been updated. total list count: {0}", announce_list_.Count);

                if (image_list_.Count > 0)
                {
                    // Request a file.
                    KeyValuePair<string, string> item = image_list_[0];
                    web_client_.DownloadFileAsync(new Uri(item.Key), item.Value);
                    FunDebug.DebugLog1("Download announcement image: {0}", item.Key);
                }
                else
                {
                    onResult(AnnounceResult.kSucceeded);
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in Announcement.downloadDataCompleteCb:\n{0}", e.ToString());
                onResult(AnnounceResult.kExceptionError);
            }
        }

        void downloadFileCompleteCb (object sender, System.ComponentModel.AsyncCompletedEventArgs ar)
        {
            try
            {
                if (ar.Error != null)
                {
                    throw ar.Error;
                }

                image_list_.RemoveAt(0);
                if (image_list_.Count > 0)
                {
                    KeyValuePair<string, string> item = image_list_[0];
                    web_client_.DownloadFileAsync(new Uri(item.Key), item.Value);
                    FunDebug.DebugLog1("Download announcement image: {0}", item.Key);
                }
                else
                {
                    FunDebug.Log("All announcement images has been downloaded.\npath:{0}", local_path_);
                    onResult(AnnounceResult.kSucceeded);
                }
            }
            catch (Exception e)
            {
                FunDebug.LogError("Failure in Announcement.downloadFileCompleteCb:\n{0}", e.ToString());
                onResult(AnnounceResult.kExceptionError);
            }
        }

        void checkDownloadImage (string url, string imgmd5)
        {
            if (url.Length <= 0 || imgmd5.Length <= 0)
                return;

            string path = local_path_ + Path.GetFileName(url);
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

        void onResult (AnnounceResult result)
        {
            if (ResultCallback != null)
                ResultCallback(result);
        }


        // Result callback delegate
        public delegate void EventHandler(AnnounceResult result);
        public event EventHandler ResultCallback;

        // Url-related constants.
        const string kLocalPath = "/announce/";
        const string kImagesUrl = "/images";
        const string kImageUrlKey = "image_url";
        const string kImageMd5Key = "image_md5";
        const string kAnnouncementsUrl = "/announcements/";

        // member variables.
        string host_url_ = "";
        string local_path_ = "";
        WebClient web_client_ = null;
        List<Dictionary<string, object>> announce_list_ = new List<Dictionary<string, object>>();
        List<KeyValuePair<string, string>> image_list_ = new List<KeyValuePair<string, string>>();
    }
}
