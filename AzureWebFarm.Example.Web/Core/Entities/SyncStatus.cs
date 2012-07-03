﻿using System;

namespace AzureWebFarm.Example.Web.Core.Entities
{
    public class SyncStatus
    {
        public DateTime SyncTimestamp { get; set; }

        public string DeploymentId { get; set; }

        public string RoleInstanceId { get; set; }

        public string SiteName { get; set; }

        public SyncInstanceStatus Status { get; set; }

        public bool IsOnline { get; set; }
    }
}