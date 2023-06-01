﻿using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.VisualBasic;
using Microsoft.WindowsAzure.ResourceStack.Common.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LogicAppAdvancedTool
{
    public partial class Program
    {
        /// <summary>
        /// Retrieve the table name which contains all the workflow definitions
        /// </summary>
        /// <param name="ConnectionString"></param>
        /// <returns></returns>
        private static string GetMainTableName(string logicAppName)
        {
            string tableName = $"flow{StoragePrefixGenerator.Generate(logicAppName.ToLower())}flows";

            TableServiceClient serviceClient = new TableServiceClient(AppSettings.ConnectionString);

            //Double check whether the table exists
            Pageable<TableItem> results = serviceClient.Query(filter: $"TableName eq '{tableName}'");

            if (results.Count() != 0)
            {
                return tableName;
            }

            throw new UserInputException("No table found in Azure Storage Account, please check whether the Logic App Name correct or not.");
        }

        /// <summary>
        /// Retrieve the table name which contains all the workflow definitions
        /// </summary>
        /// <param name="ConnectionString"></param>
        /// <returns></returns>
        private static string GetMainTablePrefix(string logicAppName)
        {
            string tablePrefix = StoragePrefixGenerator.Generate(logicAppName.ToLower());
            string tableName = $"flow{tablePrefix}flows";

            TableServiceClient serviceClient = new TableServiceClient(AppSettings.ConnectionString);

            //Double check whether the table exists
            Pageable<TableItem> results = serviceClient.Query(filter: $"TableName eq '{tableName}'");

            if (results.Count() != 0)
            {
                return tablePrefix;
            }

            Console.WriteLine("No table found in Azure Storage Account, please check whether the Logic App Name correct or not.");
            return string.Empty;
        }

        private static string GenerateLogicAppPrefix(string logicAppName) 
        {
            string tablePrefix = StoragePrefixGenerator.Generate(logicAppName.ToLower());

            return tablePrefix;
        }

        private static string GenerateWorkflowTablePrefix(string logicAppName, string workflowName)
        {
            string mainTableName = GetMainTableName(logicAppName);

            TableClient tableClient = new TableClient(AppSettings.ConnectionString, mainTableName);
            Pageable<TableEntity> tableEntities = tableClient.Query<TableEntity>(filter: $"FlowName eq '{workflowName}'");

            if (tableEntities.Count() == 0)
            {
                throw new UserInputException($"{workflowName} cannot be found in storage table, please check whether workflow name is correct.");
            }

            string logicAppPrefix = StoragePrefixGenerator.Generate(logicAppName.ToLower());

            string workflowID = tableEntities.First<TableEntity>().GetString("FlowId");
            string workflowPrefix = StoragePrefixGenerator.Generate(workflowID.ToLower());

            return $"{logicAppPrefix}{workflowPrefix}";
        }

        private static string BackupCurrentSite()
        {
            string filePath = $"{Directory.GetCurrentDirectory()}/Backup_{DateTime.Now.ToString("yyyyMMddHHmmss")}.zip";

            ZipFile.CreateFromDirectory("C:/home/site/wwwroot/", filePath, CompressionLevel.Fastest, false);

            return filePath;
        }

        #region Deflate/infalte related
        public static string DecompressContent(byte[] content)
        {
            if (content == null)
            {
                return null;
            }

            string result = DeflateCompressionUtility.Instance.InflateString(new MemoryStream(content));

            return result;
        }

        /// <summary>
        /// Compress string to Inflate stream
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public static byte[] CompressContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            MemoryStream compressedStream = DeflateCompressionUtility.Instance.DeflateString(content);
            byte[] compressedBytes = compressedStream.ToArray();

            return compressedBytes;
        }
        #endregion

        #region Storage operation
        public static string GetBlobContent(string blobUri, int contentSize = 1024*1024)
        {
            Uri uri = new Uri(blobUri);
            ConnectionInfo info = new ConnectionInfo(AppSettings.ConnectionString);
            StorageSharedKeyCredential cred = new StorageSharedKeyCredential(info.AccountName, info.AccountKey);

            BlobClient client = new BlobClient(uri, cred);

            BlobDownloadResult result = client.DownloadContent().Value;

            long blobSize = client.GetProperties().Value.ContentLength;

            if (blobSize > contentSize)
            {
                string blobName = blobUri.Split("/").Last();

                Console.WriteLine($"{blobName} content size is larger than 1MB, skip content check for this blob due to memory saving.");

                return String.Empty;
            }

            Stream contentStream = result.Content.ToStream();
            string content;

            using (BinaryReader br = new BinaryReader(contentStream))
            {
                byte[] b = br.ReadBytes((int)contentStream.Length);

                content = DecompressContent(b);
            }

            return content;
        }

        public class ConnectionInfo
        {
            private Dictionary<string, string> CSInfo;
            public ConnectionInfo(string connectionString)
            {
                CSInfo = new Dictionary<string, string>();

                string[] infos = connectionString.Split(";");
                foreach (string info in infos)
                {
                    int index = info.IndexOf('=');
                    string key = info.Substring(0, index);
                    string value = info.Substring(index + 1, info.Length - index - 1);

                    CSInfo.Add(key, value);
                }

                BlobEndpoint = $"{AccountName}.blob.{EndpointSuffix}";
                FileEndpoint = $"{AccountName}.file.{EndpointSuffix}";
                QueueEndpoint = $"{AccountName}.queue.{EndpointSuffix}";
                TableEndpoint = $"{AccountName}.table.{EndpointSuffix}";
            }

            public string BlobEndpoint { get; private set; }
            public string FileEndpoint { get; private set; }
            public string QueueEndpoint { get; private set; }
            public string TableEndpoint { get; private set; }

            public string DefaultEndpointsProtocol
            {
                get
                {
                    return CSInfo["DefaultEndpointsProtocol"];
                }
            }

            public string AccountName
            {
                get
                {
                    return CSInfo["AccountName"];
                }
            }

            public string AccountKey
            {
                get
                {
                    return CSInfo["AccountKey"];
                }
            }

            public string EndpointSuffix
            {
                get
                {
                    return CSInfo["EndpointSuffix"];
                }
            }
        }
        #endregion
    }
}
