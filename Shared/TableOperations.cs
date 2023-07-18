﻿using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using System.Collections.Generic;
using System.Linq;
using static LogicAppAdvancedTool.Program;

namespace LogicAppAdvancedTool
{
    public class TableOperations
    {
        public static List<TableEntity> QueryTable(string tableName, string query, string[] select = null)
        {
            List<TableEntity> tableEntities = new List<TableEntity>();

            TableServiceClient serviceClient = new TableServiceClient(AppSettings.ConnectionString);
            Pageable<TableItem> results = serviceClient.Query(filter: $"TableName eq '{tableName}'");

            if (results.Count() == 0)
            {
                throw new UserInputException($"Table - {tableName} not exist, please check whether parameters are correct or not.");
            }

            TableClient tableClient = new TableClient(AppSettings.ConnectionString, tableName);

            tableEntities = tableClient.Query<TableEntity>(filter: query, select: select).ToList();

            return tableEntities;
        }
    }
}
