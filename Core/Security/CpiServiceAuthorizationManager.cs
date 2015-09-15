using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IdentityModel.Policy;
using System.ServiceModel;
using System.ServiceModel.Description;
using Microsoft.IdentityModel.Claims;
using Microsoft.IdentityModel.Tokens;

namespace CPI.Applications.Core.Security
{
    public class CpiServiceAuthorizationManager : IdentityModelServiceAuthorizationManager
    {
        public override bool CheckAccess(OperationContext operationContext, ref System.ServiceModel.Channels.Message message)
        {
            bool isAuthorized = false;

            ReadOnlyCollection<IAuthorizationPolicy> authorizationPolicies = GetAuthorizationPolicies(operationContext);
            operationContext.IncomingMessageProperties.Security.ServiceSecurityContext = new ServiceSecurityContext(authorizationPolicies ?? new List<IAuthorizationPolicy>().AsReadOnly());

            FederatedServiceCredentials fedCredentials = GetFederatedServiceCredentials();
            string to = operationContext.IncomingMessageHeaders.To.AbsoluteUri;
            string action = operationContext.IncomingMessageHeaders.Action;

            if (fedCredentials == null || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(action))
            {
                throw new Exception("FederatedServiceCredentials are null or there is not a valid action or to header for processing.");
            }

            ClaimsPrincipal claimsPrincipal = GetApplicationClaimsPrincipal(operationContext);

            operationContext.ServiceSecurityContext.AuthorizationContext.Properties["Principal"] = claimsPrincipal;
            isAuthorized = fedCredentials.ClaimsAuthorizationManager.CheckAccess(
                new Microsoft.IdentityModel.Claims.AuthorizationContext(claimsPrincipal, to, action));

            return isAuthorized;
        }

        public ClaimsPrincipal GetApplicationClaimsPrincipal(OperationContext operationContext)
        {
            var claimsPrincipal = operationContext.ServiceSecurityContext.AuthorizationContext.Properties["Principal"] as IClaimsPrincipal;

            if (claimsPrincipal == null)
            {
                throw new Exception("Security principal is not IClaimsPrincipal type.");
            }

            var returnValue = new ClaimsPrincipal(claimsPrincipal.Identities);
            return returnValue;
        }

        public FederatedServiceCredentials GetFederatedServiceCredentials()
        {
            ServiceCredentials credentials = null;

            if (((OperationContext.Current != null) &&
                (OperationContext.Current.Host != null)) &&
                ((OperationContext.Current.Host.Description != null) &&
                (OperationContext.Current.Host.Description.Behaviors != null)))
            {
                credentials = OperationContext.Current.Host.Description.Behaviors.Find<ServiceCredentials>();
            }

            FederatedServiceCredentials fedCredentials = credentials as FederatedServiceCredentials;

            if (fedCredentials == null)
            {
                throw new Exception("Invalid service credentials. WIF not enabled.");
            }
            return fedCredentials;

        }
    }
}
