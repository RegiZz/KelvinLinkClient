using KelvinLinkClient;
using RedLoader;
using RedLoader.Unity.IL2CPP.Utils;
using SonsSdk;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KelvinLinkMod
{
    public class KelvinLinkClient : SonsMod
    {
        private GameObject helperObject;
        private KelvinLinkHelper helperComponent;
        private UnityAction<Scene, LoadSceneMode> sceneLoadedAction;
        private GameObject serverMenuPanel;
        private JavaConnectionManager javaBridge;
        private bool isAutoReconnectEnabled = true;
        private float reconnectInterval = 5f;
        private Coroutine gameStateUpdateCoroutine;

        private List<ServerInfo> availableServers = new List<ServerInfo>
        {
            new ServerInfo
            {
                Name = "KelvinLink EU #1",
                IP = "192.168.1.100",
                Port = 7777,
                JavaPort = 25565,
                Players = "2/8",
                Ping = 45,
                HasPassword = false,
                ServerType = "PvE",
                Description = "European server with PvE gameplay"
            },
            new ServerInfo
            {
                Name = "KelvinLink US West",
                IP = "10.0.0.50",
                Port = 7778,
                JavaPort = 25565,
                Players = "5/8",
                Ping = 120,
                HasPassword = true,
                ServerType = "PvP",
                Description = "US West Coast PvP server"
            },
            new ServerInfo
            {
                Name = "Private Server",
                IP = "172.16.1.25",
                Port = 7779,
                JavaPort = 25565,
                Players = "1/4",
                Ping = 23,
                HasPassword = false,
                ServerType = "Private",
                Description = "Private testing server"
            },
            new ServerInfo
            {
                Name = "KelvinLink Asia",
                IP = "203.0.113.42",
                Port = 7780,
                JavaPort = 25565,
                Players = "7/8",
                Ping = 200,
                HasPassword = false,
                ServerType = "PvP",
                Description = "Asian region PvP server"
            }
        };

        private ServerInfo currentServer;

        protected override void OnInitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod zainicjalizowany");

            helperObject = new GameObject("KelvinLinkHelper");
            UnityEngine.Object.DontDestroyOnLoad(helperObject);
            helperComponent = helperObject.AddComponent<KelvinLinkHelper>();

            javaBridge = helperObject.AddComponent<JavaConnectionManager>();
            SetupConnectionEvents();

            // Poprawione dla IL2CPP - używamy wzorca (UnityAction)new System.Action
            sceneLoadedAction = (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded);
            SceneManager.sceneLoaded += sceneLoadedAction;
        }

        private void SetupConnectionEvents()
        {
            // Poprawione dla IL2CPP - używamy wzorca (Action)new System.Action
            javaBridge.OnConnected += (System.Action)new System.Action(() =>
            {
                RLog.Msg("[KelvinLink] Successfully connected to Java backend");
                SendInitialGameState();
                StartGameStateUpdates();
            });

            javaBridge.OnDisconnected += (System.Action)new System.Action(() =>
            {
                RLog.Msg("[KelvinLink] Disconnected from Java backend");
                StopGameStateUpdates();

                if (isAutoReconnectEnabled && currentServer != null)
                {
                    helperComponent.StartCoroutine(AutoReconnectCoroutine());
                }
            });

            javaBridge.OnMessageReceived += (System.Action<string>)new System.Action<string>((message) =>
            {
                HandleServerMessage(message);
            });
        }

        private void SendInitialGameState()
        {
            javaBridge.SendGameEvent("PLAYER_JOIN", SystemInfo.deviceName);
            javaBridge.SendGameState();
        }

        private void StartGameStateUpdates()
        {
            if (gameStateUpdateCoroutine != null)
            {
                helperComponent.StopCoroutine(gameStateUpdateCoroutine);
            }
            gameStateUpdateCoroutine = helperComponent.StartCoroutine(GameStateUpdateCoroutine());
        }

        private void StopGameStateUpdates()
        {
            if (gameStateUpdateCoroutine != null)
            {
                helperComponent.StopCoroutine(gameStateUpdateCoroutine);
                gameStateUpdateCoroutine = null;
            }
        }

        private IEnumerator GameStateUpdateCoroutine()
        {
            while (javaBridge.IsConnected())
            {
                yield return new WaitForSeconds(30f);

                if (javaBridge.IsConnected())
                {
                    javaBridge.SendGameState();
                }
            }
        }

        private void HandleServerMessage(string message)
        {
            if (message.StartsWith("SPAWN_ITEM:"))
            {
                string itemData = message.Substring("SPAWN_ITEM:".Length);
                HandleSpawnItem(itemData);
            }
            else if (message.StartsWith("WEATHER_CHANGE:"))
            {
                string weatherData = message.Substring("WEATHER_CHANGE:".Length);
                HandleWeatherChange(weatherData);
            }
            else if (message.StartsWith("SERVER_MESSAGE:"))
            {
                string serverMsg = message.Substring("SERVER_MESSAGE:".Length);
                HandleServerMessage_Display(serverMsg);
            }
            else if (message.StartsWith("PLAYER_TELEPORT:"))
            {
                string teleportData = message.Substring("PLAYER_TELEPORT:".Length);
                HandlePlayerTeleport(teleportData);
            }
        }

        private void HandleSpawnItem(string itemData)
        {
            RLog.Msg($"[KelvinLink] Spawning item: {itemData}");
        }

        private void HandleWeatherChange(string weatherData)
        {
            RLog.Msg($"[KelvinLink] Weather change: {weatherData}");
        }

        private void HandleServerMessage_Display(string message)
        {
            RLog.Msg($"[KelvinLink] Server message: {message}");
        }

        private void HandlePlayerTeleport(string teleportData)
        {
            RLog.Msg($"[KelvinLink] Player teleport: {teleportData}");
        }

        private IEnumerator AutoReconnectCoroutine()
        {
            yield return new WaitForSeconds(reconnectInterval);

            if (!javaBridge.IsConnected() && currentServer != null)
            {
                RLog.Msg("[KelvinLink] Attempting to reconnect...");
                ConnectToServerAsync(currentServer);
            }
        }

        private async void ConnectToServerAsync(ServerInfo server)
        {
            currentServer = server;
            RLog.Msg($"[KelvinLink] Łączenie z {server.Name} ({server.IP}:{server.JavaPort})");

            bool connected = await javaBridge.ConnectAsync(server.IP, server.JavaPort, 10f);

            if (connected)
            {
                CloseServerMenu();
                RLog.Msg($"[KelvinLink] Pomyślnie połączono z {server.Name}");
            }
            else
            {
                RLog.Error($"[KelvinLink] Nie udało się połączyć z {server.Name}");
                currentServer = null;
            }
        }

        protected override void OnDeinitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod wyłączany");
            isAutoReconnectEnabled = false;
            StopGameStateUpdates();

            if (sceneLoadedAction != null)
            {
                SceneManager.sceneLoaded -= sceneLoadedAction;
                sceneLoadedAction = null;
            }

            javaBridge?.Disconnect();

            if (serverMenuPanel != null)
            {
                UnityEngine.Object.Destroy(serverMenuPanel);
                serverMenuPanel = null;
            }

            if (helperObject != null)
            {
                UnityEngine.Object.Destroy(helperObject);
                helperObject = null;
                helperComponent = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RLog.Msg($"[KelvinLink] Załadowano scenę: {scene.name}");

            if (!string.IsNullOrEmpty(scene.name) && scene.name.ToLower().Contains("title"))
            {
                helperComponent.StartCoroutine(AddKelvinButtonCoroutine());
            }

            if (javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent("SCENE_CHANGE", scene.name);
            }
        }

        private IEnumerator AddKelvinButtonCoroutine()
        {
            yield return new WaitForSeconds(0.15f);

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono Canvas w menu!");
                yield break;
            }

            var existingButton = UnityEngine.Object.FindObjectOfType<Button>();
            if (existingButton == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono przycisku do klonowania!");
                yield break;
            }

            var kelvinBtnObj = UnityEngine.Object.Instantiate(existingButton.gameObject, existingButton.transform.parent);
            kelvinBtnObj.name = "KelvinLinkButton";

            var btn = kelvinBtnObj.GetComponent<Button>();
            var text = kelvinBtnObj.GetComponentInChildren<Text>();

            if (text != null)
                text.text = "🌐 KelvinLink";

            btn.onClick.RemoveAllListeners();
            // Używamy wzorca (UnityAction)new System.Action dla IL2CPP
            btn.onClick.AddListener((UnityAction)new System.Action(OpenKelvinServerMenu));

            var rt = kelvinBtnObj.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition -= new Vector2(0f, 100f);

            CreateConnectionStatusIndicator(kelvinBtnObj);
        }

        private void CreateConnectionStatusIndicator(GameObject buttonObj)
        {
            var indicator = new GameObject("ConnectionStatus");
            indicator.transform.SetParent(buttonObj.transform, false);

            var indicatorImage = indicator.AddComponent<Image>();
            indicatorImage.color = javaBridge.IsConnected() ? Color.green : Color.red;

            var indicatorRT = indicator.GetComponent<RectTransform>();
            indicatorRT.anchorMin = new Vector2(1f, 1f);
            indicatorRT.anchorMax = new Vector2(1f, 1f);
            indicatorRT.anchoredPosition = new Vector2(-10f, -10f);
            indicatorRT.sizeDelta = new Vector2(15f, 15f);

            helperComponent.StartCoroutine(UpdateConnectionIndicator(indicatorImage));
        }

        private IEnumerator UpdateConnectionIndicator(Image indicator)
        {
            while (indicator != null)
            {
                indicator.color = javaBridge.IsConnected() ? Color.green : Color.red;
                yield return new WaitForSeconds(1f);
            }
        }

        private void OpenKelvinServerMenu()
        {
            if (serverMenuPanel != null)
            {
                serverMenuPanel.SetActive(true);
                RefreshServerList();
                return;
            }
            helperComponent.StartCoroutine(CreateServerMenuCoroutine());
        }

        private void RefreshServerList()
        {
            RLog.Msg("[KelvinLink] Refreshing server list...");
        }

        private IEnumerator CreateServerMenuCoroutine()
        {
            yield return new WaitForEndOfFrame();

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono Canvas!");
                yield break;
            }

            serverMenuPanel = new GameObject("KelvinServerMenu");
            serverMenuPanel.transform.SetParent(canvas.transform, false);

            var panelImage = serverMenuPanel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            var panelRT = serverMenuPanel.GetComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            CreateServerMenuContent();
            CreateMenuTitle();
        }

        private void CreateMenuTitle()
        {
            var titleObj = new GameObject("MenuTitle");
            titleObj.transform.SetParent(serverMenuPanel.transform, false);

            var titleText = titleObj.AddComponent<Text>();
            titleText.text = "KelvinLink - Server Browser";
            titleText.color = Color.white;
            titleText.fontSize = 24;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.fontStyle = FontStyle.Bold;

            var titleRT = titleObj.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 0.9f);
            titleRT.anchorMax = new Vector2(1f, 0.95f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
        }

        private void CreateServerMenuContent()
        {
            float serverButtonHeight = 90f;
            float spacing = 20f;
            float startY = -150f;

            for (int i = 0; i < availableServers.Count; i++)
            {
                CreateServerButton(serverMenuPanel, availableServers[i], i, serverButtonHeight, spacing, startY);
            }

            CreateCloseButton();
            CreateRefreshButton();
        }

        private void CreateServerButton(GameObject parent, ServerInfo server, int index, float buttonHeight, float spacing, float startY)
        {
            var serverBtnObj = new GameObject($"ServerButton_{index}");
            serverBtnObj.transform.SetParent(parent.transform, false);

            var serverBtn = serverBtnObj.AddComponent<Button>();
            var btnImage = serverBtnObj.AddComponent<Image>();

            // Różne kolory dla różnych typów serwerów
            switch (server.ServerType.ToLower())
            {
                case "pve":
                    btnImage.color = new Color(0.2f, 0.4f, 0.3f, 0.8f);
                    break;
                case "pvp":
                    btnImage.color = new Color(0.4f, 0.2f, 0.2f, 0.8f);
                    break;
                default:
                    btnImage.color = new Color(0.2f, 0.3f, 0.4f, 0.8f);
                    break;
            }

            var serverRT = serverBtnObj.GetComponent<RectTransform>();
            serverRT.anchorMin = new Vector2(0.1f, 0.5f);
            serverRT.anchorMax = new Vector2(0.9f, 0.5f);
            serverRT.anchoredPosition = new Vector2(0, startY - (index * (buttonHeight + spacing)));
            serverRT.sizeDelta = new Vector2(0, buttonHeight);

            CreateServerButtonText(serverBtnObj, server);

            // Używamy wzorca (UnityAction)new System.Action dla IL2CPP z capture zmiennej
            int serverIndex = index; // Capture dla closure
            serverBtn.onClick.AddListener((UnityAction)new System.Action(() => ConnectToServerAsync(availableServers[serverIndex])));
        }

        private void CreateServerButtonText(GameObject buttonObj, ServerInfo server)
        {
            var textObj = new GameObject("ServerText");
            textObj.transform.SetParent(buttonObj.transform, false);

            var text = textObj.AddComponent<Text>();

            string passwordIcon = server.HasPassword ? "🔒" : "🔓";
            string statusColor = server.Ping < 100 ? "#00FF00" : server.Ping < 200 ? "#FFFF00" : "#FF0000";

            text.text = $"<b>{server.Name}</b> {passwordIcon}\n" +
                       $"{server.IP}:{server.JavaPort} | {server.Players} | <color={statusColor}>{server.Ping}ms</color> | {server.ServerType}\n" +
                       $"<i>{server.Description}</i>";

            text.color = Color.white;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = true;

            var textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 5f);
            textRT.offsetMax = new Vector2(-10f, -5f);
        }

        private void CreateCloseButton()
        {
            var closeBtnObj = new GameObject("CloseButton");
            closeBtnObj.transform.SetParent(serverMenuPanel.transform, false);

            var closeBtn = closeBtnObj.AddComponent<Button>();
            var closeBtnImage = closeBtnObj.AddComponent<Image>();
            closeBtnImage.color = new Color(0.8f, 0.2f, 0.2f, 0.8f);

            var closeBtnRT = closeBtnObj.GetComponent<RectTransform>();
            closeBtnRT.anchorMin = new Vector2(0.85f, 0.85f);
            closeBtnRT.anchorMax = new Vector2(0.95f, 0.95f);
            closeBtnRT.offsetMin = Vector2.zero;
            closeBtnRT.offsetMax = Vector2.zero;

            var closeText = new GameObject("CloseText");
            closeText.transform.SetParent(closeBtnObj.transform, false);
            var closeTextComp = closeText.AddComponent<Text>();
            closeTextComp.text = "✕";
            closeTextComp.color = Color.white;
            closeTextComp.fontSize = 20;
            closeTextComp.alignment = TextAnchor.MiddleCenter;

            var closeTextRT = closeText.GetComponent<RectTransform>();
            closeTextRT.anchorMin = Vector2.zero;
            closeTextRT.anchorMax = Vector2.one;
            closeTextRT.offsetMin = Vector2.zero;
            closeTextRT.offsetMax = Vector2.zero;

            // Używamy wzorca (UnityAction)new System.Action dla IL2CPP
            closeBtn.onClick.AddListener((UnityAction)new System.Action(CloseServerMenu));
        }

        private void CreateRefreshButton()
        {
            var refreshBtnObj = new GameObject("RefreshButton");
            refreshBtnObj.transform.SetParent(serverMenuPanel.transform, false);

            var refreshBtn = refreshBtnObj.AddComponent<Button>();
            var refreshBtnImage = refreshBtnObj.AddComponent<Image>();
            refreshBtnImage.color = new Color(0.3f, 0.6f, 0.3f, 0.8f);

            var refreshBtnRT = refreshBtnObj.GetComponent<RectTransform>();
            refreshBtnRT.anchorMin = new Vector2(0.05f, 0.85f);
            refreshBtnRT.anchorMax = new Vector2(0.15f, 0.95f);
            refreshBtnRT.offsetMin = Vector2.zero;
            refreshBtnRT.offsetMax = Vector2.zero;

            var refreshText = new GameObject("RefreshText");
            refreshText.transform.SetParent(refreshBtnObj.transform, false);
            var refreshTextComp = refreshText.AddComponent<Text>();
            refreshTextComp.text = "⟳";
            refreshTextComp.color = Color.white;
            refreshTextComp.fontSize = 18;
            refreshTextComp.alignment = TextAnchor.MiddleCenter;

            var refreshTextRT = refreshText.GetComponent<RectTransform>();
            refreshTextRT.anchorMin = Vector2.zero;
            refreshTextRT.anchorMax = Vector2.one;
            refreshTextRT.offsetMin = Vector2.zero;
            refreshTextRT.offsetMax = Vector2.zero;

            // Używamy wzorca (UnityAction)new System.Action dla IL2CPP
            refreshBtn.onClick.AddListener((UnityAction)new System.Action(RefreshServerList));
        }

        private void CloseServerMenu()
        {
            if (serverMenuPanel != null)
            {
                serverMenuPanel.SetActive(false);
            }
        }

        public void DisconnectFromCurrentServer()
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent("PLAYER_LEAVE", SystemInfo.deviceName);
                javaBridge.Disconnect();
                currentServer = null;
                RLog.Msg("[KelvinLink] Ręcznie rozłączono z serwera");
            }
        }

        // Metody do komunikacji z serwerem Java - można wywoływać z innych części kodu
        public void SendPlayerAction(string action, string data = "")
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent($"PLAYER_ACTION:{action}", data);
            }
        }

        public void SendBuildingPlaced(Vector3 position, string buildingType)
        {
            if (javaBridge.IsConnected())
            {
                string buildingData = $"{position.x:F3},{position.y:F3},{position.z:F3},{buildingType}";
                javaBridge.SendGameEvent("BUILDING_PLACED", buildingData);
            }
        }

        public void SendInventoryUpdate(string itemName, int quantity, string action = "add")
        {
            if (javaBridge.IsConnected())
            {
                string inventoryData = $"{itemName},{quantity},{action}";
                javaBridge.SendGameEvent("INVENTORY_UPDATE", inventoryData);
            }
        }

        public void SendPlayerDeath(string cause = "unknown")
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent("PLAYER_DEATH", cause);
            }
        }

        public void SendPlayerRespawn(Vector3 position)
        {
            if (javaBridge.IsConnected())
            {
                string respawnData = $"{position.x:F3},{position.y:F3},{position.z:F3}";
                javaBridge.SendGameEvent("PLAYER_RESPAWN", respawnData);
            }
        }

        // Pobieranie aktualnego statusu połączenia
        public bool IsConnectedToServer()
        {
            return javaBridge != null && javaBridge.IsConnected();
        }

        public ServerInfo GetCurrentServer()
        {
            return currentServer;
        }

        // Metody do obsługi pozycji gracza - można wywołać z Update() w innej klasie
        public void UpdatePlayerPosition(Vector3 position, Vector3 rotation)
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendPlayerPosition(position, rotation);
            }
        }
    }

    [System.Serializable]
    public class ServerInfo
    {
        public string Name;
        public string IP;
        public int Port;
        public int JavaPort;
        public string Players;
        public int Ping;
        public bool HasPassword;
        public string ServerType;
        public string Description;
        public string Region;
        public string Version;
        public bool IsOnline;

        public ServerInfo()
        {
            Region = "Unknown";
            Version = "1.0.0";
            IsOnline = true;
        }
    }

    public class KelvinLinkHelper : MonoBehaviour
    {
        private float positionUpdateTimer = 0f;
        private float positionUpdateInterval = 5f; // Aktualizuj pozycję co 5 sekund
        private KelvinLinkClient parentMod;

        void Start()
        {
            parentMod = GameObject.FindObjectOfType<KelvinLinkClient>();
        }
        

        
        void Update()
        {
            // Debug klawisz F1
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log("[KelvinLink] Debug key pressed - Connection status: " +
                         (parentMod?.IsConnectedToServer() ?? false));

                if (parentMod != null && parentMod.IsConnectedToServer())
                {
                    var currentServer = parentMod.GetCurrentServer();
                    if (currentServer != null)
                    {
                        Debug.Log($"[KelvinLink] Connected to: {currentServer.Name}");
                    }
                }
            }

            // Debug klawisz F2 - wymuś rozłączenie
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log("[KelvinLink] Force disconnect requested");
                parentMod?.DisconnectFromCurrentServer();
            }

            // Debug klawisz F3 - wyślij testową wiadomość
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Debug.Log("[KelvinLink] Sending test message");
                parentMod?.SendPlayerAction("TEST", "Debug test message from F3 key");
            }

            // Regularna aktualizacja pozycji gracza
            positionUpdateTimer += Time.deltaTime;
            if (positionUpdateTimer >= positionUpdateInterval)
            {
                positionUpdateTimer = 0f;
                UpdatePlayerPositionIfConnected();
            }
        }

        private void UpdatePlayerPositionIfConnected()
        {
            if (parentMod != null && parentMod.IsConnectedToServer())
            {
                // Próba pobrania pozycji gracza - może być różna w zależności od implementacji Sons of the Forest
                Transform playerTransform = GetPlayerTransform();
                if (playerTransform != null)
                {
                    parentMod.UpdatePlayerPosition(playerTransform.position, playerTransform.eulerAngles);
                }
            }
        }

        private Transform GetPlayerTransform()
        {
            // To może wymagać dostosowania w zależności od struktury Sons of the Forest
            // Przykładowe sposoby znalezienia gracza:

            // Metoda 1: Znajdź przez tag
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) return player.transform;

            // Metoda 2: Znajdź przez nazwę
            player = GameObject.Find("Player");
            if (player != null) return player.transform;

            // Metoda 3: Znajdź pierwszy obiekt z konkretnym komponentem
            var playerController = FindObjectOfType<CharacterController>();
            if (playerController != null) return playerController.transform;

            // Metoda 4: Użyj Camera.main jako przybliżenie pozycji gracza
            if (Camera.main != null) return Camera.main.transform;

            return null;
        }

        // Metody pomocnicze do testowania
        void OnGUI()
        {
            if (parentMod == null) return;

            // Wyświetl status połączenia w lewym górnym rogu (tylko w trybie debug)
            if (Debug.isDebugBuild)
            {
                GUILayout.BeginArea(new Rect(10, 10, 300, 200));

                GUILayout.Label($"KelvinLink Status: {(parentMod.IsConnectedToServer() ? "Connected" : "Disconnected")}");

                if (parentMod.IsConnectedToServer())
                {
                    var server = parentMod.GetCurrentServer();
                    if (server != null)
                    {
                        GUILayout.Label($"Server: {server.Name}");
                        GUILayout.Label($"Address: {server.IP}:{server.JavaPort}");
                        GUILayout.Label($"Type: {server.ServerType}");
                    }
                }

                GUILayout.Space(10);
                GUILayout.Label("Debug Keys:");
                GUILayout.Label("F1 - Status info");
                GUILayout.Label("F2 - Force disconnect");
                GUILayout.Label("F3 - Send test message");

                GUILayout.EndArea();
            }
        }
    }

    // Dodatkowa klasa do zarządzania eventami gry
    public static class KelvinLinkEvents
    {
        // Eventy które można wywołać z innych części kodu gdy coś się dzieje w grze
        public static event System.Action<Vector3, string> OnBuildingPlaced;
        public static event System.Action<string, int> OnItemPickedUp;
        public static event System.Action<string> OnPlayerDied;
        public static event System.Action<Vector3> OnPlayerRespawned;
        public static event System.Action<string, string> OnPlayerAction;

        // Metody do wywoływania eventów
        public static void TriggerBuildingPlaced(Vector3 position, string buildingType)
        {
            OnBuildingPlaced?.Invoke(position, buildingType);
        }

        public static void TriggerItemPickedUp(string itemName, int quantity)
        {
            OnItemPickedUp?.Invoke(itemName, quantity);
        }

        public static void TriggerPlayerDied(string cause)
        {
            OnPlayerDied?.Invoke(cause);
        }

        public static void TriggerPlayerRespawned(Vector3 position)
        {
            OnPlayerRespawned?.Invoke(position);
        }

        public static void TriggerPlayerAction(string action, string data)
        {
            OnPlayerAction?.Invoke(action, data);
        }

        // Metoda do subskrypcji eventów przez KelvinLinkClient
        public static void SubscribeToEvents(KelvinLinkClient client)
        {
            OnBuildingPlaced += (position, buildingType) => client.SendBuildingPlaced(position, buildingType);
            OnItemPickedUp += (itemName, quantity) => client.SendInventoryUpdate(itemName, quantity, "add");
            OnPlayerDied += (cause) => client.SendPlayerDeath(cause);
            OnPlayerRespawned += (position) => client.SendPlayerRespawn(position);
            OnPlayerAction += (action, data) => client.SendPlayerAction(action, data);
        }
    }
}