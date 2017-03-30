using System;
using Microsoft.Azure;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using Microsoft.Azure.Management;
using Microsoft.Azure.Management.Resources;
using Microsoft.Azure.Management.Resources.Models;
using System.Collections.Generic;
using ARMHelper;

namespace DnsSDKtest
{
    class Program
    {
        /*
         *   MAIN
         */
        static void Main(string[] args)
        {
            try
            {
                /*
                 *  validate args
                 */
                if (args.Length != 3)
                {
                    Console.WriteLine("Must provide the subscription ID, resource group and  zone name on the command line:");
                    Console.WriteLine(string.Format("e.g. {0} a11765aa-da85-55df-322c-f43434afcdb2 myRG mycontoso.com", System.AppDomain.CurrentDomain.FriendlyName));
                    PauseBeforeExit();
                    return;
                }
                string subID = args[0];
                string rgName = args[1];
                string zoneName = args[2];
                

                /*
                 *   Authorization
                 */

                //  get the JWT for the subscription, will be prompted for credentials
                Console.WriteLine(string.Format("Logging into subscription {0}...",  subID));
                string jwt = JWTHelper.GetAuthToken(tenantId: JWTHelper.GetSubscriptionTenantId(subID), alwaysPrompt: true);

                //  make the credentials for your subscription ID
                TokenCloudCredentials tcCreds = new TokenCloudCredentials(subID, jwt);


                /*
                 *   Make sure we have a resource group as all ARM resources are in a resouce group
                 */

                //  get the resource management client
                ResourceManagementClient resourceClient = new ResourceManagementClient(tcCreds);

                //  check if the resource group already exists
                ResourceGroupExistsResult rgExists = resourceClient.ResourceGroups.CheckExistence(rgName);
                if (rgExists.Exists)
                {
                    Console.WriteLine(string.Format("ResourceGroup {0} already exists, but that's ok we'll reuse it...", rgName));
                }
                else
                {
                    Console.WriteLine(string.Format("Creating resouce group {0}...", rgName));
                    resourceClient.ResourceGroups.CreateOrUpdate(rgName, new ResourceGroup("northeurope"));
                }
                

                /*
                 *  Create a zone and some record sets
                 *  for Private Preview, zone name must be globally unique so it may already exist!
                 */

                //  get the DNS management client
                DnsManagementClient dnsClient = new DnsManagementClient(tcCreds);

                // check we're registered for Microsoft.Network namespace
                if (!IsProviderRegistered(resourceClient.Providers.List(null).Providers, "Microsoft.Network"))
                {
                    Console.WriteLine("Registering with Microsoft.Network namespace...");
                    resourceClient.Providers.Register("Microsoft.Network");
                }
                else
                {
                    Console.WriteLine("Already registered with Microsoft.Network namespace.");
                }

                //  create a DNS zone
                Console.WriteLine(string.Format("Creating zone and records for {0}...", zoneName));
                Zone z = new Zone("global");
                z.Properties = new ZoneProperties();
                z.Tags.Add("dept", "shopping");
                z.Tags.Add("env", "production");
                ZoneCreateOrUpdateResponse responseCreateZone = dnsClient.Zones.CreateOrUpdate(rgName, zoneName, new ZoneCreateOrUpdateParameters(z));

                // make some records (dnsClient.RecordSets will become dnsClient.RecordSetsets in future)
                RecordSet rsWwwA = new RecordSet("global");
                rsWwwA.Properties = new RecordSetProperties(3600);
                rsWwwA.Properties.ARecords = new List<ARecord>();
                rsWwwA.Properties.ARecords.Add(new ARecord("1.2.3.4"));
                rsWwwA.Properties.ARecords.Add(new ARecord("1.2.3.5"));
                RecordSetCreateOrUpdateResponse responseCreateA = dnsClient.RecordSets.CreateOrUpdate(rgName, zoneName, "www", RecordType.A, new RecordSetCreateOrUpdateParameters(rsWwwA));

                RecordSet rsWwwAaaa = new RecordSet("global");
                rsWwwAaaa.Properties = new RecordSetProperties(3600);
                rsWwwAaaa.Properties.AaaaRecords = new List<AaaaRecord>();
                rsWwwAaaa.Properties.AaaaRecords.Add(new AaaaRecord("1:1:1:1::1"));
                rsWwwAaaa.Properties.AaaaRecords.Add(new AaaaRecord("1:1:1:1::2"));
                RecordSetCreateOrUpdateResponse responseCreateAAAA = dnsClient.RecordSets.CreateOrUpdate(rgName, zoneName, "www", RecordType.AAAA, new RecordSetCreateOrUpdateParameters(rsWwwAaaa));

                // list the zones & record sets in the resource group
                ZoneListResponse zoneListResponse = dnsClient.Zones.List(rgName, new ZoneListParameters());
                foreach (Zone zone in zoneListResponse.Zones)
                {
                    RecordSetListResponse recordSets = dnsClient.RecordSets.ListAll(rgName, zone.Name, new RecordSetListParameters());
                    WriteRecordSetsToConsole(zone.Name, recordSets.RecordSets);
                }


                /*
                 *  ETAGs - set to a value to check record hasn't changed, set to * to make sure it exists
                 *  
                 *  Also in RecordSetCreateOrUpdateParameters: 
                 *      IfNoneMatch = *, only succesed if resource does not exist
                 */

                //  get the RecordSet for {Name=www, Type=A}
                RecordSetGetResponse getWwwA = dnsClient.RecordSets.Get(rgName, zoneName, "www", RecordType.A);
                string previousETag = getWwwA.RecordSet.ETag;
                Console.WriteLine(string.Format("ETag for www.{0} is {1}", zoneName, previousETag));

                //  make a new record set, setting the ETag
                RecordSet newWwwA = new RecordSet("global");
                newWwwA.Properties = new RecordSetProperties(3600);
                newWwwA.Properties.ARecords = new List<ARecord>();
                newWwwA.Properties.ARecords.Add(new ARecord("4.3.2.1"));
                newWwwA.Properties.ARecords.Add(new ARecord("5.3.2.1"));
                newWwwA.ETag = previousETag;

                // do two creates, second one will fail
                try
                {
                    Console.WriteLine("Doing first update - should succeed");
                    RecordSetCreateOrUpdateResponse responseETagUpdate1 = dnsClient.RecordSets.CreateOrUpdate(rgName, zoneName, "www", RecordType.A, new RecordSetCreateOrUpdateParameters(newWwwA));
                    Console.WriteLine(string.Format("Update set Etag to {0}", responseETagUpdate1.RecordSet.ETag));

                    Console.WriteLine("Doing second update - should fail because ETag changed!");
                    RecordSetCreateOrUpdateResponse responseETagUpdate2 = dnsClient.RecordSets.CreateOrUpdate(rgName, zoneName, "www", RecordType.A, new RecordSetCreateOrUpdateParameters(newWwwA));
                    Console.WriteLine(string.Format("Update set Etag to {0}", responseETagUpdate2.RecordSet.ETag));
                }
                catch (Hyak.Common.CloudException e)
                {
                    //  check if the precondition failed
                    if (e.Response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                    {
                        Console.WriteLine("The ETag precondition failed");
                    }
                    else
                    {
                        throw e;
                    }
                }

                // show records now
                WriteRecordSetsToConsole(zoneName, dnsClient.RecordSets.ListAll(rgName, zoneName, new RecordSetListParameters()).RecordSets);


                /*
                 *  End
                 */

                //  get one of the NS records
                RecordSetGetResponse getNS = dnsClient.RecordSets.Get(rgName, zoneName, "@", RecordType.NS);
                string firstNS = getNS.RecordSet.Properties.NsRecords[0].Nsdname;

                //  show how to resolve record
                string url = string.Format("http://www.digwebinterface.com/?hostnames=www.{0}&type=&ns=self&nameservers={1}", zoneName, firstNS);
                Console.WriteLine(string.Format("To see the record resolve, goto: {0}", url));
                
                //  done
                PauseBeforeExit();

                // if we dare to delete the resource group :)
                // resourceClient.ResourceGroups.DeleteAsync(rgName);
            }
            catch (Exception e)
            {
                Console.WriteLine(string.Format("Exception Caught: {0}", e.Message));
                PauseBeforeExit();
            }
        }

        /*
         *   WriteRecordSetsToConsole
         */
        private static void WriteRecordSetsToConsole(string ZoneName, IList<RecordSet> recordSets)
        {
            Console.WriteLine();
            Console.WriteLine(string.Format("Records in {0}:", ZoneName));
            foreach (RecordSet rset in recordSets)
            {
                Console.WriteLine(string.Format("  RecordSet: {0} ({1})", rset.Name, rset.Type));
                if (rset.Properties.ARecords != null)
                {
                    Console.Write("    A records: ");
                    foreach (ARecord a in rset.Properties.ARecords)
                        Console.Write(string.Format("{0}   ", a.Ipv4Address));
                    Console.WriteLine();
                }
                if (rset.Properties.AaaaRecords != null)
                {
                    Console.Write("    AAAA records: ");
                    foreach (AaaaRecord aaaa in rset.Properties.AaaaRecords)
                        Console.Write(string.Format("{0}   ", aaaa.Ipv6Address));
                    Console.WriteLine();
                }
                if (rset.Properties.NsRecords != null)
                {
                    Console.Write("    NS records: ");
                    foreach (NsRecord ns in rset.Properties.NsRecords)
                        Console.Write(string.Format("{0}   ", ns.Nsdname));
                    Console.WriteLine();
                }
                if (rset.Properties.SoaRecord != null)
                    Console.WriteLine(string.Format("    SOA host: {0}", rset.Properties.SoaRecord.Host));
            }
            Console.WriteLine();
        }

        /*
         * 
         */
        static void PauseBeforeExit()
        {
            #if DEBUG
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(false);
                }

                Console.WriteLine();
                Console.WriteLine("Press key to exit...");
                Console.Read();
            #endif
        }

        /*
         * 
         */
        static bool IsProviderRegistered(IEnumerable<Provider> providers, string Namespace)
        {
            foreach (Provider p in providers)
            {
                if (p.Namespace.ToLower().Equals(Namespace.ToLower()))
                {
                    return p.RegistrationState.ToLower().Equals("registered");
                }
            }
            return false;
        }
    }
}
