namespace LogWorker.Interfaces;

public interface IPromptManager
{
    void LoadPrompts();
    string GetSystemPrompt();
    string GetPrompt(string modelName, string command, string deviceName);
    string GetSummaryPrompt(string modelName, string command, string deviceName);
    string GetDiagnosisPrompt(string modelName, string command, string deviceName, string dateStr, string severity, string summaryText);
}
