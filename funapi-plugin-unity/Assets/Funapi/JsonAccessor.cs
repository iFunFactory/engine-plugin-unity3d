// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.Collections.Generic;


namespace Fun
{
    // Container to hold json-related functions.
    public abstract class JsonAccessor
    {
        public abstract string Serialize(object json_obj);
        public abstract object Deserialize(string json_str);
        public abstract string GetStringField(object json_obj, string field_name);
        public abstract void SetStringField(object json_obj, string field_name, string value);
        public abstract Int64 GetIntegerField(object json_obj, string field_name);
        public abstract void SetIntegerField(object json_obj, string field_name, Int64 value);
        public abstract bool HasField(object json_obj, string field_name);
        public abstract void RemoveStringField(object json_obj, string field_name);
        public abstract object Clone(object json_obj);
    }


    // Default json accessor
    public class DictionaryJsonAccessor : JsonAccessor
    {
        public override string Serialize(object json_obj)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return Json.Serialize(d);
        }

        public override object Deserialize(string json_string)
        {
            return Json.Deserialize(json_string) as Dictionary<string, object>;
        }

        public override string GetStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return d[field_name] as string;
        }

        public override void SetStringField(object json_obj, string field_name, string value)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d[field_name] = value;
        }

        public override Int64 GetIntegerField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return Convert.ToInt64(d [field_name]);
        }

        public override void SetIntegerField(object json_obj, string field_name, Int64 value)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert (d != null);
            d [field_name] = value;
        }

        public override bool HasField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert (d != null);
            return d.ContainsKey (field_name);
        }

        public override void RemoveStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d.Remove(field_name);
        }

        public override object Clone(object json_obj)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return new Dictionary<string, object>(d);

        }
    }
}  // namespace Fun
