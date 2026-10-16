using System;

namespace AlgoTrading.ViewModels
{
    public class StatusMessage
    {
        public string Title { get; set; }
        public string Details { get; set; }
        public DateTime Time { get; set; }
        public StatusMessageType Type { get; set; }

        public StatusMessage(string title, string details, StatusMessageType type = StatusMessageType.Info)
        {
            Title = title ?? string.Empty;
            Details = details ?? string.Empty;
            Type = type;
            Time = DateTime.Now;
        }
    }

    public enum StatusMessageType
    {
        Info,
        Warning,
        Error
    }
}
