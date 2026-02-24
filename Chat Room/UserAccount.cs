namespace Chat_Room
{
    internal class UserAccount
    {
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public int MessageCount { get; set; } = 0;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastLogin { get; set; } = DateTime.Now;
        public bool IsMuted { get; set; } = false;
        public DateTime? MutedUntil { get; set; } = null;
        public List<string> MessageHistory { get; set; } = new();
        public bool IsAdmin { get; set; } = false;
        public string? PromotedBy { get; set; } = null;
        public DateTime? PromotedDate { get; set; } = null;
    }
}
