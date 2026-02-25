using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ChannelWindow
{
    class Program
    {
        private static string _channelName = "";
        private static string _username = "";
        private static string _pipeName = "";
        private static NamedPipeClientStream? _pipeClient;
        private static StreamWriter? _pipeWriter;
        private static bool _isRunning = true;
        private static string _currentInput = "";
        private static readonly object _consoleLock = new object();

        static async Task Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Invalid arguments");
                return;
            }

            _channelName = args[0];
            _username = args[1];
            _pipeName = args[2];

            Console.Title = $"Channel: {_channelName} - {_username}";
            Console.Clear();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  Channel: {_channelName,-48}║");
            Console.WriteLine($"║  User: {_username,-51}║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Type your messages and press Enter to send to this channel.");
            Console.WriteLine("Type '/exit' to close this channel window.\n");

            await ConnectToPipeAsync();

            var receiveTask = ReceiveMessagesAsync();
            var inputTask = HandleInputAsync();

            await Task.WhenAny(receiveTask, inputTask);

            _isRunning = false;
            _pipeWriter?.Close();
            _pipeClient?.Close();
        }

        static async Task ConnectToPipeAsync()
        {
            try
            {
                _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipeClient.ConnectAsync(5000);
                _pipeWriter = new StreamWriter(_pipeClient) { AutoFlush = true };

                await _pipeWriter.WriteLineAsync(JsonSerializer.Serialize(new { Type = "connected", Channel = _channelName }));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to connect: {ex.Message}");
                Console.ResetColor();
                _isRunning = false;
            }
        }

        static async Task ReceiveMessagesAsync()
        {
            if (_pipeClient == null) return;

            var reader = new StreamReader(_pipeClient);
            var buffer = new char[4096];

            try
            {
                while (_isRunning && _pipeClient.IsConnected)
                {
                    var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    var message = new string(buffer, 0, bytesRead);
                    var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        try
                        {
                            var msg = JsonSerializer.Deserialize<ChannelMessage>(line);
                            if (msg != null)
                            {
                                DisplayMessage(msg);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nConnection lost: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        static void DisplayMessage(ChannelMessage msg)
        {
            lock (_consoleLock)
            {
                ClearCurrentLine();

                var timestamp = DateTime.Now.ToString("HH:mm:ss");

                switch (msg.Type)
                {
                    case "message":
                        if (msg.Sender == _username)
                        {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.Write($"[{timestamp}] [you]");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write($"[{timestamp}] {msg.Sender}");
                        }
                        Console.ResetColor();
                        Console.WriteLine($": {msg.Body}");
                        break;

                    case "system":
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[{timestamp}] [SYSTEM] {msg.Body}");
                        Console.ResetColor();
                        break;

                    case "join":
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"[{timestamp}] → {msg.Sender} joined the channel");
                        Console.ResetColor();
                        break;

                    case "leave":
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[{timestamp}] ← {msg.Sender} left the channel");
                        Console.ResetColor();
                        break;
                }

                RestoreCurrentLine();
            }
        }

        static async Task HandleInputAsync()
        {
            while (_isRunning)
            {
                _currentInput = "";

                ConsoleKeyInfo keyInfo;
                while (_isRunning)
                {
                    if (!Console.KeyAvailable)
                    {
                        await Task.Delay(10);
                        continue;
                    }

                    keyInfo = Console.ReadKey(intercept: true);

                    lock (_consoleLock)
                    {
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            Console.WriteLine();
                            break;
                        }
                        else if (keyInfo.Key == ConsoleKey.Backspace)
                        {
                            if (_currentInput.Length > 0)
                            {
                                _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
                                Console.Write("\b \b");
                            }
                        }
                        else if (!char.IsControl(keyInfo.KeyChar))
                        {
                            _currentInput += keyInfo.KeyChar;
                            Console.Write(keyInfo.KeyChar);
                        }
                    }
                }

                if (!_isRunning)
                    break;

                var message = _currentInput.Trim();

                if (string.IsNullOrWhiteSpace(message))
                    continue;

                if (message.ToLower() == "/exit")
                {
                    _isRunning = false;
                    break;
                }

                await SendMessageAsync(message);
            }
        }

        static async Task SendMessageAsync(string message)
        {
            if (_pipeWriter == null) return;

            try
            {
                var msg = new { Type = "message", Channel = _channelName, Body = message };
                await _pipeWriter.WriteLineAsync(JsonSerializer.Serialize(msg));
            }
            catch { }
        }

        static void ClearCurrentLine()
        {
            Console.Write("\r" + new string(' ', Console.BufferWidth - 1) + "\r");
        }

        static void RestoreCurrentLine()
        {
            if (!string.IsNullOrEmpty(_currentInput))
            {
                Console.Write(_currentInput);
            }
        }

        class ChannelMessage
        {
            public string Type { get; set; } = "";
            public string Sender { get; set; } = "";
            public string Body { get; set; } = "";
        }
    }
}
