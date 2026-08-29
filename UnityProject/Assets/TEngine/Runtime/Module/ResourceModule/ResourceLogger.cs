namespace TEngine
{
    internal class ResourceLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            TEngine.Log.Info(message);
        }

        public void LogWarning(string message)
        {
            TEngine.Log.Warning(message);
        }

        public void LogError(string message)
        {
            TEngine.Log.Error(message);
        }

        public void LogException(System.Exception exception)
        {
            TEngine.Log.Fatal(exception.Message);
        }
    }
}
