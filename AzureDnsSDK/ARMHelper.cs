using System;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Linq;
using System.Threading.Tasks;

namespace ARMHelper
{
    class JWTHelper
    {
        /*
         *   Get the TenantID for a given Azure subscription
         */
        public static string GetSubscriptionTenantId(string subscriptionId)
        {
            //  URL to Azure Resource Manager API
            string url = string.Format(format: "https://management.azure.com/subscriptions/{0}?api-version=2014-01-01", arg0: subscriptionId);

            // Attempting an ARM request without JWT will return an auth discovery header
            AuthenticationHeaderValue wwwAuthenticateHeader = null;
            using (HttpClient httpClient = new HttpClient())
            {
                HttpResponseMessage response = httpClient.GetAsync(url).Result;
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException("Failure requesting subscription tenant id from ARM.");
                }

                wwwAuthenticateHeader = response.Headers.WwwAuthenticate.Single();
            }

            //  chop up the response
            string[] tokens = wwwAuthenticateHeader.Parameter.Split('=', ',');
            string authUrl = tokens.ElementAt(1).Trim('"');
            string tenantId = new Uri(authUrl).AbsolutePath.Trim('/');
            return tenantId;
        }

        /*
         *   Get an auth token for a given tenant ID (from GetSubscriptionTenantId)
         *   userId specifies the expected user, if no userId is specified alwaysPrompt=false will use current user if already logged in
         */
        public static string GetAuthToken(string tenantId, bool alwaysPrompt = false, string userId = null)
        {
            AuthenticationResult authResult = null;
            try
            {
                authResult = GetAuthResult(tenantId, alwaysPrompt, userId);    
            }
            catch (Exception e)
            {
                if (e.Message.ToLower().Contains("returned by service does not match user"))
                {
                    Console.WriteLine("Got username exception, trying that again without username...");
                    // if it complained about username provided, try without it
                    authResult = GetAuthResult(tenantId, true, null);    
                }
                else
                {
                    throw e;
                }
            }

            if (authResult == null)
                return null;
            else
                return authResult.CreateAuthorizationHeader().Substring("Bearer ".Length);
        }

        /*
         *   Try using AcquireToken to get auth result
         */
        private static AuthenticationResult GetAuthResult(string tenantId, bool alwaysPrompt, string userId)
        {
            AuthenticationContext context = new AuthenticationContext(authority: string.Format("https://login.windows.net/{0}", tenantId));

            Task<AuthenticationResult> acquireTokenTask = null;
            if (!string.IsNullOrEmpty(userId))
            {
                acquireTokenTask = context.AcquireTokenAsync(
                    resource: "https://management.core.windows.net/",
                    clientId: "1950a258-227b-4e31-a9cf-717495945fc2",
                    redirectUri: new Uri("urn:ietf:wg:oauth:2.0:oob"),
                    parameters: new PlatformParameters(promptBehavior: alwaysPrompt ? PromptBehavior.Always : PromptBehavior.Auto, ownerWindow: null),
                    userId: new UserIdentifier(userId, UserIdentifierType.RequiredDisplayableId));
            }
            else
            {
                acquireTokenTask = context.AcquireTokenAsync(
                    resource: "https://management.core.windows.net/",
                    clientId: "1950a258-227b-4e31-a9cf-717495945fc2",
                    redirectUri: new Uri("urn:ietf:wg:oauth:2.0:oob"),
                    parameters: new PlatformParameters(promptBehavior: alwaysPrompt ? PromptBehavior.Always : PromptBehavior.Auto, ownerWindow: null));
            }

            try
            {
                acquireTokenTask.Wait();
                return acquireTokenTask.Result;
            }
            catch (Exception e)
            {
                throw e.InnerException;
            }
        }
    }
}
