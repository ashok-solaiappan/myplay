using CPI.Applications.Core.Settings;
using CPI.Core.Environment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CPI.Applications.Core.Tests
{
    [TestClass]
    public class SettingsTest
    {
        [TestMethod]
        public void Initization_SanityCheck()
        {
            EnvironmentInitializer.Initialize(false, "CPI.ApplicationsCore.Tests");

            Assert.IsNotNull(EnvironmentConfigurationStore.Current);
        }
    }
}
