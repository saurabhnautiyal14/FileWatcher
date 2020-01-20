using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FileWatcher
{
    public class SystemFileMonitor : IDisposable
    {
        private readonly IList<FileSystemWatcher> _fileSystemWatchers = new List<FileSystemWatcher>();
        private readonly ILog _logger = LogManager.GetLogger("SystemFileMonitor");
        private readonly HttpClient _httpClient;
        private readonly Uri _httpUri;
        private readonly FileSystemWatcher _changeListnerFileWatcher;
        private readonly string _watchDirFile;


        public SystemFileMonitor()
        {
           var currentFolder= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
           var urlFile = Path.Combine(currentFolder, "UrlConfig.txt");
           _watchDirFile = Path.Combine(currentFolder, "WatchDir.txt");

           _changeListnerFileWatcher = new FileSystemWatcher(currentFolder , "WatchDir.txt");
           _changeListnerFileWatcher.EnableRaisingEvents = true; 
           _changeListnerFileWatcher.Changed += _changeListnerFileWatcher_Changed;

            _logger.Info($"Url File location...'{urlFile}'  !!!");
            _logger.Info($"Watch Dir File location...'{_watchDirFile}'  !!!");
            var urls = File.ReadAllLines(urlFile).ToList();
            var dirsToWatch = File.ReadAllText(_watchDirFile).Split(';');
            string publishUrl = urls.FirstOrDefault();

            if (string.IsNullOrEmpty(publishUrl))
            {

                 publishUrl = System.Configuration.ConfigurationManager.AppSettings["Url"];
            }

            _logger.Info($"FileMonitor Publish Url {publishUrl}...");
            var monitorAllDrives = Convert.ToBoolean(System.Configuration.ConfigurationManager.AppSettings["MonitorAllDrives"]);


            bool success = Uri.TryCreate(publishUrl, UriKind.Absolute, out _httpUri)
                         && (_httpUri.Scheme == Uri.UriSchemeHttp || _httpUri.Scheme == Uri.UriSchemeHttps);

            if (!success)
            {
                _logger.ErrorFormat("Not a Valid Http Url {0}", publishUrl);
            }
            else
            {
                _httpClient = new HttpClient();
            }


            if (monitorAllDrives)
            {
                DriveInfo[] drives = DriveInfo.GetDrives();

                foreach (var drive in drives)
                {
                    var fileSystemWatcher = new FileSystemWatcher { Path = drive.RootDirectory.FullName };

                    _logger.InfoFormat("Monitoring Location {0} ...", fileSystemWatcher.Path);
                    AttachWatcher(fileSystemWatcher);

                    _fileSystemWatchers.Add(fileSystemWatcher);

                }
            }
            else
            {
                WatchDirectory(dirsToWatch);
            }


        }

        private void _changeListnerFileWatcher_Changed(object sender, 
            FileSystemEventArgs e)
        {
            _logger.Info("Disposing Existing watcher ... !!!");
            this.Dispose();

            _logger.Info("Disposed Existing watcher Successfully ... !!!");
            var dirsToWatch = File.ReadAllText(_watchDirFile).Split(';');
            WatchDirectory(dirsToWatch);
            Start();

            
        }

        private void WatchDirectory(string[] dirToWatch)
        {
            foreach (var fileOrFolder in dirToWatch)
            {
                if (string.IsNullOrWhiteSpace(fileOrFolder)) continue;

                var fileSystemWatcher = new FileSystemWatcher {Path = fileOrFolder};

                fileSystemWatcher.InternalBufferSize = 60 * 1024; //32kb

                _logger.InfoFormat("Monitoring Location {0} ...", fileSystemWatcher.Path);
                AttachWatcher(fileSystemWatcher);

                _fileSystemWatchers.Add(fileSystemWatcher);
            }
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        private void AttachWatcher(FileSystemWatcher watcher)
        {
            try
            {
                watcher.NotifyFilter = NotifyFilters.LastWrite
                                       | NotifyFilters.FileName
                                       | NotifyFilters.DirectoryName;


                watcher.Filter = "*";
                watcher.IncludeSubdirectories = true;

                watcher.Error += Watcher_Error;
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;

            }
            catch (Exception e)
            {
                _logger.InfoFormat("Failed to monitor location {0} with exception {1} ", watcher.Path, e);
                throw;
            }

        }

        private void Watcher_Error(object sender, System.IO.ErrorEventArgs e)
        {
            if (e.GetException().GetType() == typeof(InternalBufferOverflowException))
            {
                _logger.Error($" Watcher InternalBufferOverflowException {e.GetException().ToString()}");

            }
            _logger.Error($" Watcher Error {e.GetException().ToString()}");
        }

        public void Start()
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.EnableRaisingEvents = true;

            }
        }

        public void Stop()
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.EnableRaisingEvents = false;

            }
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            Task.Factory.StartNew(async () =>
            {
                if (ShouldPublish(e))
                {

                    string eventType = string.Empty;
                    if (e.ChangeType == WatcherChangeTypes.Created)
                    {
                        eventType = "C";
                    }
                    else if (e.ChangeType == WatcherChangeTypes.Changed)
                    {
                        if (File.GetAttributes(e.FullPath).HasFlag(FileAttributes.Directory) ||
                            File.GetAttributes(e.FullPath).HasFlag(FileAttributes.Temporary))
                        {
                            return;
                        }

                        eventType = "U";
                    }
                    else if (e.ChangeType == WatcherChangeTypes.Deleted)
                    {
                        eventType = "D";
                    }

                    _logger.InfoFormat($"File MonitorEvent - File Name : {e.FullPath} EventTYpe : {e.ChangeType} ");
                    await Publish(e.FullPath, eventType);

                }
            });


        }


        private  void OnRenamed(object source, RenamedEventArgs e)
        {
            Task.Factory.StartNew(async () =>
            {
                if (ShouldPublish(e))
                {
                    _logger.InfoFormat(
                        $"File MonitorEvent - New File Name : {e.FullPath} EventTYpe : {e.ChangeType} Old File Name {e.OldName}");

                    if (File.GetAttributes(e.FullPath).HasFlag(FileAttributes.Temporary))
                    {
                        return;
                    }

                    await Publish(e.FullPath, "R", e.OldFullPath);
                }
            });
        }

        private  bool ShouldPublish(FileSystemEventArgs e)
        {
            return !e.FullPath.Equals(this._watchDirFile);
            //return !e.FullPath.ToUpper().Contains("C:\\WINDOWS") &&
            //       !e.FullPath.ToUpper().Contains("C:\\USERS")
            //       && !e.FullPath.ToUpper().Contains("C:\\PROGRAMDATA")
            //       && !e.FullPath.ToUpper().Contains("C:\\PROGRAM FILES")
            //       && !e.FullPath.ToUpper().Contains("C:\\$RECYCLE.BIN");
        }

        private async Task Publish(string fileName, string changedType, string oldFileName = null)
        {
            DefaultContractResolver contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };

            var json = JsonConvert.SerializeObject(new ChangedFileDetails
            {
                    File = fileName,
                    Type = changedType,
                    OldFile = oldFileName
                },
                new JsonSerializerSettings
                {
                    ContractResolver = contractResolver,
                    Formatting = Formatting.Indented
                });


            var stringContent = new StringContent(json, 
                Encoding.UTF8,
                "application/json");


            if (_httpClient != null)
            {
                try
                {
                    _logger.Info("Publishing json !!!");
                    _logger.Info(json);

                    var response = await _httpClient.PostAsync(_httpUri, stringContent);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.Info($"Published Event : Successfully Status Code {response.StatusCode} ");

                    }
                    else
                    {
                        _logger.Error(
                            $"Published Event : Failed  Status Code {response.StatusCode}!!!, Content : {json} Request Message {response.RequestMessage} Response : {response}");

                    }


                }
                catch (HttpRequestException ex)
                {
                    _logger.Error($"Published Event : {fileName + " " + changedType} Failed !!! ");

                    _logger.Error(ex);
                }
            }


        }

        public void Dispose()
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.EnableRaisingEvents = false;

                watcher.Dispose();
            }

            _fileSystemWatchers.Clear();
        }
    }
}
