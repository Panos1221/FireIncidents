using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FireIncidents.Logging
{
    [ProviderAlias("File")]
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileLoggerOptions _options;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new ConcurrentDictionary<string, FileLogger>();
        private readonly BlockingCollection<string> _messageQueue = new BlockingCollection<string>(1000);
        private readonly Task _outputTask;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public FileLoggerProvider(IOptions<FileLoggerOptions> options)
        {
            _options = options.Value;

            // Create log directory if it doesn't exist
            if (!Directory.Exists(_options.BasePath))
            {
                Directory.CreateDirectory(_options.BasePath);
            }

            // Start background task to process log entries
            _outputTask = Task.Factory.StartNew(ProcessLogQueue, _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));
        }

        public void AddMessage(string message)
        {
            if (!_messageQueue.IsAddingCompleted)
            {
                try
                {
                    _messageQueue.Add(message, _cancellationTokenSource.Token);
                    return;
                }
                catch (InvalidOperationException) { }
                catch (OperationCanceledException) { }
            }
        }

        private void ProcessLogQueue()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                string currentFileName = GetCurrentFileName();
                var currentFileInfo = new FileInfo(currentFileName);

                try
                {
                    if (currentFileInfo.Exists && currentFileInfo.Length > _options.FileSizeLimitBytes)
                    {
                        // Roll the file if it's too large
                        RollFiles();
                        currentFileName = GetCurrentFileName();
                    }

                    ProcessCurrentQueue(currentFileName);
                }
                catch (Exception ex)
                {
                    try
                    {
                        string errorMessage = $"Error processing log queue: {ex}";
                        File.AppendAllText(Path.Combine(_options.BasePath, "logging-error.txt"), errorMessage + Environment.NewLine);
                    }
                    catch { }
                }

                Thread.Sleep(1000); // every sec
            }
        }

        private void ProcessCurrentQueue(string fileName)
        {
            using (var streamWriter = File.AppendText(fileName))
            {
                while (_messageQueue.TryTake(out string message, 100))
                {
                    streamWriter.WriteLine(message);
                }
                streamWriter.Flush();
            }
        }

        private string GetCurrentFileName()
        {
            return Path.Combine(_options.BasePath,
                _options.FileNamePattern.Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd")));
        }

        private void RollFiles()
        {
            try
            {
                for (int i = _options.MaxRollingFiles - 1; i >= 0; i--)
                {
                    string currentFileName = GetCurrentFileName();
                    string rolledFileName = currentFileName + "." + i;
                    string nextRolledFileName = currentFileName + "." + (i + 1);

                    if (i == 0)
                    {
                        if (File.Exists(currentFileName))
                        {
                            if (File.Exists(rolledFileName))
                                File.Delete(rolledFileName);

                            File.Move(currentFileName, rolledFileName);
                        }
                    }
                    else if (File.Exists(rolledFileName))
                    {
                        if (File.Exists(nextRolledFileName))
                            File.Delete(nextRolledFileName);

                        File.Move(rolledFileName, nextRolledFileName);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string errorMessage = $"Error rolling log files: {ex}";
                    File.AppendAllText(Path.Combine(_options.BasePath, "logging-error.txt"), errorMessage + Environment.NewLine);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _messageQueue.CompleteAdding();

            try
            {
                _outputTask.Wait(1500); // Wait for the output task to complete
            }
            catch (TaskCanceledException) { }
            catch (AggregateException) { }

            _cancellationTokenSource.Dispose();
            _messageQueue.Dispose();
        }

        private class FileLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly FileLoggerProvider _provider;

            public FileLogger(string categoryName, FileLoggerProvider provider)
            {
                _categoryName = categoryName;
                _provider = provider;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null; 
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                string logLevelString = logLevel.ToString().PadRight(12);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string message = formatter(state, exception);

                // Format: [TIMESTAMP] [LEVEL] [CATEGORY] Message
                string formattedMessage = $"[{timestamp}] [{logLevelString}] [{_categoryName}] {message}";

                if (exception != null)
                {
                    formattedMessage += Environment.NewLine + "Exception: " + exception + Environment.NewLine;
                }

                _provider.AddMessage(formattedMessage);
            }
        }
    }

    public class FileLoggerOptions
    {
        public string BasePath { get; set; } = "Logs";
        public string FileNamePattern { get; set; } = "log-{Date}.txt";
        public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024; // 10 MB default
        public int MaxRollingFiles { get; set; } = 30;
    }
}