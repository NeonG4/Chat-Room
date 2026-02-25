using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Chat_Room
{
    /// <summary>
    /// Manages separate console window processes for each channel
    /// </summary>
    internal class ChannelWindowManager
    {
        private readonly Dictionary<string, ChannelWindowInfo> _channelWindows = new();
        private readonly string _username;
        private readonly Func<string, string, Task> _sendMessageToChannel;
        private string _channelWindowExePath = "";

        public ChannelWindowManager(string username, Func<string, string, Task> sendMessageToChannel)
        {
            _username = username;
            _sendMessageToChannel = sendMessageToChannel;
            FindChannelWindowExecutable();
        }

        private void FindChannelWindowExecutable()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var possiblePaths = new[]
            {
                Path.Combine(baseDir, "ChannelWindow.exe"),
                Path.Combine(baseDir, "..", "ChannelWindow", "bin", "Debug", "net8.0", "ChannelWindow.exe"),
                Path.Combine(baseDir, "..", "..", "ChannelWindow", "bin", "Debug", "net8.0", "ChannelWindow.exe"),
                Path.Combine(baseDir, "..", "..", "..", "ChannelWindow", "bin", "Debug", "net8.0", "ChannelWindow.exe"),
            };

            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    _channelWindowExePath = fullPath;
                    return;
                }
            }
        }

        public async Task OpenChannelWindow(string channelName)
        {
            if (_channelWindows.ContainsKey(channelName))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Channel window for '{channelName}' is already open.");
                Console.ResetColor();
                return;
            }

            if (string.IsNullOrEmpty(_channelWindowExePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Channel window executable not found. Please build the ChannelWindow project.");
                Console.ResetColor();
                return;
            }

            var pipeName = $"ChatRoom_Channel_{_username}_{channelName}_{Guid.NewGuid()}";
            var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _channelWindowExePath,
                    Arguments = $"\"{channelName}\" \"{_username}\" \"{pipeName}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                }
            };

            try
            {
                process.Start();

                var windowInfo = new ChannelWindowInfo
                {
                    ChannelName = channelName,
                    Process = process,
                    PipeServer = pipeServer,
                    PipeName = pipeName
                };

                _channelWindows[channelName] = windowInfo;

                _ = WaitForConnectionAsync(windowInfo);
                _ = MonitorProcessAsync(process, channelName);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Opened new window for channel '{channelName}'");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to open channel window: {ex.Message}");
                Console.ResetColor();
                pipeServer.Close();
            }
        }

        private async Task WaitForConnectionAsync(ChannelWindowInfo windowInfo)
        {
            try
            {
                await windowInfo.PipeServer.WaitForConnectionAsync();
                windowInfo.PipeWriter = new StreamWriter(windowInfo.PipeServer) { AutoFlush = true };
                windowInfo.PipeReader = new StreamReader(windowInfo.PipeServer);

                _ = ReceiveFromPipeAsync(windowInfo);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to connect pipe for {windowInfo.ChannelName}: {ex.Message}");
                Console.ResetColor();
            }
        }

        private async Task ReceiveFromPipeAsync(ChannelWindowInfo windowInfo)
        {
            if (windowInfo.PipeReader == null) return;

            try
            {
                while (windowInfo.PipeServer.IsConnected)
                {
                    var line = await windowInfo.PipeReader.ReadLineAsync();
                    if (line == null) break;

                    try
                    {
                        var msg = JsonSerializer.Deserialize<JsonElement>(line);
                        var type = msg.GetProperty("Type").GetString();

                        if (type == "message")
                        {
                            var body = msg.GetProperty("Body").GetString();
                            if (!string.IsNullOrEmpty(body))
                            {
                                await _sendMessageToChannel(windowInfo.ChannelName, body);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task MonitorProcessAsync(Process process, string channelName)
        {
            await Task.Run(() =>
            {
                process.WaitForExit();
                CloseChannel(channelName);
            });
        }

        public void DisplayMessageInChannel(string channelName, string sender, string message)
        {
            if (!_channelWindows.TryGetValue(channelName, out var windowInfo))
                return;

            if (windowInfo.PipeWriter == null || !windowInfo.PipeServer.IsConnected)
                return;

            try
            {
                var msg = new
                {
                    Type = "message",
                    Sender = sender,
                    Body = message
                };

                windowInfo.PipeWriter.WriteLine(JsonSerializer.Serialize(msg));
            }
            catch { }
        }

        public void DisplaySystemMessageInChannel(string channelName, string message)
        {
            if (!_channelWindows.TryGetValue(channelName, out var windowInfo))
                return;

            if (windowInfo.PipeWriter == null || !windowInfo.PipeServer.IsConnected)
                return;

            try
            {
                var msg = new
                {
                    Type = "system",
                    Sender = "",
                    Body = message
                };

                windowInfo.PipeWriter.WriteLine(JsonSerializer.Serialize(msg));
            }
            catch { }
        }

        public void DisplayJoinInChannel(string channelName, string username)
        {
            if (!_channelWindows.TryGetValue(channelName, out var windowInfo))
                return;

            if (windowInfo.PipeWriter == null || !windowInfo.PipeServer.IsConnected)
                return;

            try
            {
                var msg = new
                {
                    Type = "join",
                    Sender = username,
                    Body = ""
                };

                windowInfo.PipeWriter.WriteLine(JsonSerializer.Serialize(msg));
            }
            catch { }
        }

        public void DisplayLeaveInChannel(string channelName, string username)
        {
            if (!_channelWindows.TryGetValue(channelName, out var windowInfo))
                return;

            if (windowInfo.PipeWriter == null || !windowInfo.PipeServer.IsConnected)
                return;

            try
            {
                var msg = new
                {
                    Type = "leave",
                    Sender = username,
                    Body = ""
                };

                windowInfo.PipeWriter.WriteLine(JsonSerializer.Serialize(msg));
            }
            catch { }
        }

        public void CloseChannel(string channelName)
        {
            if (_channelWindows.TryGetValue(channelName, out var windowInfo))
            {
                try
                {
                    windowInfo.PipeWriter?.Close();
                    windowInfo.PipeReader?.Close();
                    windowInfo.PipeServer?.Close();

                    if (!windowInfo.Process.HasExited)
                    {
                        windowInfo.Process.Kill();
                    }
                    windowInfo.Process.Dispose();
                }
                catch { }

                _channelWindows.Remove(channelName);
            }
        }

        public void CloseAllChannels()
        {
            foreach (var channelName in _channelWindows.Keys.ToList())
            {
                CloseChannel(channelName);
            }
        }

        public bool IsChannelWindowOpen(string channelName)
        {
            return _channelWindows.ContainsKey(channelName) &&
                   !_channelWindows[channelName].Process.HasExited;
        }

        private class ChannelWindowInfo
        {
            public string ChannelName { get; set; } = "";
            public Process Process { get; set; } = null!;
            public NamedPipeServerStream PipeServer { get; set; } = null!;
            public StreamWriter? PipeWriter { get; set; }
            public StreamReader? PipeReader { get; set; }
            public string PipeName { get; set; } = "";
        }
    }
}
