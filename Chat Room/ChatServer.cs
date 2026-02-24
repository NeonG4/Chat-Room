using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Chat_Room
{
    internal class ChatServer
    {
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
        private readonly UserManager _userManager = new();
        private readonly ChannelManager _channelManager = new();
        private bool _isRunning;
        private int _port;
        private string _currentInput = "";
        private readonly object _consoleLock = new object();
        private string _serverIp = "";

        public ChatServer(int port = 5000)
        {
            _port = port;
        }

        public async Task Start()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            // Get local IP address
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var localIp = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                _serverIp = localIp?.ToString() ?? "Unknown";
            }
            catch
            {
                _serverIp = "Unable to determine";
            }

            Console.WriteLine($"Server started on port {_port}");
            Console.WriteLine($"Server IP: {_serverIp}");
            Console.WriteLine($"Total registered users: {_userManager.GetTotalUsers()}");
            Console.WriteLine("Waiting for clients to connect...");
            Console.WriteLine("Type /help for a list of server commands\n");

            // Start server command handler
            _ = HandleServerCommandsAsync();

            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[Error] Accepting client: {ex.Message}");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                }
            }
        }

        private async Task HandleServerCommandsAsync()
        {
            await Task.Run(async () =>
            {
                while (_isRunning)
                {
                    _currentInput = "";

                    // Read input character by character
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

                    var input = _currentInput.Trim();

                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    var parts = input.Split(' ', 2);
                    var command = parts[0].ToLower();

                    switch (command)
                    {
                        case "/broadcast":
                            if (parts.Length > 1)
                            {
                                await BroadcastJsonAsync("server", parts[1], null);
                                lock (_consoleLock)
                                {
                                    ClearCurrentLine();
                                    Console.ForegroundColor = ConsoleColor.Blue;
                                    Console.WriteLine($"[Broadcast] {parts[1]}");
                                    Console.ResetColor();
                                    RestoreCurrentLine();
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /broadcast <message>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/say":
                            if (parts.Length > 1)
                            {
                                await BroadcastJsonAsync("message", parts[1], null, "SERVER");
                                lock (_consoleLock)
                                {
                                    ClearCurrentLine();
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"SERVER: {parts[1]}");
                                    Console.ResetColor();
                                    RestoreCurrentLine();
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /say <message>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/msg":
                            if (parts.Length > 1)
                            {
                                var msgParts = parts[1].Split(' ', 2);
                                if (msgParts.Length >= 2)
                                {
                                    var targetUsername = msgParts[0].Trim();
                                    var privateMessage = msgParts[1];

                                    if (_clients.TryGetValue(targetUsername, out var targetClient))
                                    {
                                        await SendJsonAsync(targetClient.GetStream(), "private", privateMessage, "SERVER");
                                        lock (_consoleLock)
                                        {
                                            ClearCurrentLine();
                                            Console.ForegroundColor = ConsoleColor.Cyan;
                                            Console.WriteLine($"[PM to {targetUsername}] {privateMessage}");
                                            Console.ResetColor();
                                            RestoreCurrentLine();
                                        }
                                    }
                                    else
                                    {
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Red;
                                            Console.WriteLine($"User '{targetUsername}' not found");
                                            Console.ResetColor();
                                        }
                                    }
                                }
                                else
                                {
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("Usage: /msg <username> <message>");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /msg <username> <message>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/kick":
                            if (parts.Length > 1)
                            {
                                var username = parts[1].Trim();
                                if (_clients.TryGetValue(username, out var client))
                                {
                                    await SendJsonAsync(client.GetStream(), "server", "You have been kicked from the server.");
                                    client.Close();
                                    _clients.TryRemove(username, out _);
                                    lock (_consoleLock)
                                    {
                                        ClearCurrentLine();
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"[Kicked] {username}");
                                        Console.ResetColor();
                                        RestoreCurrentLine();
                                    }
                                    await BroadcastJsonAsync("server", $"{username} was kicked from the server", null);
                                }
                                else
                                {
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine($"User '{username}' not found");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /kick <username>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/list":
                            lock (_consoleLock)
                            {
                                if (_clients.Count > 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine($"Connected users ({_clients.Count}):");
                                    foreach (var username in _clients.Keys)
                                    {
                                        Console.WriteLine($"  - {username}");
                                    }
                                    Console.ResetColor();
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine("No users connected");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/help":
                            lock (_consoleLock)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("\nAvailable Server Commands:");
                                Console.WriteLine("  /help                     - Show this help message");
                                Console.WriteLine("  /info                     - Show server IP and port information");
                                Console.WriteLine("  /broadcast <message>      - Send a server announcement to all clients");
                                Console.WriteLine("  /say <message>            - Send a chat message as SERVER");
                                Console.WriteLine("  /msg <username> <message> - Send a private message to a specific user");
                                Console.WriteLine("  /kick <username>          - Disconnect a specific user");
                                Console.WriteLine("  /list                     - List all connected users");
                                Console.WriteLine("  /users                    - List all registered users with statistics");
                                Console.WriteLine("  /stats <username>         - Show detailed stats for a specific user");
                                Console.WriteLine("  /mute <username> <minutes> - Mute a user for specified minutes");
                                Console.WriteLine("  /unmute <username>        - Unmute a user");
                                Console.WriteLine("  /history <username> [count] - View user's message history");
                                Console.WriteLine("\n  Admin Management:");
                                Console.WriteLine("  /promote <username>       - Promote a user to admin");
                                Console.WriteLine("  /demote <username>        - Demote an admin to regular user");
                                Console.WriteLine("  /admins                   - List all server admins");
                                Console.WriteLine("\n  Channel Management:");
                                Console.WriteLine("  /channels                 - List all private channels");
                                Console.WriteLine("  /channelinfo <channel>    - Show detailed info about a channel");
                                Console.WriteLine("\n  /stop                     - Stop the server");
                                Console.ResetColor();
                            }
                            break;

                        case "/info":
                            lock (_consoleLock)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("\nServer Information:");
                                Console.WriteLine($"  IP Address: {_serverIp}");
                                Console.WriteLine($"  Port: {_port}");
                                Console.WriteLine($"  Status: {(_isRunning ? "Running" : "Stopped")}");
                                Console.WriteLine($"  Connected users: {_clients.Count}");
                                Console.WriteLine($"  Total registered users: {_userManager.GetTotalUsers()}");

                                var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                                Console.WriteLine($"  Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
                                Console.ResetColor();
                            }
                            break;

                        case "/users":
                            lock (_consoleLock)
                            {
                                var allUsers = _userManager.GetAllUsers();
                                if (allUsers.Count > 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine($"\nRegistered users ({allUsers.Count}):");
                                    Console.WriteLine($"{"Username",-20} {"Messages",-10} {"Last Login",-20}");
                                    Console.WriteLine(new string('-', 50));
                                    foreach (var user in allUsers.OrderByDescending(u => u.MessageCount))
                                    {
                                        var status = _clients.ContainsKey(user.Username) ? " (online)" : "";
                                        Console.WriteLine($"{user.Username + status,-20} {user.MessageCount,-10} {user.LastLogin:yyyy-MM-dd HH:mm}");
                                    }
                                    Console.ResetColor();
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine("No registered users");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/stats":
                            if (parts.Length > 1)
                            {
                                var username = parts[1].Trim();
                                var userAccount = _userManager.GetUserAccount(username);

                                lock (_consoleLock)
                                {
                                    if (userAccount != null)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.WriteLine($"\nStatistics for {username}:");
                                        Console.WriteLine($"  Total messages: {userAccount.MessageCount}");
                                        Console.WriteLine($"  Account created: {userAccount.CreatedDate:yyyy-MM-dd HH:mm:ss}");
                                        Console.WriteLine($"  Last login: {userAccount.LastLogin:yyyy-MM-dd HH:mm:ss}");
                                        Console.WriteLine($"  Currently online: {(_clients.ContainsKey(username) ? "Yes" : "No")}");
                                        Console.WriteLine($"  Admin: {(userAccount.IsAdmin ? "Yes" : "No")}");
                                        if (userAccount.IsAdmin && userAccount.PromotedBy != null)
                                        {
                                            Console.WriteLine($"  Promoted by: {userAccount.PromotedBy} on {userAccount.PromotedDate:yyyy-MM-dd HH:mm:ss}");
                                        }
                                        Console.WriteLine($"  Muted: {(userAccount.IsMuted ? $"Yes (until {userAccount.MutedUntil:yyyy-MM-dd HH:mm:ss})" : "No")}");
                                        Console.ResetColor();
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine($"User '{username}' not found");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /stats <username>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/mute":
                            if (parts.Length > 1)
                            {
                                var muteParts = parts[1].Split(' ', 2);
                                if (muteParts.Length >= 2)
                                {
                                    var username = muteParts[0].Trim();
                                    if (int.TryParse(muteParts[1], out int minutes))
                                    {
                                        if (_userManager.UserExists(username))
                                        {
                                            _userManager.MuteUser(username, minutes);
                                            lock (_consoleLock)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Yellow;
                                                Console.WriteLine($"[Muted] {username} for {minutes} minutes");
                                                Console.ResetColor();
                                            }

                                            if (_clients.ContainsKey(username))
                                            {
                                                await SendJsonAsync(_clients[username].GetStream(), "server",
                                                    $"You have been muted for {minutes} minutes.");
                                            }
                                        }
                                        else
                                        {
                                            lock (_consoleLock)
                                            {
                                                Console.ForegroundColor = ConsoleColor.Red;
                                                Console.WriteLine($"User '{username}' not found");
                                                Console.ResetColor();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Red;
                                            Console.WriteLine("Invalid time format. Use minutes as a number.");
                                            Console.ResetColor();
                                        }
                                    }
                                }
                                else
                                {
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("Usage: /mute <username> <minutes>");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /mute <username> <minutes>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/unmute":
                            if (parts.Length > 1)
                            {
                                var username = parts[1].Trim();
                                if (_userManager.UserExists(username))
                                {
                                    _userManager.UnmuteUser(username);
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($"[Unmuted] {username}");
                                        Console.ResetColor();
                                    }

                                    if (_clients.ContainsKey(username))
                                    {
                                        await SendJsonAsync(_clients[username].GetStream(), "server",
                                            "You have been unmuted.");
                                    }
                                }
                                else
                                {
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine($"User '{username}' not found");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /unmute <username>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/history":
                            if (parts.Length > 1)
                            {
                                var histParts = parts[1].Split(' ', 2);
                                var username = histParts[0].Trim();
                                int count = 10;

                                if (histParts.Length > 1)
                                {
                                    int.TryParse(histParts[1], out count);
                                }

                                var history = _userManager.GetMessageHistory(username, count);

                                lock (_consoleLock)
                                {
                                    if (history.Count > 0)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.WriteLine($"\nMessage history for {username} (last {history.Count} messages):");
                                        foreach (var msg in history)
                                        {
                                            Console.WriteLine($"  {msg}");
                                        }
                                        Console.ResetColor();
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.DarkGray;
                                        Console.WriteLine($"No message history found for {username}");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /history <username> [count]");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/promote":
                            if (parts.Length > 1)
                            {
                                var username = parts[1].Trim();
                                if (_userManager.UserExists(username))
                                {
                                    if (!_userManager.IsAdmin(username))
                                    {
                                        _userManager.PromoteToAdmin(username, "SERVER");
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Magenta;
                                            Console.WriteLine($"[Promoted] {username} is now an admin");
                                            Console.ResetColor();
                                        }

                                        if (_clients.ContainsKey(username))
                                        {
                                            await SendJsonAsync(_clients[username].GetStream(), "server",
                                                "You have been promoted to admin! You now have access to admin commands.");
                                        }

                                        await BroadcastJsonAsync("server", $"{username} has been promoted to admin", null);
                                    }
                                    else
                                    {
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"User '{username}' is already an admin");
                                            Console.ResetColor();
                                        }
                                    }
                                }
                                else
                                {
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine($"User '{username}' not found");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /promote <username>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/demote":
                            if (parts.Length > 1)
                            {
                                var username = parts[1].Trim();
                                if (_userManager.UserExists(username))
                                {
                                    if (_userManager.IsAdmin(username))
                                    {
                                        _userManager.DemoteFromAdmin(username);
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"[Demoted] {username} is no longer an admin");
                                            Console.ResetColor();
                                        }

                                        if (_clients.ContainsKey(username))
                                        {
                                            await SendJsonAsync(_clients[username].GetStream(), "server",
                                                "You have been demoted from admin status.");
                                        }

                                        await BroadcastJsonAsync("server", $"{username} has been demoted from admin", null);
                                    }
                                    else
                                    {
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"User '{username}' is not an admin");
                                            Console.ResetColor();
                                        }
                                    }
                                }
                                else
                                {
                                    lock (_consoleLock)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine($"User '{username}' not found");
                                        Console.ResetColor();
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /demote <username>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/admins":
                            lock (_consoleLock)
                            {
                                var adminList = _userManager.GetAdmins();
                                if (adminList.Count > 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine($"\nServer Admins ({adminList.Count}):");
                                    Console.WriteLine($"{"Username",-20} {"Promoted By",-15} {"Promoted Date",-20}");
                                    Console.WriteLine(new string('-', 55));
                                    foreach (var admin in adminList)
                                    {
                                        var status = _clients.ContainsKey(admin.Username) ? " (online)" : " (offline)";
                                        var promotedBy = admin.PromotedBy ?? "SYSTEM";
                                        var promotedDate = admin.PromotedDate?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
                                        Console.WriteLine($"{admin.Username + status,-20} {promotedBy,-15} {promotedDate,-20}");
                                    }
                                    Console.ResetColor();
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine("No admins currently assigned");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/channels":
                            lock (_consoleLock)
                            {
                                var allUsers = _userManager.GetAllUsers();
                                var channelList = new List<PrivateChannel>();
                                foreach (var user in allUsers)
                                {
                                    channelList.AddRange(_channelManager.GetUserChannels(user.Username));
                                }
                                
                                var uniqueChannels = channelList.DistinctBy(c => c.ChannelName).ToList();
                                
                                if (uniqueChannels.Count > 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine($"\nPrivate Channels ({uniqueChannels.Count}):");
                                    Console.WriteLine($"{"Channel Name",-25} {"Owner",-20} {"Members",-10}");
                                    Console.WriteLine(new string('-', 55));
                                    foreach (var channel in uniqueChannels)
                                    {
                                        Console.WriteLine($"{channel.ChannelName,-25} {channel.Owner,-20} {channel.Members.Count,-10}");
                                    }
                                    Console.ResetColor();
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkGray;
                                    Console.WriteLine("No private channels exist");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/channelinfo":
                            if (parts.Length > 1)
                            {
                                var channelName = parts[1].Trim();
                                var channel = _channelManager.GetChannel(channelName);

                                lock (_consoleLock)
                                {
                                    if (channel != null)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.WriteLine($"\nChannel: {channel.ChannelName}");
                                        Console.WriteLine($"  Owner: {channel.Owner}");
                                        Console.WriteLine($"  Created: {channel.CreatedDate:yyyy-MM-dd HH:mm:ss}");
                                        Console.WriteLine($"  Members ({channel.Members.Count}):");
                                        foreach (var member in channel.Members)
                                        {
                                            var status = _clients.ContainsKey(member) ? " (online)" : "";
                                            Console.WriteLine($"    - {member}{status}");
                                        }
                                        if (channel.InvitedUsers.Count > 0)
                                        {
                                            Console.WriteLine($"  Invited Users ({channel.InvitedUsers.Count}):");
                                            foreach (var invited in channel.InvitedUsers)
                                            {
                                                Console.WriteLine($"    - {invited}");
                                            }
                                        }
                                        Console.WriteLine($"  Total messages: {channel.MessageHistory.Count}");
                                        Console.ResetColor();
                                    }
                                    else
                                    {
                                        lock (_consoleLock)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Red;
                                            Console.WriteLine($"Channel '{channelName}' not found");
                                            Console.ResetColor();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                lock (_consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Usage: /channelinfo <channelname>");
                                    Console.ResetColor();
                                }
                            }
                            break;

                        case "/stop":
                            lock (_consoleLock)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Stopping server...");
                                Console.ResetColor();
                            }
                            Stop();
                            break;

                        default:
                            lock (_consoleLock)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Unknown command. Type /help for available commands.");
                                Console.ResetColor();
                            }
                            break;
                    }
                }
            });
        }

        private void ClearCurrentLine()
        {
            Console.Write("\r" + new string(' ', Console.BufferWidth - 1) + "\r");
        }

        private void RestoreCurrentLine()
        {
            if (!string.IsNullOrEmpty(_currentInput))
            {
                Console.Write(_currentInput);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            string? clientId = null;
            NetworkStream? stream = null;

            try
            {
                stream = client.GetStream();
                var buffer = new byte[4096];

                // Read authentication message
                var bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) return;

                var authJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                try
                {
                    var authMessage = JsonSerializer.Deserialize<ClientMessage>(authJson);

                    if (authMessage == null || string.IsNullOrWhiteSpace(authMessage.Body))
                    {
                        await SendJsonAsync(stream, "server", "Invalid authentication format.");
                        client.Close();
                        return;
                    }

                    clientId = authMessage.Body.Trim();
                    var password = authMessage.Password ?? "";
                    var isRegister = authMessage.Command?.ToLower() == "register";

                    // Validate username
                    if (string.IsNullOrWhiteSpace(clientId))
                    {
                        await SendJsonAsync(stream, "server", "Invalid username. Connection rejected.");
                        client.Close();
                        return;
                    }

                    // Handle registration
                    if (isRegister)
                    {
                        if (_userManager.UserExists(clientId))
                        {
                            await SendJsonAsync(stream, "server", "Username already exists. Please login instead.");
                            client.Close();
                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[Registration Failed] Username '{clientId}' already exists");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                            return;
                        }

                        if (_userManager.RegisterUser(clientId, password))
                        {
                            await SendJsonAsync(stream, "server", "Registration successful! You are now logged in.");
                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"[Registered] {clientId}");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                        }
                        else
                        {
                            await SendJsonAsync(stream, "server", "Registration failed.");
                            client.Close();
                            return;
                        }
                    }
                    else
                    {
                        // Handle login
                        if (!_userManager.UserExists(clientId))
                        {
                            await SendJsonAsync(stream, "server", "Username not found. Please register first.");
                            client.Close();
                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[Login Failed] Username '{clientId}' not found");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                            return;
                        }

                        if (!_userManager.AuthenticateUser(clientId, password))
                        {
                            await SendJsonAsync(stream, "server", "Incorrect password. Connection rejected.");
                            client.Close();
                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[Login Failed] Incorrect password for '{clientId}'");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                            return;
                        }

                        await SendJsonAsync(stream, "server", "Login successful!");
                    }
                }
                catch (JsonException)
                {
                    await SendJsonAsync(stream, "server", "Invalid authentication format.");
                    client.Close();
                    return;
                }

                // Check for duplicate connection
                if (_clients.ContainsKey(clientId))
                {
                    await SendJsonAsync(stream, "server", $"Username '{clientId}' is already connected. Connection rejected.");
                    client.Close();
                    lock (_consoleLock)
                    {
                        ClearCurrentLine();
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[Connection Rejected] Username '{clientId}' already connected");
                        Console.ResetColor();
                        RestoreCurrentLine();
                    }
                    return;
                }

                if (_clients.TryAdd(clientId, client))
                {
                    var account = _userManager.GetUserAccount(clientId);
                    lock (_consoleLock)
                    {
                        ClearCurrentLine();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[Connected] {clientId} (Messages: {account?.MessageCount ?? 0})");
                        Console.ResetColor();
                        RestoreCurrentLine();
                    }
                    await BroadcastJsonAsync("server", $"{clientId} joined the chat", null);

                    // Handle incoming messages
                    while (_isRunning && client.Connected)
                    {
                        bytesRead = await stream.ReadAsync(buffer);
                        if (bytesRead == 0) break;

                        var jsonString = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                        try
                        {
                            var message = JsonSerializer.Deserialize<ClientMessage>(jsonString);

                            if (message != null)
                            {
                                await ProcessMessageAsync(message, clientId, stream);
                            }
                        }
                        catch (JsonException ex)
                        {
                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[Error] Invalid JSON from {clientId}: {ex.Message}");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Error] Handling client {clientId}: {ex.Message}");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            finally
            {
                if (clientId != null && _clients.TryRemove(clientId, out _))
                {
                    var account = _userManager.GetUserAccount(clientId);
                    lock (_consoleLock)
                    {
                        ClearCurrentLine();
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[Disconnected] {clientId} (Total Messages: {account?.MessageCount ?? 0})");
                        Console.ResetColor();
                        RestoreCurrentLine();
                    }
                    await BroadcastJsonAsync("server", $"{clientId} left the chat", null);
                }
                stream?.Close();
                client.Close();
            }
        }

        private async Task ProcessMessageAsync(ClientMessage message, string clientId, NetworkStream stream)
        {
            var command = message.Command?.ToLower();

            if (command == "/say")
            {
                // Check if user is muted
                if (_userManager.IsUserMuted(clientId))
                {
                    await SendJsonAsync(stream, "command", "You are currently muted and cannot send messages.");
                    return;
                }

                _userManager.IncrementMessageCount(clientId);
                _userManager.AddMessageToHistory(clientId, message.Body ?? "");
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"{clientId}");
                    Console.ResetColor();
                    Console.WriteLine($": {message.Body}");
                    RestoreCurrentLine();
                }
                await BroadcastJsonAsync("message", message.Body ?? "", clientId, clientId);
            }
            else if (command == "/list")
            {
                var userList = string.Join(", ", _clients.Keys);
                var response = $"Connected users ({_clients.Count}): {userList}";
                await SendJsonAsync(stream, "command", response);
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested user list");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/msg")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var parts = message.Body.Split(' ', 2);
                    if (parts.Length >= 2)
                    {
                        var targetUsername = parts[0].Trim();
                        var privateMessage = parts[1];

                        if (_clients.TryGetValue(targetUsername, out var targetClient))
                        {
                            // Check if user is muted
                            if (_userManager.IsUserMuted(clientId))
                            {
                                await SendJsonAsync(stream, "command", "You are currently muted and cannot send messages.");
                                return;
                            }

                            _userManager.IncrementMessageCount(clientId);
                            _userManager.AddMessageToHistory(clientId, $"PM to {targetUsername}: {privateMessage}");
                            await SendJsonAsync(targetClient.GetStream(), "private", privateMessage, clientId);
                            // Play notification sound for private message
                            Console.Beep(800, 100);
                            await SendJsonAsync(stream, "privatesent", $"To {targetUsername}: {privateMessage}", clientId);
                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"[PM] {clientId} -> {targetUsername}: {privateMessage}");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                        }
                        else
                        {
                            await SendJsonAsync(stream, "command", $"User '{targetUsername}' not found");
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", "Usage: /msg <username> <message>");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /msg <username> <message>");
                }
            }
            else if (command == "/ping")
            {
                await SendJsonAsync(stream, "command", "Pong! Server is responsive.");
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} pinged the server");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/time")
            {
                var serverTime = DateTime.Now.ToString("HH:mm:ss");
                await SendJsonAsync(stream, "command", $"Server time: {serverTime}");
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested server time");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/uptime")
            {
                var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                await SendJsonAsync(stream, "command", $"Server uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested uptime");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/whoami")
            {
                var account = _userManager.GetUserAccount(clientId);
                var stats = new StringBuilder();
                stats.AppendLine($"You are logged in as: {clientId}");
                if (account != null)
                {
                    stats.AppendLine($"Total messages sent: {account.MessageCount}");
                    stats.AppendLine($"Account created: {account.CreatedDate:yyyy-MM-dd HH:mm:ss}");
                    stats.AppendLine($"Last login: {account.LastLogin:yyyy-MM-dd HH:mm:ss}");
                    stats.AppendLine($"Admin status: {(account.IsAdmin ? "Yes" : "No")}");
                }
                await SendJsonAsync(stream, "command", stats.ToString());
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} used whoami");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/me")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    // Check if user is muted
                    if (_userManager.IsUserMuted(clientId))
                    {
                        await SendJsonAsync(stream, "command", "You are currently muted and cannot send messages.");
                        return;
                    }

                    _userManager.IncrementMessageCount(clientId);
                    _userManager.AddMessageToHistory(clientId, $"* {message.Body}");
                    var actionMessage = $"* {clientId} {message.Body}";
                    await BroadcastJsonAsync("action", actionMessage, null);
                    lock (_consoleLock)
                    {
                        ClearCurrentLine();
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[Action] {actionMessage}");
                        Console.ResetColor();
                        RestoreCurrentLine();
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /me <action>");
                }
            }
            else if (command == "/exit")
            {
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested disconnect");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/help")
            {
                var account = _userManager.GetUserAccount(clientId);
                var helpText = new StringBuilder();
                helpText.AppendLine("Available commands:");
                helpText.AppendLine("  /help     - Show this help message");
                helpText.AppendLine("  /list     - List all connected users");
                helpText.AppendLine("  /msg <username> <message> - Send a private message to a user");
                helpText.AppendLine("  /me <action> - Send an action message (e.g., /me waves)");
                helpText.AppendLine("  /whoami   - Show your current username and stats");
                helpText.AppendLine("  /history [count] - View your message history");
                helpText.AppendLine("  /ping     - Check server responsiveness");
                helpText.AppendLine("  /time     - Show server time");
                helpText.AppendLine("  /uptime   - Show server uptime");
                helpText.AppendLine("  /say      - Sends a message to the server");
                helpText.AppendLine("  /exit     - Disconnect from the chat");
                helpText.AppendLine("\nChannel Commands:");
                helpText.AppendLine("  /createchannel <name> - Create a new private channel");
                helpText.AppendLine("  /invite <channel> <username> - Invite a user to your channel");
                helpText.AppendLine("  /joinchannel <name> - Join a channel you were invited to");
                helpText.AppendLine("  /leavechannel <name> - Leave a channel");
                helpText.AppendLine("  /deletechannel <name> - Delete a channel you own");
                helpText.AppendLine("  /channelmsg <channel> <message> - Send message to a channel");
                helpText.AppendLine("  /mychannels - List your channels");
                helpText.AppendLine("  /channelinvites - List your channel invitations");
                helpText.AppendLine("  /channelmembers <name> - List members of a channel");

                // Add admin commands if user is an admin
                if (account?.IsAdmin == true)
                {
                    helpText.AppendLine("\nAdmin Commands:");
                    helpText.AppendLine("  /adminkick <username> - Kick a user from the server");
                    helpText.AppendLine("  /adminmute <username> <minutes> - Mute a user");
                    helpText.AppendLine("  /adminunmute <username> - Unmute a user");
                    helpText.AppendLine("  /adminbroadcast <message> - Send an admin broadcast");
                    helpText.Append("  /adminlist - List all server admins");
                }

                await SendJsonAsync(stream, "command", helpText.ToString());
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested help");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/history")
            {
                int count = 10;
                if (!string.IsNullOrWhiteSpace(message.Body) && int.TryParse(message.Body.Trim(), out int requestedCount))
                {
                    count = Math.Min(requestedCount, 50); // Cap at 50 messages
                }

                var history = _userManager.GetMessageHistory(clientId, count);
                var response = new StringBuilder();

                if (history.Count > 0)
                {
                    response.AppendLine($"Your message history (last {history.Count} messages):");
                    foreach (var msg in history)
                    {
                        response.AppendLine(msg);
                    }
                }
                else
                {
                    response.Append("No message history found.");
                }

                await SendJsonAsync(stream, "command", response.ToString());
                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested message history");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            // ADMIN COMMANDS FOR CLIENTS
            else if (command == "/adminkick")
            {
                if (!_userManager.IsAdmin(clientId))
                {
                    await SendJsonAsync(stream, "command", "You must be an admin to use this command.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var username = message.Body.Trim();
                    if (_clients.TryGetValue(username, out var targetClient))
                    {
                        await SendJsonAsync(targetClient.GetStream(), "server", $"You have been kicked by admin {clientId}.");
                        targetClient.Close();
                        _clients.TryRemove(username, out _);

                        await SendJsonAsync(stream, "command", $"Successfully kicked {username}");
                        await BroadcastJsonAsync("server", $"{username} was kicked by admin {clientId}", null);

                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"[Admin Kick] {clientId} kicked {username}");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"User '{username}' not found or not connected");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /adminkick <username>");
                }
            }
            else if (command == "/adminmute")
            {
                if (!_userManager.IsAdmin(clientId))
                {
                    await SendJsonAsync(stream, "command", "You must be an admin to use this command.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var parts = message.Body.Split(' ', 2);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int minutes))
                    {
                        var username = parts[0].Trim();
                        if (_userManager.UserExists(username))
                        {
                            _userManager.MuteUser(username, minutes);
                            await SendJsonAsync(stream, "command", $"Muted {username} for {minutes} minutes");

                            if (_clients.ContainsKey(username))
                            {
                                await SendJsonAsync(_clients[username].GetStream(), "server",
                                    $"You have been muted for {minutes} minutes by admin {clientId}");
                            }

                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[Admin Mute] {clientId} muted {username} for {minutes} minutes");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                        }
                        else
                        {
                            await SendJsonAsync(stream, "command", $"User '{username}' not found");
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", "Usage: /adminmute <username> <minutes>");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /adminmute <username> <minutes>");
                }
            }
            else if (command == "/adminunmute")
            {
                if (!_userManager.IsAdmin(clientId))
                {
                    await SendJsonAsync(stream, "command", "You must be an admin to use this command.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var username = message.Body.Trim();
                    if (_userManager.UserExists(username))
                    {
                        _userManager.UnmuteUser(username);
                        await SendJsonAsync(stream, "command", $"Unmuted {username}");

                        if (_clients.ContainsKey(username))
                        {
                            await SendJsonAsync(_clients[username].GetStream(), "server",
                                $"You have been unmuted by admin {clientId}");
                        }

                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Admin Unmute] {clientId} unmuted {username}");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"User '{username}' not found");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /adminunmute <username>");
                }
            }
            else if (command == "/adminbroadcast")
            {
                if (!_userManager.IsAdmin(clientId))
                {
                    await SendJsonAsync(stream, "command", "You must be an admin to use this command.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    await BroadcastJsonAsync("server", $"[Admin {clientId}] {message.Body}", null);
                    await SendJsonAsync(stream, "command", "Broadcast sent to all users");

                    lock (_consoleLock)
                    {
                        ClearCurrentLine();
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"[Admin Broadcast] {clientId}: {message.Body}");
                        Console.ResetColor();
                        RestoreCurrentLine();
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /adminbroadcast <message>");
                }
            }
            else if (command == "/adminlist")
            {
                if (!_userManager.IsAdmin(clientId))
                {
                    await SendJsonAsync(stream, "command", "You must be an admin to use this command.");
                    return;
                }

                var adminList = _userManager.GetAdmins();
                var response = new StringBuilder();

                if (adminList.Count > 0)
                {
                    response.AppendLine($"Server Admins ({adminList.Count}):");
                    foreach (var admin in adminList)
                    {
                        var status = _clients.ContainsKey(admin.Username) ? " (online)" : " (offline)";
                        response.AppendLine($"  {admin.Username}{status}");
                    }
                }
                else
                {
                    response.Append("No admins currently assigned");
                }

                await SendJsonAsync(stream, "command", response.ToString());

                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested admin list");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            // CHANNEL COMMANDS
            else if (command == "/createchannel")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var channelName = message.Body.Trim();
                    
                    if (_channelManager.CreateChannel(channelName, clientId))
                    {
                        await SendJsonAsync(stream, "command", $"Channel '{channelName}' created successfully! You are the owner.");
                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Channel] {clientId} created channel '{channelName}'");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"Failed to create channel. Channel '{channelName}' may already exist.");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /createchannel <channelname>");
                }
            }
            else if (command == "/invite")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var parts = message.Body.Split(' ', 2);
                    if (parts.Length >= 2)
                    {
                        var channelName = parts[0].Trim();
                        var inviteUsername = parts[1].Trim();

                        if (!_userManager.UserExists(inviteUsername))
                        {
                            await SendJsonAsync(stream, "command", $"User '{inviteUsername}' not found.");
                            return;
                        }

                        if (_channelManager.InviteToChannel(channelName, inviteUsername, clientId))
                        {
                            await SendJsonAsync(stream, "command", $"Invited {inviteUsername} to channel '{channelName}'");
                            
                            if (_clients.ContainsKey(inviteUsername))
                            {
                                await SendJsonAsync(_clients[inviteUsername].GetStream(), "server", 
                                    $"{clientId} invited you to join channel '{channelName}'. Use /joinchannel {channelName} to join.");
                            }

                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"[Channel] {clientId} invited {inviteUsername} to '{channelName}'");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                        }
                        else
                        {
                            await SendJsonAsync(stream, "command", $"Failed to invite user. Check if channel exists and you are a member.");
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", "Usage: /invite <channelname> <username>");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /invite <channelname> <username>");
                }
            }
            else if (command == "/joinchannel")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var channelName = message.Body.Trim();
                    
                    if (_channelManager.JoinChannel(channelName, clientId))
                    {
                        await SendJsonAsync(stream, "command", $"Successfully joined channel '{channelName}'");
                        
                        var channel = _channelManager.GetChannel(channelName);
                        if (channel != null)
                        {
                            foreach (var member in channel.Members)
                            {
                                if (member != clientId && _clients.ContainsKey(member))
                                {
                                    await SendJsonAsync(_clients[member].GetStream(), "channel", 
                                        $"{clientId} joined the channel", channelName);
                                }
                            }
                        }

                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Channel] {clientId} joined channel '{channelName}'");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"Failed to join channel. You may not be invited or channel doesn't exist.");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /joinchannel <channelname>");
                }
            }
            else if (command == "/leavechannel")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var channelName = message.Body.Trim();
                    
                    if (_channelManager.LeaveChannel(channelName, clientId))
                    {
                        await SendJsonAsync(stream, "command", $"Left channel '{channelName}'");
                        
                        var channel = _channelManager.GetChannel(channelName);
                        if (channel != null)
                        {
                            foreach (var member in channel.Members)
                            {
                                if (_clients.ContainsKey(member))
                                {
                                    await SendJsonAsync(_clients[member].GetStream(), "channel", 
                                        $"{clientId} left the channel", channelName);
                                }
                            }
                        }

                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"[Channel] {clientId} left channel '{channelName}'");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"Failed to leave channel. You may be the owner or not a member.");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /leavechannel <channelname>");
                }
            }
            else if (command == "/deletechannel")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var channelName = message.Body.Trim();
                    
                    if (_channelManager.DeleteChannel(channelName, clientId))
                    {
                        await SendJsonAsync(stream, "command", $"Channel '{channelName}' deleted successfully");
                        
                        lock (_consoleLock)
                        {
                            ClearCurrentLine();
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[Channel] {clientId} deleted channel '{channelName}'");
                            Console.ResetColor();
                            RestoreCurrentLine();
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"Failed to delete channel. You must be the owner.");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /deletechannel <channelname>");
                }
            }
            else if (command == "/channelmsg")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var parts = message.Body.Split(' ', 2);
                    if (parts.Length >= 2)
                    {
                        var channelName = parts[0].Trim();
                        var channelMessage = parts[1];

                        var channel = _channelManager.GetChannel(channelName);
                        if (channel != null && channel.IsMember(clientId))
                        {
                            // Check if user is muted
                            if (_userManager.IsUserMuted(clientId))
                            {
                                await SendJsonAsync(stream, "command", "You are currently muted and cannot send messages.");
                                return;
                            }

                            _userManager.IncrementMessageCount(clientId);
                            _channelManager.AddMessageToChannel(channelName, clientId, channelMessage);

                            foreach (var member in channel.Members)
                            {
                                if (_clients.ContainsKey(member))
                                {
                                    await SendJsonAsync(_clients[member].GetStream(), "channelmessage", 
                                        channelMessage, $"{clientId}@{channelName}");
                                }
                            }

                            lock (_consoleLock)
                            {
                                ClearCurrentLine();
                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.WriteLine($"[{channelName}] {clientId}: {channelMessage}");
                                Console.ResetColor();
                                RestoreCurrentLine();
                            }
                        }
                        else
                        {
                            await SendJsonAsync(stream, "command", $"You are not a member of channel '{channelName}'");
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", "Usage: /channelmsg <channelname> <message>");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /channelmsg <channelname> <message>");
                }
            }
            else if (command == "/mychannels")
            {
                var userChannels = _channelManager.GetUserChannels(clientId);
                var response = new StringBuilder();

                if (userChannels.Count > 0)
                {
                    response.AppendLine($"Your channels ({userChannels.Count}):");
                    foreach (var channel in userChannels)
                    {
                        var ownerTag = channel.Owner == clientId ? " [OWNER]" : "";
                        response.AppendLine($"  {channel.ChannelName}{ownerTag} - {channel.Members.Count} members");
                    }
                }
                else
                {
                    response.Append("You are not a member of any channels. Use /createchannel to create one.");
                }

                await SendJsonAsync(stream, "command", response.ToString());

                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested channel list");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/channelinvites")
            {
                var invitations = _channelManager.GetUserInvitations(clientId);
                var response = new StringBuilder();

                if (invitations.Count > 0)
                {
                    response.AppendLine($"Channel invitations ({invitations.Count}):");
                    foreach (var channel in invitations)
                    {
                        response.AppendLine($"  {channel.ChannelName} - owned by {channel.Owner}");
                    }
                    response.Append("Use /joinchannel <channelname> to join");
                }
                else
                {
                    response.Append("No pending channel invitations");
                }

                await SendJsonAsync(stream, "command", response.ToString());

                lock (_consoleLock)
                {
                    ClearCurrentLine();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[Command] {clientId} requested channel invitations");
                    Console.ResetColor();
                    RestoreCurrentLine();
                }
            }
            else if (command == "/channelmembers")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var channelName = message.Body.Trim();
                    var channel = _channelManager.GetChannel(channelName);

                    if (channel != null && channel.IsMember(clientId))
                    {
                        var response = new StringBuilder();
                        response.AppendLine($"Members of '{channelName}' ({channel.Members.Count}):");
                        foreach (var member in channel.Members)
                        {
                            var status = _clients.ContainsKey(member) ? " (online)" : " (offline)";
                            var ownerTag = member == channel.Owner ? " [OWNER]" : "";
                            response.AppendLine($"  {member}{ownerTag}{status}");
                        }

                        await SendJsonAsync(stream, "command", response.ToString());
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", $"Channel '{channelName}' not found or you are not a member.");
                    }

                    lock (_consoleLock)
                    {
                        ClearCurrentLine();
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"[Command] {clientId} requested channel members");
                        Console.ResetColor();
                        RestoreCurrentLine();
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /channelmembers <channelname>");
                }
            }
            else
            {
                await SendJsonAsync(stream, "command", "Unknown command. Type /help for available commands.");
            }
        }

        private async Task SendJsonAsync(NetworkStream stream, string type, string body, string? username = null)
        {
            var response = new ServerMessage
            {
                Type = type,
                Body = body,
                Username = username
            };

            var jsonString = JsonSerializer.Serialize(response) + "\n";
            var jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            try
            {
                await stream.WriteAsync(jsonBytes);
            }
            catch
            {
                // Client disconnected
            }
        }

        private async Task BroadcastJsonAsync(string type, string body, string? excludeClientId, string? username = null)
        {
            var response = new ServerMessage
            {
                Type = type,
                Body = body,
                Username = username
            };

            var jsonString = JsonSerializer.Serialize(response) + "\n";
            var jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            var tasks = _clients
                .Where(kvp => kvp.Key != excludeClientId)
                .Select(async kvp =>
                {
                    try
                    {
                        var stream = kvp.Value.GetStream();
                        await stream.WriteAsync(jsonBytes);
                    }
                    catch
                    {
                        // Client disconnected, will be cleaned up by HandleClientAsync
                    }
                });

            await Task.WhenAll(tasks);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();

            foreach (var client in _clients.Values)
            {
                client.Close();
            }
            _clients.Clear();
        }
    }
}
