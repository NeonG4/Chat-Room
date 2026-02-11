using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chat_Room
{
    internal class UserManager
    {
        private readonly string _userDataFile = "users.json";
        private Dictionary<string, UserAccount> _users = new();
        private readonly object _fileLock = new object();

        public UserManager()
        {
            LoadUsers();
        }

        private void LoadUsers()
        {
            lock (_fileLock)
            {
                if (File.Exists(_userDataFile))
                {
                    try
                    {
                        var json = File.ReadAllText(_userDataFile);
                        _users = JsonSerializer.Deserialize<Dictionary<string, UserAccount>>(json) ?? new();
                    }
                    catch
                    {
                        _users = new Dictionary<string, UserAccount>();
                    }
                }
            }
        }

        private void SaveUsers()
        {
            lock (_fileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_userDataFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving users: {ex.Message}");
                }
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        public bool RegisterUser(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            if (_users.ContainsKey(username))
                return false;

            var account = new UserAccount
            {
                Username = username,
                PasswordHash = HashPassword(password),
                MessageCount = 0,
                CreatedDate = DateTime.Now,
                LastLogin = DateTime.Now
            };

            _users[username] = account;
            SaveUsers();
            return true;
        }

        public bool AuthenticateUser(string username, string password)
        {
            if (!_users.ContainsKey(username))
                return false;

            var user = _users[username];
            var inputHash = HashPassword(password);

            if (user.PasswordHash == inputHash)
            {
                user.LastLogin = DateTime.Now;
                SaveUsers();
                return true;
            }

            return false;
        }

        public void IncrementMessageCount(string username)
        {
            if (_users.ContainsKey(username))
            {
                _users[username].MessageCount++;
                SaveUsers();
            }
        }

        public int GetMessageCount(string username)
        {
            return _users.ContainsKey(username) ? _users[username].MessageCount : 0;
        }

        public UserAccount? GetUserAccount(string username)
        {
            return _users.ContainsKey(username) ? _users[username] : null;
        }

        public bool UserExists(string username)
        {
            return _users.ContainsKey(username);
        }

        public int GetTotalUsers()
        {
            return _users.Count;
        }

        public List<UserAccount> GetAllUsers()
        {
            return _users.Values.ToList();
        }
    }
}
