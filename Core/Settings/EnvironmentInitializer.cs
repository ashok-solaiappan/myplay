using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CPI.Applications.Core.Security;
using CPI.Core;
using CPI.Core.Environment;
using CPI.Core.Environment.Logger;
using CPI.Core.Environment.Settings;

namespace CPI.Applications.Core.Settings
{
    public static class EnvironmentInitializer
    {
        #region Constants

        const string ENV_VAR_TOKENS = "CPI.ConfigurationTokens.FileName";
        const string ENV_VAR_ENVIRONMENT = "CPI.Environment.Name";
        const string ENV_VAR_SETTINGS_PATH = "CPI.Settings.Path";
        const string BASE_CONFIGURATION_FILE_NAME = "BaseConfiguration.xml";
        const string STANDARD_CONFIGURATION_FILE_NAME = "StandardConfiguration.xml";

        #endregion Constants

        public static bool IsInitialized { get; private set; }
        private static readonly ReaderWriterLockSlim _Lock = new ReaderWriterLockSlim();

        public static void Reinitialize(bool isHttpContext = true)
        {
            IsInitialized = false;
            Initialize();
        }

        public static void ReInitialize(bool isHttpContext = true, string applicationName = null, Action<string> listener = null)
        {
            IsInitialized = false;
            Initialize(isHttpContext, applicationName, listener);
        }

        public static void Initialize(
            bool isHttpContext = true, 
            string applicationName = null, 
            Action<string> listener = null, 
            List<string> additionalConfigurationStringList = null)
        {
            CriticalSection criticalSection =
                errorMessage =>
                {
                    if (IsInitialized && EnvironmentConfigurationStore.Current == null)
                    {
                        LogManager.Logger.Warning(LogCategories.Environment.Configuration,
                                                      "Something is funky with EnvironmentInitializer, IsInitialized is true but EnvironmentConfigurationStore.Current == null)");
                        IsInitialized = false; 
                    }

                    if (IsInitialized)
                    {
                        LogManager.Logger.Configuration(LogCategories.Environment.Configuration,
                                                            "Initialization has already taken place and as such, not needed.");
                        return CricticalSectionResultEnum.Success;
                    }

                    initialize(applicationName, listener, additionalConfigurationStringList);
                    return CricticalSectionResultEnum.Success;
                };

            LockTimeOut threadProblem;
            var criticalSectionResult = CriticalSectionManager.ExecuteWriteCriticalSection(_Lock, 120 * 1000, criticalSection, "EnvironmentInitializer.Initialize", out threadProblem);
            manageLogging(criticalSectionResult, threadProblem);
        }

        private static void manageLogging(CricticalSectionResultEnum criticalSectionResult, LockTimeOut threadProblem)
        {
            if (threadProblem != null)
            {
                throw new CPI.Core.Environment.Exceptions.CpiApplicationException(LogCategories.Common.Configuration.General,
                                                  string.Format("CPI.Applications.Core.Settings.EnvironmentInitializer.Initialize could not attain a lock: {0}", threadProblem));
            }

            if (criticalSectionResult != CricticalSectionResultEnum.Success)
            {
                throw new CPI.Core.Environment.Exceptions.CpiApplicationException(LogCategories.Common.Configuration.General,
                                                  string.Format("CPI.Applications.Core.Settings.EnvironmentInitializer.Initialize had problems: {0}", criticalSectionResult));
            }

            LogManager.Logger.Information(LogCategories.Common.Configuration.General, "CPI.Applications.Core.Settings.EnvironmentInitializer.Initialize: Method Completed");
        }

        private static void ensureConfigurationAssembliesAreLoaded()
        {
            var configurationFileNameList = new[] { "Configuration", "Contracts", "Service" };

            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var binPath = new FileInfo(assemblyLocation).DirectoryName;
            var assemblyList = AppDomain.CurrentDomain.GetAssemblies().ToList();

            Debug.Assert(binPath != null);

            foreach (var fileName in Directory.GetFiles(binPath, "*.dll", SearchOption.AllDirectories))
            {
                if (fileName.ToLowerInvariant().Contains("\\obj\\")) continue;

                if (!configurationFileNameList.Any(x => fileName.Contains(x))) continue;

                var probableConfigurationAssembly = Assembly.ReflectionOnlyLoadFrom(fileName);
                if (assemblyList.Any(x => x.FullName == probableConfigurationAssembly.FullName)) continue;

                LogManager.Logger.Verbose(LogCategories.Environment.Configuration, string.Format("Loading Assembly in file {0}", fileName));
                var assembly = Assembly.LoadFile(fileName);
                assemblyList.Add(assembly);
            }
        }

        private static void initialize(string applicationName = null, Action<string> listener = null, List<string> additionalConfigurationStringList = null)
        {
            if (additionalConfigurationStringList == null) additionalConfigurationStringList = new List<string>();

            try
            {
                initializeLogger(applicationName, listener);
                ensureConfigurationAssembliesAreLoaded();
                loadSettings(additionalConfigurationStringList);
                assignApplicationName();
                assignEnvironmentName();
                dumpConfigToLog();
                initializeIoc();
                configureLogger();

                IsInitialized = true;

            }
            catch (Exception ex)
            {
                LogManager.Logger.Error(ex, LogCategories.Environment.Configuration, "Problem Initializing", string.Empty);
                throw;
            }
        }

        private static void assignEnvironmentName()
        {
            EnvironmentConfigurationStore.Current.EnvironmentName = EnvironmentUtility.GetValueFromEnvironmentVariable(ENV_VAR_ENVIRONMENT);

        }

        private static void configureLogger()
        {
            var logConfiguration = EnvironmentConfigurationStore.Current.GetAuditLogConfiguration("__GeneralPurposeLog");
            LogManager.Logger.ConfigureLogger(logConfiguration);
        }

        private static void initializeIoc()
        {
            IocManager.Initialize();
        }

        private static void dumpConfigToLog()
        {
            var configDump = EnvironmentUtility.DumpEnvironmentConfiguration(EnvironmentConfigurationStore.Current);
            LogManager.Logger.Configuration(LogCategories.Common.Configuration.Environment, "EnvironmentConfiguration Dump",
                                            string.Empty, configDump);
        }

        private static void assignApplicationName()
        {
            EnvironmentConfigurationStore.Current.ApplicationName = LogManager.Logger.ApplicationName;
        }

        private static void loadSettings(List<string> additionalConfigurationStringList)
        {
            if (tryLoadSettingsFromLocalConfigurationFolder())
            {
                return;
            }

            loadSettingsFromEnvironmentVariable(additionalConfigurationStringList);
        }

        private static bool tryLoadSettingsFromLocalConfigurationFolder()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var localFolder = new FileInfo(assemblyPath).Directory;
            Debug.Assert(localFolder != null);
            var localFolderFullName = localFolder.FullName;
            var localConfigPath = Path.Combine(localFolderFullName, "Settings"); 

            if (!Directory.Exists(localConfigPath))
            {
                return false;
            }

            var envVarTokensValue = EnvironmentUtility.GetValueFromEnvironmentVariable(ENV_VAR_TOKENS);
            var envVarEnvironmentValue = EnvironmentUtility.GetValueFromEnvironmentVariable(ENV_VAR_ENVIRONMENT);

            var baseConfigurationPath = Path.Combine(localConfigPath, BASE_CONFIGURATION_FILE_NAME);
            var standardConfigurationPath = Path.Combine(localConfigPath, envVarEnvironmentValue, STANDARD_CONFIGURATION_FILE_NAME);
            var tokensPath = Path.Combine(localConfigPath, envVarEnvironmentValue, envVarTokensValue);
            CPI.Core.Utilities.Utility.AddXmlExtensionIfNeeded(ref tokensPath);

            if (!File.Exists(baseConfigurationPath) ||
                !File.Exists(tokensPath))
            {
                return false;
            }

            var loaderProperties = new Loader.LoaderProperties
            {
                TokensPath = tokensPath,
                BaseConfigurationPath = baseConfigurationPath,
                StandardConfigurationPath = standardConfigurationPath,
                AssembliesToInspectForConfiguration = new[] { "Configuration", "Service", "Contracts" },
                IsProduction = envVarEnvironmentValue.Contains("Production"),
            };

            EnvironmentConfigurationStore.Current = Loader.GetEnvironmentConfiguration(loaderProperties);
            EnvironmentConfigurationStore.Current.EnvironmentName = envVarEnvironmentValue;

            return true;
        }

        private static void loadSettingsFromEnvironmentVariable(List<string> additionalConfigurationStringList)
        {

            var envVarTokensValue = EnvironmentUtility.GetValueFromEnvironmentVariable(ENV_VAR_TOKENS);
            var envVarEnvironmentValue = EnvironmentUtility.GetValueFromEnvironmentVariable(ENV_VAR_ENVIRONMENT);
            var envVarSettingsPathValue = EnvironmentUtility.GetValueFromEnvironmentVariable(ENV_VAR_SETTINGS_PATH);

            var tokensPath = Path.Combine(envVarSettingsPathValue, envVarEnvironmentValue, envVarTokensValue);
            CPI.Core.Utilities.Utility.AddXmlExtensionIfNeeded(ref tokensPath);

            var loaderProperties = new Loader.LoaderProperties
                {
                    TokensPath = tokensPath,
                    BaseConfigurationPath = Path.Combine(envVarSettingsPathValue, BASE_CONFIGURATION_FILE_NAME),
                    StandardConfigurationPath =
                        Path.Combine(envVarSettingsPathValue, envVarEnvironmentValue, STANDARD_CONFIGURATION_FILE_NAME),
                    AssembliesToInspectForConfiguration = new[] {"Configuration", "Service", "Contracts"},
                    IsProduction = envVarEnvironmentValue.Contains("Production"),
                    AdditionalConfigurationStringList = additionalConfigurationStringList,
                };

            EnvironmentConfigurationStore.Current = Loader.GetEnvironmentConfiguration(loaderProperties);
        }

        private static void initializeLogger(string applicationName, Action<string> listener)
        {
            LogManager.Logger = new Logger();

            if (listener != null) LogManager.Logger.AddListener(listener);
            LogManager.Logger.GetUsername = SecurityUtility.GetUsername;
            LogManager.Logger.ApplicationName = applicationName ?? Assembly.GetCallingAssembly().GetName().Name;
            LogManager.Logger.Initialize();
        }
    }
}