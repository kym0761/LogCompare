namespace LogLibrary.Services
{
    public class LogUploadResult
    {
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public int UploadedCount { get; set; }
    }
}
