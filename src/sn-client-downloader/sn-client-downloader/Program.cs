// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;
using NLog;
using SenseNet.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
namespace sn_client_downloader
{
    class Program
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        static ServerContext? serverContext;
        static Config? configuration;
        static long errorCount = 0;
        public static Config? Configuration
        {
            get
            {
                if (configuration != null) { return configuration; }
                try
                {
                    var builder = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: false);

                    IConfiguration config = builder.Build();
                    configuration = config.Get<Config>();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Wrong configuration");
                }
                return configuration;
            }
        }

        static HttpClient? _client;
        public static HttpClient Client
        {
            get
            {
                if (_client == null)
                {
                    if (Configuration != null)
                    {
                        _client = new HttpClient();
                        string? authenticationString = $"{Configuration.UserName}:{Configuration.Password}";
                        string Base64EncodedAuthenticationString = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(authenticationString));
                        _client.DefaultRequestHeaders.Add("Authorization", "Basic " + Base64EncodedAuthenticationString);
                        return _client;
                    }
                    else
                    {
                        throw new Exception("Client initializaion failed.");
                    }
                }
                else
                {
                    return _client;
                }
            }
        }

        static void ProccessTargets()
        {
            logger.Info("Processing targets");
            if (Configuration == null || Configuration.Target == null)
            {
                logger.Info("Targets empty. Exit.");
                Environment.Exit(500);
            }
            foreach (var target in Configuration.Target)
            {
                if (target == null || string.IsNullOrEmpty(target.SNPath) || string.IsNullOrEmpty(target.LocalPath))
                {
                    continue;
                }
                var content = LoadContent(target.SNPath);
                if (content != null)
                {
                    logger.Info("Target loaded:" + content.Path + " -> " + target.LocalPath);
                    var select = new[] { "Path", "Name", "DisplayName", "ModificationDate" };
                    //Get contents from sensenet
                    var settings = new QuerySettings() { EnableAutofilters = FilterStatus.Disabled, EnableLifespanFilter = FilterStatus.Disabled };
                  
                    List<Content> foldersResult = new();
                    try
                    {
                        foldersResult = ClientLibraryRetryer.Query("+InTree:'" + content.Path + "' +TypeIs:Folder", select, settings, serverContext);
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        logger.Error(ex, "Folder query failed:" + content.Path + "(" + target.SNPath + " | " + target.LocalPath + ")(" + ex.Message + ")");
                        continue;
                    }
                    var folders = foldersResult.OrderBy(p => p.Path);
                    foreach (var folder in folders)
                    {
                        string pathPart = folder.Path.Replace(target.SNPath, "").Replace('/', Path.DirectorySeparatorChar);
                        string filePath = Path.Join(target.LocalPath, pathPart);
                        //download all contents
                        DownloadFiles(folder, filePath);
                        logger.Info(folder.Path);
                    }
                }
                else
                {
                    logger.Info("Target is null");
                }
            }
        }
        static void DownloadFiles(Content folder, string filePath)
        {
            if (folder == null || string.IsNullOrEmpty(filePath))
            {
                return;
            }
            logger.Info("#Folder:" + folder.Path);

            var select = new[] { "Path", "Name", "DisplayName", "ModificationDate" };
            var settings = new QuerySettings() { EnableAutofilters = FilterStatus.Disabled, EnableLifespanFilter = FilterStatus.Disabled };
            List<Content> filesResult =new();
            try
            {
                filesResult = ClientLibraryRetryer.Query("+InFolder:'" + folder.Path + "' +TypeIs:File", select, settings, serverContext); 
            }
            catch (Exception ex)
            {
                errorCount++; 
                logger.Error(ex, "Donwload query failed:" + folder.Path + "(" + filePath + ")(" + ex.Message + ")");
            }
           
                foreach (var file in filesResult)
                {
                    string downloadUrl = Configuration?.RepoURL + file.Path + "?download";
                    //Get download file path
                    string localPath = Path.Join(filePath, file.Name);
                    string containerFolder = filePath;
                    if (!Directory.Exists(containerFolder))
                    {
                        try
                        {
                            Directory.CreateDirectory(containerFolder);
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            logger.Error(ex, "Create directory failed:" + containerFolder + "(" + filePath + " | " + folder.Path + ")(" + ex.Message + ")");
                            continue;
                        }

                        _ = DateTime.TryParse(folder["ModificationDate"].ToString(), out DateTime lastWriteTime);
                        try
                        {
                            Directory.SetLastWriteTimeUtc(containerFolder, lastWriteTime);
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            logger.Error(ex, "SetLastWriteTimeUtc failed:" + containerFolder + "(" + filePath + " | " + folder.Path + ")(" + ex.Message + ")");
                        }
                    }

                    _ = DateTime.TryParse(file["ModificationDate"].ToString(), out DateTime modificationDate);
                    if (File.Exists(localPath))
                    {
                        //check dates
                        FileInfo fileInfo = new(localPath);
                        if (fileInfo.LastWriteTimeUtc < modificationDate)
                        {
                            try
                            {
                                logger.Info(">Update:" + file.Name);
                                var fileBytesTask = Client.GetByteArrayAsync(downloadUrl);

                                fileBytesTask.Wait();
                                byte[] fileBytes = fileBytesTask.Result;
                                File.WriteAllBytes(localPath, fileBytes);

                                File.SetLastWriteTimeUtc(localPath, modificationDate);
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                logger.Error(ex, "Download failed:" + file.Path + "(" + downloadUrl + ")(" + ex.Message + ")");
                            }
                        }
                        else
                        {
                            logger.Info(">Skip:" + file.Name);
                        }
                    }
                    else
                    {
                        //does not exist create!
                        try
                        {
                            logger.Info(">Download:" + file.Name);
                            var fileBytesTask = Client.GetByteArrayAsync(downloadUrl);
                            fileBytesTask.Wait();
                            byte[] fileBytes = fileBytesTask.Result;
                            File.WriteAllBytes(localPath, fileBytes);
                            File.SetLastWriteTimeUtc(localPath, modificationDate);
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            logger.Error(ex, "Download failed:" + file.Path + "(" + downloadUrl + ")(" + ex.Message + ")");
                        }
                    }
                }
        }
        static void Main()
        {
            var config = new NLog.Config.LoggingConfiguration();

            // Targets where to log to: File and Console
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "./logs/log_" + DateTime.Now.ToString("yyyy.MM.dd_HH_mm_ss") + ".txt" };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            // Rules for mapping loggers to targets            
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

            // Apply config           
            NLog.LogManager.Configuration = config;
            var s1 = Stopwatch.StartNew();

            logger.Info("sensenet client library baser file downloader tool.");
            logger.Info("#############################");
            try
            {
                if (Configuration == null)
                {
                    logger.Info("Configuration is null. Exit.");
                    return;
                }
                logger.Info("Create the servercontext.");
                serverContext = new ServerContext()
                {
                    Password = Configuration.Password,
                    Username = Configuration.UserName,
                    Url = Configuration.RepoURL
                };
                ClientLibraryRetryer.Configuration = configuration;
                ClientLibraryRetryer.Logger = logger;
                logger.Info("ProccessTargets.");
                ProccessTargets();
            }
            catch (Exception ex)
            {
                errorCount++;
                logger.Error(ex, "Main exception:" + ex.Message);
                s1.Stop();
                var elapsedTime = s1.Elapsed;
                logger.Info("Error count: " + errorCount.ToString());
                logger.Info("Running duration: " + elapsedTime.ToString(@"dd\ hh\:mm\:ss") + "(dd hh:mm:ss");
                logger.Info("Done");
                NLog.LogManager.Shutdown();
                Environment.Exit(501);
            }
            finally
            {
                s1.Stop();
                var elapsedTime = s1.Elapsed;
                logger.Info("Error count: " + errorCount.ToString());
                logger.Info("Running duration: " + elapsedTime.ToString(@"dd\ hh\:mm\:ss") + "(dd hh:mm:ss");
                logger.Info("Done");
                NLog.LogManager.Shutdown();
            }
        }
        public static Content LoadContent(string contentPath)
        {
            var testTask = Content.LoadAsync(contentPath, serverContext);
            testTask.Wait();
            if (testTask.Result == null)
            {
                throw new UnauthorizedAccessException("Content does not exist or does not have enough permission for load. ");
            }
            else
            {
                return testTask.Result;
            }
        }
    }
}
