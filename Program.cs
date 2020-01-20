using System;
using System.Runtime.InteropServices;
using  System.ServiceProcess;
using log4net;
using log4net.Core;

namespace FileWatcher
{
    public class Watcher
    {
        private static SystemFileMonitor _fileWatcher;
        private static ILog _logger = LogManager.GetLogger("SystemFileMonitor");


        public static void Main()
        {
            log4net.Config.XmlConfigurator.Configure();

            if (Environment.UserInteractive)
            {
                Start();

                _logger.Info("Press any key to stop...");
                Console.ReadKey(true);

                Stop();
            }
            else
            {
                // running as service
                using (var service = new FIleWatcherService())
                {
                    ServiceBase.Run(service);
                }
            }

        }

        private static void Start()
        {
            _fileWatcher = new SystemFileMonitor();

            _fileWatcher.Start();
        }

        private static void Stop()
        {
            _fileWatcher.Stop();
        }
    }
}