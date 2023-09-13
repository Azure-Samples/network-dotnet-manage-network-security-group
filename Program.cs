// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using System.Xml.Linq;


namespace ManageNetworkSecurityGroup
{

    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Network sample for managing network security groups -
         *  - Create a network security group for the front end of a subnet
         *  - Create a network security group for the back end of a subnet
         *  - Create Linux virtual machines for the front end and back end
         *  -- Apply network security groups
         *  - List network security groups
         *  - Update a network security group.
         */
        public static async Task RunSample(ArmClient client)
        {
            string frontEndNSGName = Utilities.CreateRandomName("fensg");
            string backEndNSGName = Utilities.CreateRandomName("bensg");
            string vnetName = Utilities.CreateRandomName("vnet");
            string nicName1 = Utilities.CreateRandomName("nic1");
            string nicName2 = Utilities.CreateRandomName("nic2");
            string publicIPAddressLeafDNS1 = Utilities.CreateRandomName("pip1");
            string frontEndVmName = Utilities.CreateRandomName("fevm");
            string backEndVmName = Utilities.CreateRandomName("bevm");

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                string rgName = Utilities.CreateRandomName("NetworkSampleRG");
                Utilities.Log($"Creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                // Define a virtual network for VMs in this availability set

                Utilities.Log("Creating a virtual network ...");
                VirtualNetworkData vnetInput = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "172.16.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { Name = "Front-end", AddressPrefix = "172.16.1.0/24" },
                        new SubnetData() { Name = "Back-end", AddressPrefix = "172.16.2.0/24" },
                    },
                };
                var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
                VirtualNetworkResource vnet = vnetLro.Value;
                Utilities.Log($"Created a virtual network: {vnet.Data.Name}");

                //============================================================
                // Create a network security group for the front end of a subnet
                // front end subnet contains two rules
                // - ALLOW-SSH - allows SSH traffic into the front end subnet
                // - ALLOW-WEB- allows HTTP traffic into the front end subnet

                Utilities.Log("Creating a security group for the front end - allows SSH and HTTP");
                NetworkSecurityGroupData frontEndNsgInput = new NetworkSecurityGroupData()
                {
                    Location = resourceGroup.Data.Location,
                    SecurityRules =
                    {
                        new SecurityRuleData()
                        {
                            Name = "ALLOW-SSH",
                            Description = "Allow SSH",
                            Access = SecurityRuleAccess.Allow,
                            Direction = SecurityRuleDirection.Inbound,
                            SourceAddressPrefix = "*",
                            SourcePortRange = "*",
                            DestinationAddressPrefix = "*",
                            DestinationPortRange = "22",
                            Priority = 100,
                            Protocol = SecurityRuleProtocol.Tcp,
                        },
                        new SecurityRuleData()
                        {
                            Name = "ALLOW-HTTP",
                            Description = "Allow HTTP",
                            Access = SecurityRuleAccess.Allow,
                            Direction = SecurityRuleDirection.Inbound,
                            SourceAddressPrefix = "*",
                            SourcePortRange = "*",
                            DestinationAddressPrefix = "*",
                            DestinationPortRange = "80",
                            Priority = 101,
                            Protocol = SecurityRuleProtocol.Tcp,
                        }
                    }
                };
                var frontEndNsgLro = await resourceGroup.GetNetworkSecurityGroups().CreateOrUpdateAsync(WaitUntil.Completed, frontEndNSGName, frontEndNsgInput);
                NetworkSecurityGroupResource frontEndNsg = frontEndNsgLro.Value;
                Utilities.Log("Created a security group for the front end: " + frontEndNsg.Data.Name);
                Utilities.PrintNetworkSecurityGroup(frontEndNsg);

                //============================================================
                // Create a network security group for the back end of a subnet
                // back end subnet contains two rules
                // - ALLOW-SQL - allows SQL traffic only from the front end subnet
                // - DENY-WEB - denies all outbound internet traffic from the back end subnet

                Utilities.Log("Creating a security group for the front end - allows SSH and "
                        + "denies all outbound internet traffic  ");

                NetworkSecurityGroupData backEndNsgInput = new NetworkSecurityGroupData()
                {
                    Location = resourceGroup.Data.Location,
                    SecurityRules =
                    {
                        new SecurityRuleData()
                        {
                            Name = "ALLOW-SQL",
                            Description = "Allow SQL",
                            Access = SecurityRuleAccess.Allow,
                            Direction = SecurityRuleDirection.Inbound,
                            SourceAddressPrefix = "172.16.1.0/24",
                            SourcePortRange = "*",
                            DestinationAddressPrefix = "*",
                            DestinationPortRange = "1433",
                            Priority = 100,
                            Protocol = SecurityRuleProtocol.Tcp,
                        },
                        new SecurityRuleData()
                        {
                            Name = "DENY-WEB",
                            Description = "Deny Web",
                            Access = SecurityRuleAccess.Deny,
                            Direction = SecurityRuleDirection.Outbound,
                            SourceAddressPrefix = "*",
                            SourcePortRange = "*",
                            DestinationAddressPrefix = "*",
                            DestinationPortRange = "*",
                            Priority = 200,
                            Protocol = SecurityRuleProtocol.Asterisk,
                        }
                    }
                };
                var backEndNsgLro = await resourceGroup.GetNetworkSecurityGroups().CreateOrUpdateAsync(WaitUntil.Completed, backEndNSGName, backEndNsgInput);
                NetworkSecurityGroupResource backEndNsg = backEndNsgLro.Value;
                Utilities.Log("Created a security group for the back end: " + backEndNsg.Data.Name);
                Utilities.PrintNetworkSecurityGroup(backEndNsg);

                //========================================================
                // Create a network interface and apply the
                // front end network security group

                Utilities.Log("Creating multiple network interfaces");
                Utilities.Log("Creating a network interface for the front end");

                // To create a NIC, an existing IP address is required.
                Utilities.Log("Created two public ip...");
                PublicIPAddressResource pip1 = await Utilities.CreatePublicIP(resourceGroup, publicIPAddressLeafDNS1);
                Utilities.Log($"Created public ip: {pip1.Data.Name}");

                var nicInput1 = new NetworkInterfaceData()
                {
                    Location = resourceGroup.Data.Location,
                    EnableIPForwarding = true,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "default-config",
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets.First(item => item.Name == "Front-end").Id
                            },
                            PublicIPAddress = new PublicIPAddressData
                            {
                                Id = pip1.Id,
                            }
                        }
                    },
                    NetworkSecurityGroup = new NetworkSecurityGroupData()
                    {
                        Id = frontEndNsg.Data.Id
                    }
                };
                var networkInterfaceLro1 = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, nicName1, nicInput1);
                NetworkInterfaceResource nic1 = networkInterfaceLro1.Value;
                Utilities.Log($"Created network interface for the front end: {nic1.Data.Name}");

                //========================================================
                // Create a network interface and apply the
                // back end network security group

                Utilities.Log("Creating a network interface for the back end");

                var nicInput2 = new NetworkInterfaceData()
                {
                    Location = resourceGroup.Data.Location,
                    IPConfigurations =
                    {
                        new NetworkInterfaceIPConfigurationData()
                        {
                            Name = "default-config",
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets.First(item => item.Name == "Back-end").Id
                            }
                        }
                    },
                    NetworkSecurityGroup = new NetworkSecurityGroupData()
                    {
                        Id = backEndNsg.Data.Id
                    }
                };
                var networkInterfaceLro2 = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, nicName2, nicInput2);
                NetworkInterfaceResource nic2 = networkInterfaceLro2.Value;
                Utilities.Log($"Created network interface 2: {nic2.Data.Name}");

                //=============================================================
                // Create a virtual machine (for the front end)
                // with the network interface that has the network security group for the front end

                Utilities.Log("Creating a virtual machine (for the front end) - "
                        + "with the network interface that has the network security group for the front end");

                VirtualMachineData frontEndVmInput = Utilities.GetDefaultVMInputData(resourceGroup, frontEndVmName);
                frontEndVmInput.NetworkProfile.NetworkInterfaces.Add(
                    new VirtualMachineNetworkInterfaceReference()
                    {
                        Id = nic1.Id,
                        Primary = true
                    });
                var frontEndVmLro = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, frontEndVmName, frontEndVmInput);
                VirtualMachineResource frontendfrontEndVm = frontEndVmLro.Value;
                Utilities.Log("Created frontEndVm: " + frontendfrontEndVm.Data.Name);

                //=============================================================
                // Create a virtual machine (for the back end)
                // with the network interface that has the network security group for the back end

                Utilities.Log("Creating a Linux virtual machine (for the back end) - "
                        + "with the network interface that has the network security group for the back end");

                VirtualMachineData backEndVmInput = Utilities.GetDefaultVMInputData(resourceGroup, backEndVmName);
                backEndVmInput.NetworkProfile.NetworkInterfaces.Add(
                    new VirtualMachineNetworkInterfaceReference()
                    {
                        Id = nic2.Id,
                        Primary = true
                    });
                var backEndVmLro = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, backEndVmName, backEndVmInput);
                VirtualMachineResource frontendbackEndVm = backEndVmLro.Value;
                Utilities.Log("Created backEndVm: " + frontendbackEndVm.Data.Name);

                //========================================================
                // List network security groups

                Utilities.Log("Walking through network security groups");

                await foreach (var networkSecurityGroup in resourceGroup.GetNetworkSecurityGroups().GetAllAsync())
                {
                    Utilities.Log(networkSecurityGroup.Data.Name);
                }

                //========================================================
                // Update a network security group

                Utilities.Log("Updating the front end network security group to allow FTP");

                frontEndNsgInput = frontEndNsg.Data;
                frontEndNsgInput.SecurityRules.Add(
                    new SecurityRuleData()
                    {
                        Name = "ALLOW-FTP",
                        Description = "Allow FTP",
                        Access = SecurityRuleAccess.Allow,
                        Direction = SecurityRuleDirection.Inbound,
                        SourceAddressPrefix = "*",
                        SourcePortRange = "*",
                        DestinationAddressPrefix = "*",
                        DestinationPortRange = "20,21",
                        Priority = 200,
                        Protocol = SecurityRuleProtocol.Tcp,
                    });
                frontEndNsgLro = await resourceGroup.GetNetworkSecurityGroups().CreateOrUpdateAsync(WaitUntil.Completed, frontEndNSGName, frontEndNsgInput);
                frontEndNsg = frontEndNsgLro.Value;
                Utilities.Log("Updated the front end network security group");
                Utilities.PrintNetworkSecurityGroup(frontEndNsg);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group...");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId.Name}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}