using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using CPI.Core;
using CPI.Core.DataAccess.NHibernate;
using CPI.Core.Environment;
using CPI.Core.Environment.Logger;
using CPI.Core.Environment.Settings;

namespace CPI.Applications.Core.DataAccess.Settings
{
    public class ConfigurePesistenceHelper : IConfigurePersistenceHelper
    {
        #region Properties

        private static readonly ReaderWriterLockSlim _Lock;
        private static readonly Dictionary<string, ConfigurePesistenceHelper> _InstanceHash;
        public static readonly EnvironmentConfiguration StoredEnvironmentConfiguration;
        private static readonly NHibernateConfiguration _StoredNHibernateConfiguration;
        private List<string> _OtherDatabaseKeysToIncludeInMap = new List<string>();

        public EnvironmentConfiguration EnvironmentConfiguration { get; set; }

        public DbMapper DbMapper { get; set; }
        public NHibernateConfiguration NHibernateConfiguration { get; set; }

        private string _DatabaseKey;
        public string DatabaseKey
        {
            get { return DbMapper == null ? _DatabaseKey : DbMapper.DatabaseKey; }
            set { _DatabaseKey = value; }
        }

        #endregion Properties

        #region Constructors

        static ConfigurePesistenceHelper()
        {
            // TODO: Refactor to SRP and log

            var executingAssembly = Assembly.GetExecutingAssembly();
            _InstanceHash = new Dictionary<string, ConfigurePesistenceHelper>();
            _Lock = new ReaderWriterLockSlim();

            _StoredNHibernateConfiguration = NHibernateUtility.GetConfiguration(executingAssembly);
            StoredEnvironmentConfiguration = EnvironmentConfigurationStore.Current;
        }

        #endregion Constructors

        public List<string> OtherDatabaseKeysToIncludeInMap { get { return _OtherDatabaseKeysToIncludeInMap; } set { _OtherDatabaseKeysToIncludeInMap = value; } }

        public string GetLoggingInformation()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0, -60}{1, -70}", "ConfigurePesistenceHelper.DatabaseKey: ", DatabaseKey));
            sb.AppendLine(string.Format("{0, -60}{1, -70}", "ConfigurePesistenceHelper.DbMapper.ControllerKey: ", DbMapper.ControllerKey));
            sb.AppendLine(string.Format("{0, -60}{1, -70}", "ConfigurePesistenceHelper.DbMapper.DatabaseConfigurationId: ", DbMapper.DatabaseConfigurationId));

            if (StoredEnvironmentConfiguration != null)
            {
                sb.AppendLine();
                sb.Append(StoredEnvironmentConfiguration);
            }

            return sb.ToString();
        }

        public static ConfigurePesistenceHelper GetInstance(string databaseKey)
        {
            var dbMapper = (from c in StoredEnvironmentConfiguration.DbMapperCollection.DbMapperList
                            where c.DatabaseKey == databaseKey
                            select c).FirstOrDefault();

            if (dbMapper == null)
            {
                throw new global::CPI.Core.Environment.Exceptions.CpiApplicationException(LogCategories.Common.Configuration.MissingDbMapper,
                    string.Format("A DbMapper named [{0}] was not found.", databaseKey), "Add the Map to the DbMapper.xml file.");
            }

            return GetInstance(dbMapper);
        }

        public static ConfigurePesistenceHelper GetInstance(DbMapper dbMapper)
        {
            ConfigurePesistenceHelper config = null;

            CriticalSection criticalSectionWrite =
                errorMessage =>
                    {
                        if (!_InstanceHash.TryGetValue(dbMapper.DatabaseKey, out config))
                        {
                            config = new ConfigurePesistenceHelper
                                {
                                    EnvironmentConfiguration = StoredEnvironmentConfiguration,
                                    NHibernateConfiguration = _StoredNHibernateConfiguration,
                                    DbMapper = dbMapper,
                                };
                            _InstanceHash.Add(dbMapper.DatabaseKey, config);
                        }
                        return CricticalSectionResultEnum.Success;
                    };

            CriticalSection criticalSectionRead =
                errorMessage =>
                    {
                        _InstanceHash.TryGetValue(dbMapper.DatabaseKey, out config);
                        return CricticalSectionResultEnum.Success;
                    };

            LockTimeOut threadProblem;

            CriticalSectionManager.ExecuteReadCriticalSection(_Lock, 1000, criticalSectionRead, "ConfigurePersistanceHelper", out threadProblem);
            if (config != null) return config;

            CriticalSectionManager.ExecuteWriteCriticalSection(_Lock, 1000, criticalSectionWrite, "ConfigurePersistanceHelper", out threadProblem);
            return config;
        }

    }
}