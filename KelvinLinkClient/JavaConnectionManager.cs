using RedLoader;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace KelvinLinkClient
{
    public class JavaConnectionManager : MonoBehaviour
    {
        private TcpClient client;
        private NetworkStream stream;
        private Thread receiveThread;
        private bool isConnected = false;
        private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
        private CancellationTokenSource cancellationTokenSource;

        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        void Update()
        {
            ProcessIncomingMessages();
        }

        public async Task<bool> ConnectAsync(string ip, int port, float timeoutSeconds = 5f)
        {
            try
            {
                cancellationTokenSource = new CancellationTokenSource();
                client = new TcpClient();

                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    client?.Close();
                    throw new TimeoutException($"Connection timeout after {timeoutSeconds} seconds");
                }

                stream = client.GetStream();
                isConnected = true;

                receiveThread = new Thread(ReceiveMessages) { IsBackground = true };
                receiveThread.Start();

                RLog.Msg($"[KelvinLink] Połączono z backendem Java {ip}:{port}");
                OnConnected?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                RLog.Error($"[KelvinLink] Błąd połączenia z Java: {ex.Message}");
                isConnected = false;
                return false;
            }
        }

        public void SendMessage(string message)
        {
            if (!isConnected || stream == null || !stream.CanWrite) return;

            try
            {
                string jsonPacket = CreateJsonPacket(message, "game_event");
                byte[] data = Encoding.UTF8.GetBytes(jsonPacket + "\n");
                stream.Write(data, 0, data.Length);
                RLog.Msg($"[KelvinLink] Wysłano: {message}");
            }
            catch (Exception ex)
            {
                RLog.Error($"[KelvinLink] Błąd wysyłania: {ex.Message}");
                HandleConnectionLost();
            }
        }

        public void SendRawMessage(string message)
        {
            if (!isConnected || stream == null || !stream.CanWrite) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(data, 0, data.Length);
                RLog.Msg($"[KelvinLink] Wysłano surową wiadomość: {message}");
            }
            catch (Exception ex)
            {
                RLog.Error($"[KelvinLink] Błąd wysyłania surowej wiadomości: {ex.Message}");
                HandleConnectionLost();
            }
        }

        // Ręczne budowanie JSON-a - unikamy problemów z IL2CPP
        private string CreateJsonPacket(string messageData, string messageType)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string playerId = EscapeJsonString(SystemInfo.deviceUniqueIdentifier);
            string escapedData = EscapeJsonString(messageData);
            string escapedType = EscapeJsonString(messageType);

            return $"{{\"timestamp\":{timestamp},\"type\":\"{escapedType}\",\"data\":\"{escapedData}\",\"playerId\":\"{playerId}\"}}";
        }

        private string CreatePositionJson(Vector3 position, Vector3 rotation)
        {
            return $"{{\"x\":{position.x:F3},\"y\":{position.y:F3},\"z\":{position.z:F3},\"rotX\":{rotation.x:F3},\"rotY\":{rotation.y:F3},\"rotZ\":{rotation.z:F3}}}";
        }

        private string CreateGameStateJson()
        {
            string gameVersion = EscapeJsonString(Application.version);
            string playerName = EscapeJsonString(SystemInfo.deviceName);
            string sceneName = EscapeJsonString(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            string deviceInfo = EscapeJsonString(SystemInfo.operatingSystem);

            return $"{{\"gameVersion\":\"{gameVersion}\",\"playerName\":\"{playerName}\",\"gameTime\":{Time.time:F2},\"scene\":\"{sceneName}\",\"deviceInfo\":\"{deviceInfo}\"}}";
        }

        private string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            return input.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\n", "\\n")
                       .Replace("\r", "\\r")
                       .Replace("\t", "\\t");
        }

        private void ReceiveMessages()
        {
            byte[] buffer = new byte[4096];
            StringBuilder messageBuilder = new StringBuilder();

            while (isConnected && client?.Connected == true)
            {
                try
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested) break;

                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        messageBuilder.Append(chunk);

                        string fullMessage = messageBuilder.ToString();
                        string[] messages = fullMessage.Split('\n');

                        for (int i = 0; i < messages.Length - 1; i++)
                        {
                            if (!string.IsNullOrEmpty(messages[i].Trim()))
                            {
                                messageQueue.Enqueue(messages[i].Trim());
                            }
                        }

                        messageBuilder.Clear();
                        if (!string.IsNullOrEmpty(messages[messages.Length - 1]))
                        {
                            messageBuilder.Append(messages[messages.Length - 1]);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    RLog.Error($"[KelvinLink] Błąd odbierania: {ex.Message}");
                    break;
                }
            }

            HandleConnectionLost();
        }

        private void ProcessIncomingMessages()
        {
            while (messageQueue.TryDequeue(out string message))
            {
                try
                {
                    ProcessServerMessage(message);
                    OnMessageReceived?.Invoke(message);
                }
                catch (Exception ex)
                {
                    RLog.Error($"[KelvinLink] Błąd przetwarzania wiadomości: {ex.Message}");
                }
            }
        }

        private void ProcessServerMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                if (message.StartsWith("{") && message.EndsWith("}"))
                {
                    string type = ExtractJsonValue(message, "type");
                    string data = ExtractJsonValue(message, "data");

                    if (!string.IsNullOrEmpty(type))
                    {
                        switch (type)
                        {
                            case "player_update":
                                HandlePlayerUpdate(data);
                                break;
                            case "world_event":
                                HandleWorldEvent(data);
                                break;
                            case "server_command":
                                HandleServerCommand(data);
                                break;
                            default:
                                RLog.Msg($"[KelvinLink] Nieznany typ pakietu: {type}");
                                break;
                        }
                        return;
                    }
                }

                RLog.Msg($"[KelvinLink] Otrzymano surową wiadomość: {message}");
            }
            catch (Exception ex)
            {
                RLog.Error($"[KelvinLink] Błąd parsowania wiadomości: {ex.Message}");
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            string searchPattern = $"\"{key}\":\"";
            int startIndex = json.IndexOf(searchPattern);
            if (startIndex == -1) return null;

            startIndex += searchPattern.Length;
            int endIndex = json.IndexOf("\"", startIndex);
            if (endIndex == -1) return null;

            return json.Substring(startIndex, endIndex - startIndex);
        }

        private void HandlePlayerUpdate(string data)
        {
            RLog.Msg($"[KelvinLink] Player update: {data}");
        }

        private void HandleWorldEvent(string data)
        {
            RLog.Msg($"[KelvinLink] World event: {data}");
        }

        private void HandleServerCommand(string data)
        {
            RLog.Msg($"[KelvinLink] Server command: {data}");
        }

        private void HandleConnectionLost()
        {
            if (!isConnected) return;

            isConnected = false;
            OnDisconnected?.Invoke();
            RLog.Msg("[KelvinLink] Utracono połączenie z serwerem Java");
        }

        public void SendPlayerPosition(Vector3 position, Vector3 rotation)
        {
            string positionJson = CreatePositionJson(position, rotation);
            SendRawMessage($"PLAYER_POSITION:{positionJson}");
        }

        public void SendGameEvent(string eventType, string eventData)
        {
            string escapedEventType = EscapeJsonString(eventType);
            string escapedEventData = EscapeJsonString(eventData);
            SendRawMessage($"GAME_EVENT:{escapedEventType}:{escapedEventData}");
        }

        public void SendGameState()
        {
            string gameStateJson = CreateGameStateJson();
            SendRawMessage($"GAME_STATE:{gameStateJson}");
        }

        public void SendCustomPacket(string packetType, string data)
        {
            string jsonPacket = CreateJsonPacket(data, packetType);
            SendRawMessage(jsonPacket);
        }

        public bool IsConnected()
        {
            return isConnected && client?.Connected == true;
        }

        public void Disconnect()
        {
            isConnected = false;
            cancellationTokenSource?.Cancel();

            try
            {
                receiveThread?.Join(1000);
            }
            catch { }

            stream?.Close();
            client?.Close();

            client = null;
            stream = null;
            receiveThread = null;

            RLog.Msg("[KelvinLink] Rozłączono z backendem Java");
        }

        void OnDestroy()
        {
            Disconnect();
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && IsConnected())
            {
                SendRawMessage("PLAYER_STATUS:PAUSED");
            }
            else if (!pauseStatus && IsConnected())
            {
                SendRawMessage("PLAYER_STATUS:RESUMED");
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && IsConnected())
            {
                SendRawMessage("PLAYER_STATUS:UNFOCUSED");
            }
            else if (hasFocus && IsConnected())
            {
                SendRawMessage("PLAYER_STATUS:FOCUSED");
            }
        }
    }
}