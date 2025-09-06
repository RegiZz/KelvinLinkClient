using Il2CppInterop.Runtime.Injection;
using KelvinLinkClient;
using RedLoader;
using RedLoader.Unity.IL2CPP.Utils;
using SonsSdk;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KelvinLinkMod
{
    /// <summary>
    /// Główny mod klienta KelvinLink — odpowiada za inicjalizację, zarządzanie połączeniem z backendem Java,
    /// tworzenie GUI oraz obsługę zdarzeń gry.
    /// </summary>
    public class KelvinLinkClient : SonsMod
    {
        private GameObject helperObject;
        public static KelvinLinkClient Instance { get; private set; }
        private KelvinLinkHelper helperComponent;
        private UnityAction<Scene, LoadSceneMode> sceneLoadedAction;
        private GameObject serverMenuPanel;
        private JavaConnectionManager javaBridge;
        private bool isAutoReconnectEnabled = true;
        private float reconnectInterval = 5f;
        private Coroutine gameStateUpdateCoroutine;

        /// <summary>
        /// Konstruktor modu — ustawia singleton Instance.
        /// </summary>
        public KelvinLinkClient()
        {
            Instance = this;
        }

        private static bool typesRegistered;

        /// <summary>
        /// Rejestruje typy IL2CPP wymagane przez mod (KelvinLinkHelper, JavaConnectionManager).
        /// Wywoływane przed dodaniem komponentów typu Il2Cpp.
        /// </summary>
        private static void TryRegisterTypes()
        {
            if (typesRegistered) return;
            ClassInjector.RegisterTypeInIl2Cpp<KelvinLinkHelper>();
            ClassInjector.RegisterTypeInIl2Cpp<JavaConnectionManager>();
            typesRegistered = true;
        }

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

        /// <summary>
        /// Wywoływane przy inicjalizacji modu. Rejestruje typy IL2CPP, podłącza handler scen i tworzy helpera jeśli scena jest już wczytana.
        /// </summary>
        protected override void OnInitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod zainicjalizowany");
            TryRegisterTypes();
            if (sceneLoadedAction == null)
                sceneLoadedAction = (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded);
            SceneManager.sceneLoaded -= sceneLoadedAction;
            SceneManager.sceneLoaded += sceneLoadedAction;
            if (SceneManager.GetActiveScene().isLoaded)
                CreateHelper();
        }

        /// <summary>
        /// Konfiguruje zdarzenia połączenia javaBridge (OnConnected, OnDisconnected, OnMessageReceived).
        /// </summary>
        private void SetupConnectionEvents()
        {
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

        /// <summary>
        /// Tworzy helperObject z komponentami KelvinLinkHelper i JavaConnectionManager oraz ustawia ich zdarzenia.
        /// </summary>
        private void CreateHelper()
        {
            if (helperObject != null) return;
            helperObject = new GameObject("KelvinLinkHelper");
            UnityEngine.Object.DontDestroyOnLoad(helperObject);
            helperComponent = helperObject.AddComponent<KelvinLinkHelper>();
            javaBridge = helperObject.AddComponent<JavaConnectionManager>();
            SetupConnectionEvents();
        }

        /// <summary>
        /// Handler wywoływany po załadowaniu sceny. Uruchamia dodawanie przycisku w menu tytułowym i wysyła event sceny.
        /// </summary>
        /// <param name="scene">Załadowana scena</param>
        /// <param name="mode">Tryb ładowania</param>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RLog.Msg($"[KelvinLink] Załadowano scenę: {scene.name}");
            CreateHelper();
            if (!string.IsNullOrEmpty(scene.name) && scene.name.ToLower().Contains("title"))
            {
                helperComponent.StartCoroutine(AddKelvinButtonCoroutine());
            }
            if (javaBridge != null && javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent("SCENE_CHANGE", scene.name);
            }
        }

        /// <summary>
        /// Wysyła początkowe informacje o graczu i stanie gry po połączeniu.
        /// </summary>
        private void SendInitialGameState()
        {
            javaBridge.SendGameEvent("PLAYER_JOIN", SystemInfo.deviceName);
            javaBridge.SendGameState();
        }

        /// <summary>
        /// Uruchamia coroutine wysyłającą okresowo stan gry.
        /// </summary>
        private void StartGameStateUpdates()
        {
            if (gameStateUpdateCoroutine != null)
            {
                helperComponent.StopCoroutine(gameStateUpdateCoroutine);
            }
            gameStateUpdateCoroutine = helperComponent.StartCoroutine(GameStateUpdateCoroutine());
        }

        /// <summary>
        /// Zatrzymuje coroutine aktualizacji stanu gry.
        /// </summary>
        private void StopGameStateUpdates()
        {
            if (gameStateUpdateCoroutine != null)
            {
                helperComponent.StopCoroutine(gameStateUpdateCoroutine);
                gameStateUpdateCoroutine = null;
            }
        }

        /// <summary>
        /// Coroutine wysyłająca co 30 sekund stan gry do backendu (dopóki jest połączenie).
        /// </summary>
        /// <returns>Enumerator dla coroutine</returns>
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

        /// <summary>
        /// Obsługuje wiadomości przychodzące z backendu Java i deleguje je do odpowiednich handlerów.
        /// </summary>
        /// <param name="message">Pełna treść wiadomości</param>
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

        /// <summary>
        /// Coroutine wykonująca automatyczne próby reconnectu po określonym czasie.
        /// </summary>
        /// <returns>Enumerator dla coroutine</returns>
        private IEnumerator AutoReconnectCoroutine()
        {
            yield return new WaitForSeconds(reconnectInterval);
            if (!javaBridge.IsConnected() && currentServer != null)
            {
                RLog.Msg("[KelvinLink] Attempting to reconnect...");
                ConnectToServerAsync(currentServer);
            }
        }

        /// <summary>
        /// Asynchroniczna próba połączenia z wybranym serwerem Java.
        /// </summary>
        /// <param name="server">Dane serwera</param>
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

        /// <summary>
        /// Wywoływane przy deinicjalizacji modu — odłącza eventy, zatrzymuje coroutines i usuwa obiekty pomocnicze.
        /// </summary>
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
            if (javaBridge != null)
            {
                javaBridge.Disconnect();
                javaBridge = null;
            }
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

        /// <summary>
        /// Coroutine dodająca przycisk KelvinLink do istniejącego menu (klonowanie istniejącego przycisku).
        /// Ustawia tekst przycisku i listener w sposób zgodny z IL2CPP.
        /// </summary>
        /// <returns>Enumerator dla coroutine</returns>
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
            btn.onClick.AddListener((UnityAction)new System.Action(OpenKelvinServerMenu));
            var rt = kelvinBtnObj.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition -= new Vector2(0f, 100f);
            CreateConnectionStatusIndicator(kelvinBtnObj);
        }

        /// <summary>
        /// Tworzy mały wskaźnik (Image) przy przycisku pokazujący aktualny status połączenia (zielony/czerwony).
        /// </summary>
        /// <param name="buttonObj">Obiekt przycisku, do którego dołączany jest wskaźnik</param>
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

        /// <summary>
        /// Coroutine aktualizująca kolor wskaźnika połączenia co sekundę.
        /// </summary>
        /// <param name="indicator">Komponent Image reprezentujący wskaźnik</param>
        /// <returns>Enumerator dla coroutine</returns>
        private IEnumerator UpdateConnectionIndicator(Image indicator)
        {
            while (indicator != null)
            {
                indicator.color = javaBridge.IsConnected() ? Color.green : Color.red;
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>
        /// Otwiera panel serwerów — jeśli panel istnieje, pokazuje go, w przeciwnym razie tworzy.
        /// </summary>
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

        /// <summary>
        /// Odświeża listę serwerów (placeholder — aktualizacja powinna pobierać dane z backendu).
        /// </summary>
        private void RefreshServerList()
        {
            RLog.Msg("[KelvinLink] Refreshing server list...");
        }

        /// <summary>
        /// Coroutine tworząca panel menu serwerów (jeśli nie istnieje), klonująca przykładowy przycisk i ustawiająca jego tekst oraz listener.
        /// Rejestruje także ponowną inicjalizację po załadowaniu sceny.
        /// </summary>
        /// <returns>Enumerator dla coroutine</returns>
        IEnumerator CreateServerMenuCoroutine()
        {
            yield return new WaitForEndOfFrame();
            Canvas canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var all = Resources.FindObjectsOfTypeAll<Canvas>();
                if (all.Length > 0) canvas = all[0];
            }
            if (canvas == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono Canvas w menu!");
                yield break;
            }
            if (serverMenuPanel == null)
            {
                serverMenuPanel = new GameObject("KelvinServerMenu");
                serverMenuPanel.transform.SetParent(canvas.transform, false);
                var rtPanel = serverMenuPanel.AddComponent<RectTransform>();
                rtPanel.anchorMin = new Vector2(0.5f, 0.5f);
                rtPanel.anchorMax = new Vector2(0.5f, 0.5f);
                rtPanel.pivot = new Vector2(0.5f, 0.5f);
                rtPanel.anchoredPosition = Vector2.zero;
                rtPanel.sizeDelta = new Vector2(420, 300);
                var imgPanel = serverMenuPanel.AddComponent<UnityEngine.UI.Image>();
                imgPanel.color = new Color(0f, 0f, 0f, 0.7f);
                imgPanel.raycastTarget = false;
                RLog.Msg("[KelvinLink] Utworzono serverMenuPanel");
            }
            var existingButton = UnityEngine.Object.FindObjectOfType<UnityEngine.UI.Button>();
            if (existingButton == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono przycisku do klonowania!");
                yield break;
            }
            var kelvinBtnObj = UnityEngine.Object.Instantiate(existingButton.gameObject, serverMenuPanel.transform);
            kelvinBtnObj.name = "KelvinLinkButton";
            kelvinBtnObj.transform.SetAsLastSibling();
            var childNames = kelvinBtnObj.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
            RLog.Msg("[KelvinLink] Sklonowany przycisk, children: " + string.Join(",", childNames));
            var btn = kelvinBtnObj.GetComponent<UnityEngine.UI.Button>();
            if (btn == null)
            {
                RLog.Error("[KelvinLink] Sklonowany obiekt nie ma komponentu Button!");
            }
            else
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener((UnityEngine.Events.UnityAction)new System.Action(CreateServerMenuContent));
                btn.interactable = true;
                RLog.Msg("[KelvinLink] Dodano listener do przycisku");
            }
            bool anySet = false;
            var texts = kelvinBtnObj.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            foreach (var t in texts)
            {
                RLog.Msg($"[KelvinLink] Found UI.Text on '{t.gameObject.name}' old='{t.text}'");
                t.text = "🌐 KelvinLink";
                t.color = Color.white;
                anySet = true;
            }
            var tmps = kelvinBtnObj.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            foreach (var tt in tmps)
            {
                RLog.Msg($"[KelvinLink] Found TMP on '{tt.gameObject.name}' old='{tt.text}'");
                tt.text = "🌐 KelvinLink";
                tt.color = Color.white;
                anySet = true;
            }
            if (!anySet)
            {
                RLog.Error("[KelvinLink] Nie znaleziono komponentu Text ani TextMeshProUGUI w sklonowanym przycisku!");
            }
            SceneManager.sceneLoaded -= (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded_RecreateUI);
            SceneManager.sceneLoaded += (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded_RecreateUI);
        }

        /// <summary>
        /// Handler wywoływany po załadowaniu sceny, który ponownie wywołuje tworzenie UI jeśli trzeba.
        /// </summary>
        /// <param name="scene">Załadowana scena</param>
        /// <param name="mode">Tryb ładowania</param>
        void OnSceneLoaded_RecreateUI(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded_RecreateUI);
            helperComponent.StartCoroutine(CreateServerMenuCoroutine());
        }

        /// <summary>
        /// Tworzy tytuł panelu serwerów (obiekt Text).
        /// </summary>
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

        /// <summary>
        /// Buduje zawartość panelu serwerów — listę serwerów i podstawowe przyciski (zamknij, odśwież).
        /// </summary>
        private void CreateServerMenuContent()
        {
            float serverButtonHeight = 90f;
            float spacing = 20f;
            float startY = -150f;
            for (int i = 0; i < availableServers.Count; i++)
            {
                CreateServerButton(serverMenuPanel, availableServers[i], i, serverButtonHeight, spacing, startY);
            }
            CreateMenuTitle();
            CreateCloseButton();
            CreateRefreshButton();
        }

        /// <summary>
        /// Tworzy pojedynczy przycisk reprezentujący serwer i podłącza akcję łączenia.
        /// </summary>
        /// <param name="parent">Rodzic dla przycisku</param>
        /// <param name="server">Dane serwera</param>
        /// <param name="index">Index w liście</param>
        /// <param name="buttonHeight">Wysokość przycisku</param>
        /// <param name="spacing">Odstęp między przyciskami</param>
        /// <param name="startY">Początkowa pozycja Y</param>
        private void CreateServerButton(GameObject parent, ServerInfo server, int index, float buttonHeight, float spacing, float startY)
        {
            var serverBtnObj = new GameObject($"ServerButton_{index}");
            serverBtnObj.transform.SetParent(parent.transform, false);
            var serverBtn = serverBtnObj.AddComponent<Button>();
            var btnImage = serverBtnObj.AddComponent<Image>();
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
            int serverIndex = index;
            serverBtn.onClick.AddListener((UnityAction)new System.Action(() => ConnectToServerAsync(availableServers[serverIndex])));
        }

        /// <summary>
        /// Tworzy tekst (opis) dla przycisku serwera, w tym ikonkę hasła i kolor statusu pingu.
        /// </summary>
        /// <param name="buttonObj">Obiekt przycisku</param>
        /// <param name="server">Dane serwera</param>
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

        /// <summary>
        /// Tworzy przycisk zamykania panelu serwerów i podłącza do niego akcję CloseServerMenu.
        /// </summary>
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
            closeBtn.onClick.AddListener((UnityAction)new System.Action(CloseServerMenu));
        }

        /// <summary>
        /// Tworzy przycisk odświeżania listy serwerów i podłącza go do metody RefreshServerList.
        /// </summary>
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
            refreshBtn.onClick.AddListener((UnityAction)new System.Action(RefreshServerList));
        }

        /// <summary>
        /// Ukrywa panel serwerów (jeśli jest otwarty).
        /// </summary>
        private void CloseServerMenu()
        {
            if (serverMenuPanel != null)
            {
                serverMenuPanel.SetActive(false);
            }
        }

        /// <summary>
        /// Ręczne rozłączenie się od aktualnego serwera (wysyła event LEAVE i rozłącza javaBridge).
        /// </summary>
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

        /// <summary>
        /// Wysyła akcję gracza do backendu Java (jeśli jest połączenie).
        /// </summary>
        /// <param name="action">Nazwa akcji</param>
        /// <param name="data">Dodatkowe dane</param>
        public void SendPlayerAction(string action, string data = "")
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent($"PLAYER_ACTION:{action}", data);
            }
        }

        /// <summary>
        /// Wysyła informację o postawieniu budynku do backendu.
        /// </summary>
        /// <param name="position">Pozycja</param>
        /// <param name="buildingType">Typ budynku</param>
        public void SendBuildingPlaced(Vector3 position, string buildingType)
        {
            if (javaBridge.IsConnected())
            {
                string buildingData = $"{position.x:F3},{position.y:F3},{position.z:F3},{buildingType}";
                javaBridge.SendGameEvent("BUILDING_PLACED", buildingData);
            }
        }

        /// <summary>
        /// Wysyła aktualizację inwentarza do backendu.
        /// </summary>
        public void SendInventoryUpdate(string itemName, int quantity, string action = "add")
        {
            if (javaBridge.IsConnected())
            {
                string inventoryData = $"{itemName},{quantity},{action}";
                javaBridge.SendGameEvent("INVENTORY_UPDATE", inventoryData);
            }
        }

        /// <summary>
        /// Wysyła event śmierci gracza do backendu.
        /// </summary>
        public void SendPlayerDeath(string cause = "unknown")
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendGameEvent("PLAYER_DEATH", cause);
            }
        }

        /// <summary>
        /// Wysyła event respawnu gracza z pozycją.
        /// </summary>
        public void SendPlayerRespawn(Vector3 position)
        {
            if (javaBridge.IsConnected())
            {
                string respawnData = $"{position.x:F3},{position.y:F3},{position.z:F3}";
                javaBridge.SendGameEvent("PLAYER_RESPAWN", respawnData);
            }
        }

        /// <summary>
        /// Zwraca status połączenia do serwera Java.
        /// </summary>
        /// <returns>Prawda jeśli jest połączenie</returns>
        public bool IsConnectedToServer()
        {
            return javaBridge != null && javaBridge.IsConnected();
        }

        /// <summary>
        /// Zwraca aktualnie połączony serwer (lub null).
        /// </summary>
        public ServerInfo GetCurrentServer()
        {
            return currentServer;
        }

        /// <summary>
        /// Wysyła aktualizację pozycji gracza do backendu (jeśli połączony).
        /// </summary>
        public void UpdatePlayerPosition(Vector3 position, Vector3 rotation)
        {
            if (javaBridge.IsConnected())
            {
                javaBridge.SendPlayerPosition(position, rotation);
            }
        }
}

/// <summary>
///Reprezentuje informacje o serwerze(nazwa, IP, porty, liczba graczy, ping, opis).
/// </summary>
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

        /// <summary>
        /// Konstruktor inicjalizujący pola domyślne.
        /// </summary>
        public ServerInfo()
        {
            Region = "Unknown";
            Version = "1.0.0";
            IsOnline = true;
        }
    }

    /// <summary>
    /// MonoBehaviour uruchamiany na pomocniczym GameObject — wykonuje aktualizacje pozycji gracza, debug keybindy i GUI pomocnicze.
    /// </summary>
    public class KelvinLinkHelper : MonoBehaviour
    {
        private float positionUpdateTimer = 0f;
        private float positionUpdateInterval = 5f;
        private KelvinLinkClient parentMod;

        /// <summary>
        /// Start komponentu — pobiera referencję do głównego modu.
        /// </summary>
        void Start()
        {
            parentMod = KelvinLinkClient.Instance;
        }

        /// <summary>
        /// Update wywoływany co klatkę — obsługuje debug keys i okresowe aktualizacje pozycji gracza.
        /// </summary>
        void Update()
        {
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
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Debug.Log("[KelvinLink] Force disconnect requested");
                parentMod?.DisconnectFromCurrentServer();
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Debug.Log("[KelvinLink] Sending test message");
                parentMod?.SendPlayerAction("TEST", "Debug test message from F3 key");
            }
            positionUpdateTimer += Time.deltaTime;
            if (positionUpdateTimer >= positionUpdateInterval)
            {
                positionUpdateTimer = 0f;
                UpdatePlayerPositionIfConnected();
            }
        }

        /// <summary>
        /// Jeśli jest połączenie, pobiera transform gracza i wysyła pozycję do modułu.
        /// </summary>
        private void UpdatePlayerPositionIfConnected()
        {
            if (parentMod != null && parentMod.IsConnectedToServer())
            {
                Transform playerTransform = GetPlayerTransform();
                if (playerTransform != null)
                {
                    parentMod.UpdatePlayerPosition(playerTransform.position, playerTransform.eulerAngles);
                }
            }
        }

        /// <summary>
        /// Próbuje znaleźć transform gracza kilkoma heurystykami (tag, nazwa, komponent CharacterController, Camera.main).
        /// </summary>
        /// <returns>Transform gracza lub null jeśli nie znaleziono</returns>
        private Transform GetPlayerTransform()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) return player.transform;
            player = GameObject.Find("Player");
            if (player != null) return player.transform;
            var playerController = FindObjectOfType<CharacterController>();
            if (playerController != null) return playerController.transform;
            if (Camera.main != null) return Camera.main.transform;
            return null;
        }

        /// <summary>
        /// Rysuje proste GUI debugowe w lewym górnym rogu (tylko w buildach debugowych).
        /// </summary>
        void OnGUI()
        {
            if (parentMod == null) return;
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

    /// <summary>
    /// Statyczna klasa publikująca eventy gry, które mogą zostać subskrybowane przez KelvinLinkClient lub inne komponenty.
    /// </summary>
    public static class KelvinLinkEvents
    {
        /// <summary>Wywoływane gdy postawiono budynek (pozycja, typ).</summary>
        public static event System.Action<Vector3, string> OnBuildingPlaced;
        /// <summary>Wywoływane gdy podniesiono przedmiot (nazwa, ilość).</summary>
        public static event System.Action<string, int> OnItemPickedUp;
        /// <summary>Wywoływane gdy gracz zginął (przyczyna).</summary>
        public static event System.Action<string> OnPlayerDied;
        /// <summary>Wywoływane gdy gracz się respawnuje (pozycja).</summary>
        public static event System.Action<Vector3> OnPlayerRespawned;
        /// <summary>Wywoływane gdy wykonano inną akcję gracza (akcja, dane).</summary>
        public static event System.Action<string, string> OnPlayerAction;

        /// <summary>
        /// Wywołuje event budowania budynku.
        /// </summary>
        public static void TriggerBuildingPlaced(Vector3 position, string buildingType)
        {
            OnBuildingPlaced?.Invoke(position, buildingType);
        }

        /// <summary>
        /// Wywołuje event podniesienia przedmiotu.
        /// </summary>
        public static void TriggerItemPickedUp(string itemName, int quantity)
        {
            OnItemPickedUp?.Invoke(itemName, quantity);
        }

        /// <summary>
        /// Wywołuje event śmierci gracza.
        /// </summary>
        public static void TriggerPlayerDied(string cause)
        {
            OnPlayerDied?.Invoke(cause);
        }

        /// <summary>
        /// Wywołuje event respawnu gracza.
        /// </summary>
        public static void TriggerPlayerRespawned(Vector3 position)
        {
            OnPlayerRespawned?.Invoke(position);
        }

        /// <summary>
        /// Wywołuje event akcji gracza.
        /// </summary>
        public static void TriggerPlayerAction(string action, string data)
        {
            OnPlayerAction?.Invoke(action, data);
        }

        /// <summary>
        /// Pomocnicza metoda subskrypcji eventów przez instancję KelvinLinkClient.
        /// </summary>
        /// <param name="client">Instancja klienta, która subskrybuje eventy</param>
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