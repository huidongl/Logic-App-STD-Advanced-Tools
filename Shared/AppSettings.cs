﻿using LogicAppAdvancedTool.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace LogicAppAdvancedTool
{
    public class AppSettings
    {
        public static string ConnectionString
        {
            get
            {
                return Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            }
        }

        public static string SubscriptionID
        {
            get 
            {
                return Environment.GetEnvironmentVariable("WEBSITE_OWNER_NAME").Split('+')[0];
            }
        }

        public static string ResourceGroup
        {
            get
            { 
                return Environment.GetEnvironmentVariable("WEBSITE_RESOURCE_GROUP");
            }
        }

        public static string Region
        {
            get
            {
                return Environment.GetEnvironmentVariable("REGION_NAME");
            }
        }

        public static string LogicAppName
        {
            get
            {
                return Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            }
        }

        public static string MSIEndpoint
        {
            get
            {
                return Environment.GetEnvironmentVariable("MSI_ENDPOINT");
            }
        }

        public static string MSISecret
        {
            get
            {
                return Environment.GetEnvironmentVariable("MSI_SECRET");
            }
        }

        public static string RootFolder
        {
            get
            {
                return "C:\\home\\site\\wwwroot";
            }
        }

        public static string GetRemoteAppsettings()
        {
            string Url = $"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{ResourceGroup}/providers/Microsoft.Web/sites/{LogicAppName}/config/appsettings/list?api-version=2022-03-01";

            MSIToken token = MSITokenService.RetrieveToken("https://management.azure.com");
            string response = HttpOperations.HttpGetWithToken(Url, "POST", token.access_token, $"Cannot retrieve appsettings for {LogicAppName}");

            string appSettings = JsonConvert.SerializeObject(JObject.Parse(response)["properties"], Formatting.Indented);

            return appSettings;
        }

        public static void UpdateRemoteAppsettings(string appsettingContent)
        {
            string appsettingsUrl = $"https://management.azure.com/subscriptions/{SubscriptionID}/resourceGroups/{ResourceGroup}/providers/Microsoft.Web/sites/{LogicAppName}/config/appsettings/list?api-version=2022-03-01";
            MSIToken token = MSITokenService.RetrieveToken("https://management.azure.com");
            string response = HttpOperations.HttpGetWithToken(appsettingsUrl, "POST", token.access_token, $"Cannot retrieve appsettings for {LogicAppName}");
            JToken appSettingRuntime = JObject.Parse(response);

            appSettingRuntime["properties"] = JObject.Parse(appsettingContent);

            string updateUrl = $"https://management.azure.com/subscriptions/{AppSettings.SubscriptionID}/resourceGroups/{ResourceGroup}/providers/Microsoft.Web/sites/{LogicAppName}/config/appsettings?api-version=2022-03-01";
            string updatedPayload = JsonConvert.SerializeObject(appSettingRuntime);
            HttpOperations.HttpSendWithToken(updateUrl, "PUT", updatedPayload, token.access_token, $"Failed to restore appsettings.");
        }
    }
}
