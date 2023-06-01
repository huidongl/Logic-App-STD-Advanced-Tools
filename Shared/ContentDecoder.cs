﻿using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Storage.Blobs.Models;
using Microsoft.VisualBasic;
using Microsoft.WindowsAzure.ResourceStack.Common.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Dynamic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using static LogicAppAdvancedTool.Program;

namespace LogicAppAdvancedTool
{
    public class ContentDecoder
    {
        private readonly string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
        public dynamic RawPayload { get; private set; }
        public CommonPayloadStructure PayloadBody { get; private set; }
        public bool IsBlobLink { get; private set; }
        public string BlobUri { get; private set; }
        public string InlinedContent { get; private set; }
        public bool IsEmpty { get; private set; }
        public ContentDecoder(byte[] binaryContent) 
        {
            string rawContent = DecompressContent(binaryContent) ?? string.Empty;

            if (!String.IsNullOrEmpty(rawContent))
            {
                RawPayload = JsonConvert.DeserializeObject(rawContent);

                if (RawPayload.nestedContentLinks != null)
                {
                    PayloadBody = ((JObject)RawPayload).ToObject<ConnectorPayloadStructure>().nestedContentLinks.body;
                }
                else
                {
                    PayloadBody = ((JObject)RawPayload).ToObject<CommonPayloadStructure>();
                }

                string Base64Content = PayloadBody.inlinedContent;
                if (!String.IsNullOrEmpty(Base64Content))
                {
                    InlinedContent = Encoding.UTF8.GetString(Convert.FromBase64String(Base64Content));
                }
                else
                {
                    InlinedContent = String.Empty;
                }

                BlobUri = PayloadBody.uri ?? String.Empty;
                IsBlobLink = !String.IsNullOrEmpty(BlobUri);
            }
            else 
            {
                IsEmpty = true;
            }
        }

        public bool SearchKeyword(string keyword, bool includeBlob = false)
        {
            if (IsEmpty)
            {
                return false;
            }

            if (!String.IsNullOrEmpty(InlinedContent) && InlinedContent.Contains(keyword))
            {
                return true;
            }

            if (IsBlobLink && includeBlob) 
            { 
                string contentInBlob = GetBlobContent(BlobUri);

                if (contentInBlob.StartsWith(_byteOrderMarkUtf8))
                {
                    contentInBlob = contentInBlob.Remove(0, _byteOrderMarkUtf8.Length);
                }

                if (contentInBlob.Contains("$content-type") && contentInBlob.Contains("application/octet-stream"))  //quick and dirty implementation
                {
                    StreamHistoryBlob streamContent = JsonConvert.DeserializeObject<StreamHistoryBlob>(contentInBlob);
                    if (String.IsNullOrEmpty(streamContent.Content))
                    {
                        return false;
                    }

                    contentInBlob = Encoding.UTF8.GetString(Convert.FromBase64String(streamContent.Content));
                }

                if (contentInBlob.Contains(keyword))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
