using System;
using System.ServiceProcess;
using log4net;

namespace FileWatcher
{
    class FIleWatcherService :ServiceBase
    {
        private SystemFileMonitor _fileWatcher;

        private readonly ILog _logger = LogManager.GetLogger("FIleWatcherService");


        public FIleWatcherService()
        {
            log4net.Config.XmlConfigurator.Configure();

        }

        protected override void OnStart(string[] args)
        {
            _logger.Info("Filewatcher Service is starting");
            try
            {
                _fileWatcher = new SystemFileMonitor();

                _fileWatcher.Start();
            }
            catch (Exception e)
            {
                _logger.ErrorFormat("Failed to start service Exception {0}", e);
                throw;
            }

        }

        protected override void OnStop()
        {
            try
            {
                _fileWatcher.Stop();
                

            }
            catch (Exception e)
            {
                _logger.ErrorFormat("Failed to stop service Exception {0}", e);
                throw;
            }
        }

        private void InitializeComponent()
        {
            // 
            // FIleWatcherService
            // 
            this.ServiceName = "FIleWatcherService";

        }
    }
}
