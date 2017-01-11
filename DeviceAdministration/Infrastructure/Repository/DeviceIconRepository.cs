﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.Models;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Configurations;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Helpers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.Repository
{
    public class DeviceIconRepository : IDeviceIconRepository
    {
        private readonly string _storageAccountConnectionString;
        private readonly string _deviceIconsBlobStoreContainerName;
        private readonly string _uploadedFolder = "uploaded";
        private readonly string _appliedFolder = "applied";
        private readonly string _writePolicyName;
        private readonly IBlobStorageClient _blobStorageClient;

        public DeviceIconRepository(IConfigurationProvider configurationProvider, IBlobStorageClientFactory blobStorageClientFactory)
        {
            _storageAccountConnectionString = configurationProvider.GetConfigurationSettingValue("device.StorageConnectionString");
            _deviceIconsBlobStoreContainerName = configurationProvider.GetConfigurationSettingValueOrDefault("DeviceIconStoreContainerName", "deviceicons");
            _blobStorageClient = blobStorageClientFactory.CreateClient(_storageAccountConnectionString, _deviceIconsBlobStoreContainerName);
            _writePolicyName = _deviceIconsBlobStoreContainerName + "-write";
            _blobStorageClient.SetPublicPolicyType(BlobContainerPublicAccessType.Blob);
            CreateSASPolicyIfNotExist();
        }

        public async Task<DeviceIcon> AddIcon(string fileName, Stream fileStream)
        {
            // replace '.' with '_' so that the name can be used in MVC Url route.
            var name = Path.GetFileName(fileName).Replace(".", "_");
            var extension = Path.GetExtension(fileName);
            string contentType = MimeMapping.GetMimeMapping(string.IsNullOrEmpty(extension) ? "image/png" : extension);
            var uploadedBlob = await _blobStorageClient.UploadFromStreamAsync($"{_uploadedFolder}/{name}",
                contentType,
                fileStream,
                AccessCondition.GenerateEmptyCondition(),
                null,
                null);

            var appliedBlob = await _blobStorageClient.MoveBlob(uploadedBlob.Name, $"{_appliedFolder}/{name}");
            return new DeviceIcon(Path.GetFileName(appliedBlob.Name), appliedBlob);
        }

        public async Task<DeviceIcon> GetIcon(string name)
        {
            var blob = await _blobStorageClient.GetBlob($"{_appliedFolder}/{name}");
            return new DeviceIcon(name, blob);
        }

        public async Task<DeviceIconResult> GetIcons(int skip, int take)
        {
            string folderPrefix = _appliedFolder + "/";
            var blobs = await _blobStorageClient.ListBlobs(folderPrefix, true);
            var icons = blobs.Select(b =>
            {
                string name = b.Name.Substring(folderPrefix.Length);
                return new DeviceIcon(name, b);
            });

            return new DeviceIconResult
            {
                TotalCount = icons.Count(),
                Results = icons.OrderByDescending(i => i.LastModified).Skip(skip).Take(take),
            };
        }

        public async Task<DeviceIcon> SaveIcon(string name)
        {
            var appliedBlob = await _blobStorageClient.MoveBlob($"{_uploadedFolder}/{name}", $"{_appliedFolder}/{name}");
            return new DeviceIcon(Path.GetFileName(appliedBlob.Name), appliedBlob);
        }

        public async Task<bool> DeleteIcon(string name)
        {
            return  await _blobStorageClient.DeleteBlob($"{_appliedFolder}/{name}");
        }

        public string GetIconStorageUriPrefix()
        {
            return  string.Format("{0}/{1}/", _blobStorageClient.GetContainerUri().Result, _appliedFolder);
        }

        private void CreateSASPolicyIfNotExist()
        {
            var writePolicy = new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Add | SharedAccessBlobPermissions.Delete | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write,
                SharedAccessExpiryTime = DateTime.UtcNow.AddYears(10)
            };

            _blobStorageClient.CreateSASPolicyIfNotExist(_writePolicyName, writePolicy).Wait();
        }
    }
}
