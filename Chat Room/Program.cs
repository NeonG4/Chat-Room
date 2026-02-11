namespace Chat_Room
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Chat Room Application ===");
            Console.WriteLine("1. Start Server");
            Console.WriteLine("2. Connect as Client");
            Console.Write("\nSelect option (1 or 2): ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await StartServer();
                    break;
                case "2":
                    await StartClient();
                    break;
                default:
                    Console.WriteLine("Invalid option. Exiting...");
                    break;
            }
        }

        static async Task StartServer()
        {
            Console.Write("Enter port (default 5000): ");
            var portInput = Console.ReadLine();
            int port = string.IsNullOrWhiteSpace(portInput) ? 5000 : int.Parse(portInput);

            var server = new ChatServer(port);
            
            Console.WriteLine("Press Ctrl+C to stop the server");
            
            var serverTask = server.Start();
            
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                server.Stop();
                Console.WriteLine("\nServer stopped");
            };

            await serverTask;
        }

        static async Task StartClient()
        {
            Console.Write("Enter your username: ");
            var username = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Username cannot be empty");
                return;
            }

            Console.Write("Do you have an account? (y/n): ");
            var hasAccount = Console.ReadLine()?.ToLower() == "y";

            Console.Write("Enter your password: ");
            var password = ReadPassword();
            Console.WriteLine();

            if (!hasAccount)
            {
                Console.Write("Confirm password: ");
                var confirmPassword = ReadPassword();
                Console.WriteLine();

                if (password != confirmPassword)
                {
                    Console.WriteLine("Passwords do not match!");
                    return;
                }
            }

            Console.Write("Enter server IP address (default localhost): ");
            var serverIp = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(serverIp))
                serverIp = "localhost";

            Console.Write("Enter server port (default 5000): ");
            var portInput = Console.ReadLine();
            int port = string.IsNullOrWhiteSpace(portInput) ? 5000 : int.Parse(portInput);

            var client = new ChatClient(username, password);
            await client.ConnectAsync(serverIp, port, !hasAccount);
            
            if (client.GetType().GetField("_isConnected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(client) as bool? == true)
            {
                await client.StartChatAsync();
            }
        }

        static string ReadPassword()
        {
            var password = "";
            ConsoleKeyInfo key;
            
            do
            {
                key = Console.ReadKey(true);
                
                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);
            
            return password;
        }
    }
}
