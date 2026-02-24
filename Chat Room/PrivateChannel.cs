namespace Chat_Room
{
    internal class PrivateChannel
    {
        public string ChannelName { get; set; } = "";
        public string Owner { get; set; } = "";
        public HashSet<string> Members { get; set; } = new();
        public HashSet<string> InvitedUsers { get; set; } = new();
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public List<string> MessageHistory { get; set; } = new();

        public PrivateChannel()
        {
        }

        public PrivateChannel(string channelName, string owner)
        {
            ChannelName = channelName;
            Owner = owner;
            Members.Add(owner);
            CreatedDate = DateTime.Now;
        }

        public bool IsMember(string username)
        {
            return Members.Contains(username);
        }

        public bool IsInvited(string username)
        {
            return InvitedUsers.Contains(username);
        }

        public bool InviteUser(string username)
        {
            if (Members.Contains(username))
                return false;

            return InvitedUsers.Add(username);
        }

        public bool AddMember(string username)
        {
            InvitedUsers.Remove(username);
            return Members.Add(username);
        }

        public bool RemoveMember(string username)
        {
            if (username == Owner)
                return false;

            return Members.Remove(username);
        }

        public void AddMessage(string username, string message)
        {
            MessageHistory.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {username}: {message}");
            
            if (MessageHistory.Count > 100)
            {
                MessageHistory.RemoveAt(0);
            }
        }
    }
}
