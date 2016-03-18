using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Management.Compute;
using Microsoft.WindowsAzure.Management.Compute.Models;
using Microsoft.WindowsAzure.Management.Monitoring.Autoscale;
using Microsoft.WindowsAzure.Management.Monitoring.Autoscale.Models;
using Microsoft.WindowsAzure.Management.Monitoring.Utilities;
using Microsoft.WindowsAzure.Management.Storage;
using Microsoft.WindowsAzure.Management.Storage.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace AzureDeployer.Console
{
    public class AzureManagementService : IDisposable
    {
        private CertificateCloudCredentials cloudCredentials;
        private StorageManagementClient storageClient;
        private ComputeManagementClient computeClient;

        public AzureManagementService(string subscriptionId, string base64Certificate) 
        {
            cloudCredentials = new CertificateCloudCredentials(
                subscriptionId,
                new X509Certificate2(Convert.FromBase64String(base64Certificate)));

            storageClient = CloudContext.Clients.CreateStorageManagementClient(cloudCredentials);
            computeClient = CloudContext.Clients.CreateComputeManagementClient(cloudCredentials);
            
        }

        public async Task CreateStorageAccount(string region, string accountName, string accountType) 
        {
            await storageClient.StorageAccounts.CreateAsync(new StorageAccountCreateParameters()
            {
                Location = region,
                Name = accountName,
                AccountType = accountType
            });
        }

        public async Task<string> GetStorageAccountConString(string accountName)
        {
            StorageAccountGetKeysResponse keys = await storageClient.StorageAccounts.GetKeysAsync(accountName);

            return String.Format(CultureInfo.InvariantCulture,
                "DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1};",
                accountName, keys.SecondaryKey);
        }

        public async Task CreateCloudService(string region, string serviceName)
        {
            await computeClient.HostedServices.CreateAsync(new HostedServiceCreateParameters()
            {
                Location = region,
                ServiceName = serviceName
            });
        }

        public async Task DeployCloudService(string serviceName, Uri pkgUri, string configurationFile)
        {
            await computeClient.Deployments.CreateAsync(
                serviceName,
                DeploymentSlot.Production,
                new DeploymentCreateParameters()
                {
                    Label =  String.Concat(serviceName, " Prod ", DateTime.Now.Ticks.ToString()),
                    Name = Guid.NewGuid().ToString(),
                    PackageUri = pkgUri,
                    Configuration = File.ReadAllText(configurationFile),
                    StartDeployment = true
            });
        }
    
        public string AutoScaleCloudService(string serviceName, string roleName)
        {
            AutoscaleClient autoscaleClient = new AutoscaleClient(cloudCredentials);

            AutoscaleSettingCreateOrUpdateParameters autoscaleCreateParams = new AutoscaleSettingCreateOrUpdateParameters()
            {
                Setting = new AutoscaleSetting()
                {
                    Enabled = true,
                    Profiles = new List<AutoscaleProfile>
                    {
                        new AutoscaleProfile 
                        {
                            Capacity = new ScaleCapacity 
                            {
                                Default ="1", 
                                Maximum="10", 
                                Minimum="1"
                            },
                            Name = "sampleProfile",
                            Recurrence= new Recurrence 
                            { 
                                Frequency = RecurrenceFrequency.Week,
                                Schedule = new RecurrentSchedule
                                { 
                                    Days = new List<String>{"Monday", "Thursday", "Friday"},
                                    Hours = {7, 19},
                                    Minutes = new List<int>{0},
                                    TimeZone = "Eastern Standard Time"
                                }
                            },
                            Rules=new List<ScaleRule>
                            {
                                new ScaleRule
                                { 
                                    MetricTrigger = new MetricTrigger
                                    {
                                        MetricName = "PercentageCPU",
                                        MetricNamespace = "", 
                                        MetricSource= AutoscaleMetricSourceBuilder.BuildCloudServiceMetricSource(serviceName, roleName, true),
                                        Operator = ComparisonOperationType.GreaterThanOrEqual,
                                        Threshold = 80,
                                        Statistic = MetricStatisticType.Average,
                                        TimeGrain = TimeSpan.FromMinutes(5),
                                        TimeAggregation = TimeAggregationType.Average,
                                        TimeWindow = TimeSpan.FromMinutes(30)
                                    },
                                    ScaleAction = new ScaleAction 
                                    {
                                        Direction = ScaleDirection.Increase,
                                        Cooldown = TimeSpan.FromMinutes(20),
                                        Type = ScaleType.ChangeCount,
                                        Value = "1"
                                    },
                                },
                                new ScaleRule
                                { 
                                    MetricTrigger = new MetricTrigger
                                    {
                                        MetricName = "PercentageCPU",
                                        MetricNamespace = "", 
                                        MetricSource= AutoscaleMetricSourceBuilder.BuildCloudServiceMetricSource(serviceName, roleName, true),
                                        Operator = ComparisonOperationType.LessThanOrEqual,
                                        Threshold = 60,
                                        Statistic = MetricStatisticType.Average,
                                        TimeGrain = TimeSpan.FromMinutes(5),
                                        TimeAggregation = TimeAggregationType.Average,
                                        TimeWindow = TimeSpan.FromMinutes(30)
                                    },
                                    ScaleAction = new ScaleAction 
                                    {
                                        Direction = ScaleDirection.Decrease,
                                        Cooldown = TimeSpan.FromMinutes(20),
                                        Type = ScaleType.ChangeCount,
                                        Value = "1"
                                    },
                                }
                            }
                        }
                    }
                }
            };


            OperationResponse autoscaleResponse = autoscaleClient.Settings.CreateOrUpdate(
                AutoscaleResourceIdBuilder.BuildCloudServiceResourceId(serviceName, roleName, true),
                autoscaleCreateParams);

            string statusCode = autoscaleResponse.StatusCode.ToString();

            AutoscaleSettingGetResponse settingReponse = autoscaleClient.Settings.Get(AutoscaleResourceIdBuilder.BuildCloudServiceResourceId(serviceName, roleName, true));
            AutoscaleSetting autoscaleSetting = settingReponse.Setting;

            return statusCode;
        } 

        public void Dispose()
        {
            if (storageClient != null) storageClient.Dispose();
            if (computeClient != null) computeClient.Dispose();
        }
    }
}
