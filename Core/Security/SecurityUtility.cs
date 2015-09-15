using System.Threading;

namespace CPI.Applications.Core.Security
{
    public static class SecurityUtility
    {
        public static string GetUsername()
        {
            return Thread.CurrentPrincipal.Identity == null ? string.Empty : Thread.CurrentPrincipal.Identity.Name;
        }

     }
}