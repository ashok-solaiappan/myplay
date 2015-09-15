using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using CPI.Core;
using CPI.Core.Services;
using CPI.Core.Services.Hosting;
using CPI.Applications.Core.Settings;

namespace CPI.ServiceHostFactories
{
    public class NonHttpServiceHostFactory : System.ServiceModel.Activation.ServiceHostFactory
    {
        private static ILogService _LoggingServiceHandler;

        public override ServiceHostBase CreateServiceHost(string constructorString, Uri[] baseAddresses)
        {
            EnsureInitialization(constructorString);

            var serviceEndPoint = EnvironmentConfigurationStore.Current.GetServiceEndpoint(constructorString);
            var serviceType = serviceEndPoint.ServiceType;

            var serviceHost = new CpiServiceHost(serviceType, serviceEndPoint, baseAddresses);

            serviceHost.SetMaxItemsInObjectGraphToMaximum();

            return serviceHost;
        }

        protected override ServiceHost CreateServiceHost(Type serviceType, Uri[] baseAddresses)
        {
            throw new NotImplementedException();
        }

        protected void EnsureInitialization(string constructorString)
        {
            var directoryInfo = new DirectoryInfo(System.Web.HttpRuntime.BinDirectory);
            var applicationName = directoryInfo.Parent == null ? directoryInfo.Name : directoryInfo.Parent.Name;

            EnvironmentInitializer.Initialize(isHttpContext: false, applicationName: applicationName, listener: logListener);
            IocManager.LogManager.Verbose(CPI.Core.Environment.Logger.LogCategories.Common.Services.Startup, "NonHttpServiceHostFactory Complete");
        }

        private static void logListener(string logEntry)
        {
            if (_LoggingServiceHandler == null) _LoggingServiceHandler = IocManager.Resolve<ILogService>();
            var persistLogEntryRequest = IocManager.Resolve<WriteToLogRequest>();
            persistLogEntryRequest.LogEntry = logEntry;
            _LoggingServiceHandler.WriteToLog(persistLogEntryRequest);
        }

    }
}
