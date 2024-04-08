﻿using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure.ResourceStack.Common.Extensions;
using Azure.Data.Tables.Models;
using Azure;
using McMaster.Extensions.CommandLineUtils;

namespace LogicAppAdvancedTool.Operations
{
    public static class MergeRunHistory
    {
        public static void Run(string workflowName, string date)
        {
            List<TableEntity> targetWorkflow = TableOperations.QueryCurrentWorkflowByName(workflowName);

            if (targetWorkflow.Count == 0)
            {
                throw new UserInputException($"Cannot find existing workflow with name {workflowName}, please review your input.");
            }

            Console.WriteLine($"Existing workflow named {workflowName} found.");
            Console.WriteLine($"Retrieving deleted worklfows named {workflowName}");

            string targetFlowID = targetWorkflow[0].GetString("FlowId");

            List<TableEntity> entities = TableOperations.QueryMainTable($"FlowName eq '{workflowName}' and FlowId ne '{targetFlowID}'", select: new string[] { "FlowName", "FlowId", "ChangedTime" })
                                .GroupBy(t => t.GetString("FlowId"))
                                .Select(g => g.OrderByDescending(
                                    x => x.GetDateTimeOffset("ChangedTime"))
                                    .FirstOrDefault())
                                .ToList();

            if (entities.Count == 0)
            {
                throw new UserInputException($"No deleted workflow found with name {workflowName}, please review your input or use 'ListVersions' command to list them.");
            }

            Console.WriteLine($"{entities.Count} deleted workflow(s) found.");

            ConsoleTable consoleTable = new ConsoleTable("Index", "Workflow Name", "Flow ID", "Last Updated Time (UTC)");

            int index = 0;

            foreach (TableEntity entity in entities)
            {
                string flowName = entity.GetString("FlowName");
                string changedTime = entity.GetDateTimeOffset("ChangedTime")?.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string flowID = entity.GetString("FlowId");

                consoleTable.AddRow((++index).ToString(), flowName, flowID, changedTime);
            }

            consoleTable.Print();

            string sourceFlowID;

            if (entities.Count == 1)
            {
                sourceFlowID = entities[0].GetString("FlowId");
                Console.WriteLine($"Only 1 workflow found, using default workflow id: {sourceFlowID}");
            }
            else
            {
                Console.WriteLine($"There are {entities.Count} worklfows found in Storage Table, due to workflow overwritten (delete and create workflow with same name).");
                Console.WriteLine("Please enter the Index which you would like to restore the run history");

                int rowID = Int32.Parse(Console.ReadLine());
                sourceFlowID = entities[rowID - 1].GetString("FlowId");
            }
            
            Console.WriteLine($"""
                Merge information:
                1. Workflow name: {workflowName}, source flow id: {sourceFlowID}, target flow id: {targetFlowID}, date: {date}.
                2. For the tables which contain huge mounts of records (eg: actions/variables), it will take a while for merging.
                3. Make sure before you run the command, Logic App doesn't experience high CPU or memory issue. 
                4. Merging process will consume ~120 MB memory and 10% CPU (CPU usage is based on WS1 ASP).
                5. IMPORTENT!!! This operation cannot be reverted!!!
                """);
            
            string confirmationMessage = "WARNING!!! Please review above information and input for confirmation to merge run history:";
            if (!Prompt.GetYesNo(confirmationMessage, false, ConsoleColor.Red))
            {
                throw new UserCanceledException("Operation Cancelled");
            }

            //We need to create new records and change workflow id to existing one in main table
            OverwriteFlowId(sourceFlowID, targetFlowID);

            string workflowprefix = CommonOperations.GenerateLogicAppPrefix();
            string selectWorkflowPrefix = $"flow{workflowprefix}{StoragePrefixGenerator.Generate(sourceFlowID.ToLower())}";
            string currentWorkflowPrefix = $"flow{workflowprefix}{StoragePrefixGenerator.Generate(targetFlowID.ToLower())}";

            List<string> tables = ListActionVarTables(selectWorkflowPrefix, date);

            MergeTable($"{selectWorkflowPrefix}runs", $"{currentWorkflowPrefix}runs", sourceFlowID, targetFlowID);
            MergeTable($"{selectWorkflowPrefix}flows", $"{currentWorkflowPrefix}flows", sourceFlowID, targetFlowID);
            MergeTable($"{selectWorkflowPrefix}histories", $"{currentWorkflowPrefix}histories", sourceFlowID, targetFlowID);

            foreach (string table in tables)
            {
                string targetTableName = table.Replace(selectWorkflowPrefix, currentWorkflowPrefix);

                MergeTable(table, targetTableName, sourceFlowID, targetFlowID);
            }
        }

        private static List<string> ListActionVarTables(string prefix, string date)
        {
            TableServiceClient serviceClient = new TableServiceClient(AppSettings.ConnectionString);

            return serviceClient.Query().ToList()
                    .Where(s => s.Name.StartsWith(prefix) && s.Name.Contains(date)  && (s.Name.EndsWith("actions") || s.Name.EndsWith("variables")))
                    .Select(t => t.Name)
                    .ToList();
        }

        private static void MergeTable(string sourceTableName, string targetTableName, string sourceID, string targetID)
        {
            Console.WriteLine($"Merging {sourceTableName} to {targetTableName}");

            TableServiceClient serviceClient = new TableServiceClient(AppSettings.ConnectionString);
            Pageable<TableItem> sourceTableRecords = serviceClient.Query(filter: $"TableName eq '{sourceTableName}'");

            if (sourceTableRecords.Count() == 0)
            {
                Console.WriteLine($"Skip merge for {sourceTableName} due to not found.");

                return;
            }

            TableClient targetTableClient = new TableClient(AppSettings.ConnectionString, targetTableName);
            targetTableClient.CreateIfNotExists();

            TableClient sourceTableClient = new TableClient(AppSettings.ConnectionString, sourceTableName);

            //Split records into pages for memory usage consideration
            Pageable<TableEntity> entities = sourceTableClient.Query<TableEntity>(maxPerPage: 1000);

            int pageIndex = 0;           

            foreach (Page<TableEntity> page in entities.AsPages())
            {
                Dictionary<string, List<TableTransactionAction>> actions = new Dictionary<string, List<TableTransactionAction>>();

                pageIndex++;

                //provide some infomation in console for long running jobs
                //just in case there's no infomation for to long time and isers believe the process stuck
                if (page.Values.Count == 1000 || pageIndex != 1)    
                { 
                    Console.WriteLine($"Merging page {pageIndex} with {page.Values.Count} records.");
                }

                foreach (TableEntity te in page.Values)
                {
                    te["FlowId"] = targetID;
                    te.RowKey = te.RowKey.Replace(sourceID.ToUpper(), targetID.ToUpper());

                    string partitionKey = StoragePrefixGenerator.GeneratePartitionKey(te.RowKey);
                    te.PartitionKey = partitionKey;

                    if (!actions.ContainsKey(partitionKey))
                    {
                        actions.Add(partitionKey, new List<TableTransactionAction>());    
                    }

                    actions[partitionKey].Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, te));

                    if (actions[partitionKey].Count == 100)
                    {
                        targetTableClient.SubmitTransaction(actions[partitionKey]);

                        actions.Remove(partitionKey);
                    }
                }

                foreach (List<TableTransactionAction> action in actions.Values)
                { 
                    targetTableClient.SubmitTransaction(action);
                }
            }
        }

        public static void OverwriteFlowId(string sourceID, string targetID)
        {
            string filter = $"FlowId eq '{sourceID}'";
            List<TableEntity> deletedWorkflowRecords =  TableOperations.QueryMainTable(filter);

            TableClient tableClient = new TableClient(AppSettings.ConnectionString, TableOperations.DefinitionTableName);

            foreach (TableEntity te in deletedWorkflowRecords)
            {
                TableEntity updatedEntity = new TableEntity
                {
                    { "FlowId", targetID }
                };

                updatedEntity = te;
                updatedEntity["FlowId"] = targetID;
                updatedEntity.PartitionKey = te.PartitionKey;
                updatedEntity.RowKey = te.RowKey.Replace(sourceID.ToUpper(), targetID.ToUpper());
 
                tableClient.UpsertEntity<TableEntity>(updatedEntity);
            }
        }
    }
}
