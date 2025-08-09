using RedLoader;
using RedLoader.Unity.IL2CPP.Utils;
using SonsSdk;
using System;
using System.Collections;
using System.Collections.Generic;
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

        // Lista przykładowych serwerów
        private List<ServerInfo> availableServers = new List<ServerInfo>
        {
            new ServerInfo { Name = "KelvinLink EU #1", IP = "192.168.1.100", Port = 7777, Players = "2/8", Ping = 45 },
            new ServerInfo { Name = "KelvinLink US West", IP = "10.0.0.50", Port = 7778, Players = "5/8", Ping = 120 },
            new ServerInfo { Name = "Private Server", IP = "172.16.1.25", Port = 7779, Players = "1/4", Ping = 23 },
            new ServerInfo { Name = "KelvinLink Asia", IP = "203.0.113.42", Port = 7780, Players = "7/8", Ping = 200 }
        };

        protected override void OnInitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod zainicjalizowany");

            helperObject = new GameObject("KelvinLinkHelper");
            UnityEngine.Object.DontDestroyOnLoad(helperObject);
            helperComponent = helperObject.AddComponent<KelvinLinkHelper>();

            sceneLoadedAction = (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded);
            SceneManager.sceneLoaded += sceneLoadedAction;
        }

        protected override void OnDeinitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod wyłączany");

            if (sceneLoadedAction != null)
            {
                SceneManager.sceneLoaded -= sceneLoadedAction;
                sceneLoadedAction = null;
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

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RLog.Msg($"[KelvinLink] Załadowano scenę: {scene.name}");

            if (!string.IsNullOrEmpty(scene.name) && scene.name.ToLower().Contains("title"))
            {
                helperComponent.StartCoroutine(AddKelvinButtonCoroutine());
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
                text.text = "KelvinLink";

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener((UnityAction)new System.Action(OpenKelvinServerMenu));

            var rt = kelvinBtnObj.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition -= new Vector2(0f, 100f);
        }

        private void OpenKelvinServerMenu()
        {
            RLog.Msg("[KelvinLink] Otwieranie menu serwerów KelvinLink");

            if (serverMenuPanel != null)
            {
                serverMenuPanel.SetActive(true);
                return;
            }

            helperComponent.StartCoroutine(CreateServerMenuCoroutine());
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

            // Tworzymy główny panel menu
            serverMenuPanel = new GameObject("KelvinServerMenu");
            serverMenuPanel.transform.SetParent(canvas.transform, false);

            var panelImage = serverMenuPanel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f); // Ciemne, półprzezroczyste tło

            var panelRT = serverMenuPanel.GetComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            // Dodajemy zawartość menu
            CreateServerMenuContent();
        }

        private void CreateServerMenuContent()
        {
            // Nagłówek
            var titleObj = new GameObject("Title");
            titleObj.transform.SetParent(serverMenuPanel.transform, false);
            var titleText = titleObj.AddComponent<Text>();
            titleText.text = "KelvinLink - Lista Serwerów";
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 32;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;

            var titleRT = titleObj.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.5f, 0.85f);
            titleRT.anchorMax = new Vector2(0.5f, 0.95f);
            titleRT.anchoredPosition = Vector2.zero;
            titleRT.sizeDelta = new Vector2(400, 50);

            // Obszar z listą serwerów
            var serverListObj = new GameObject("ServerList");
            serverListObj.transform.SetParent(serverMenuPanel.transform, false);

            var serverListRT = serverListObj.GetComponent<RectTransform>();
            serverListRT.anchorMin = new Vector2(0.1f, 0.2f);
            serverListRT.anchorMax = new Vector2(0.9f, 0.8f);
            serverListRT.anchoredPosition = Vector2.zero;
            serverListRT.offsetMin = Vector2.zero;
            serverListRT.offsetMax = Vector2.zero;

            // Tworzymy przyciski dla każdego serwera
            float serverButtonHeight = 60f;
            float spacing = 10f;

            for (int i = 0; i < availableServers.Count; i++)
            {
                CreateServerButton(serverListObj, availableServers[i], i, serverButtonHeight, spacing);
            }

            // Przycisk zamknij
            CreateCloseButton();

            // Przycisk odśwież
            CreateRefreshButton();
        }

        private void CreateServerButton(GameObject parent, ServerInfo server, int index, float buttonHeight, float spacing)
        {
            var serverBtnObj = new GameObject($"ServerButton_{index}");
            serverBtnObj.transform.SetParent(parent.transform, false);

            var serverBtnImage = serverBtnObj.AddComponent<Image>();
            serverBtnImage.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);

            var serverBtn = serverBtnObj.AddComponent<Button>();
            serverBtn.targetGraphic = serverBtnImage;

            var serverRT = serverBtnObj.GetComponent<RectTransform>();
            serverRT.anchorMin = new Vector2(0f, 1f);
            serverRT.anchorMax = new Vector2(1f, 1f);
            serverRT.anchoredPosition = new Vector2(0, -(index * (buttonHeight + spacing) + buttonHeight / 2));
            serverRT.sizeDelta = new Vector2(0, buttonHeight);

            // Tekst z informacjami o serwerze
            var serverTextObj = new GameObject("ServerInfo");
            serverTextObj.transform.SetParent(serverBtnObj.transform, false);

            var serverText = serverTextObj.AddComponent<Text>();
            serverText.text = $"{server.Name}\n{server.IP}:{server.Port} | {server.Players} | Ping: {server.Ping}ms";
            serverText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            serverText.fontSize = 14;
            serverText.color = Color.white;
            serverText.alignment = TextAnchor.MiddleLeft;

            var textRT = serverTextObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(20, 0);
            textRT.offsetMax = new Vector2(-20, 0);

            // Dodaj listener do przycisku
            int serverIndex = index; // Capture dla closure
            serverBtn.onClick.AddListener((UnityAction)new System.Action(() => ConnectToServer(availableServers[serverIndex])));
        }

        private void CreateCloseButton()
        {
            var closeBtnObj = new GameObject("CloseButton");
            closeBtnObj.transform.SetParent(serverMenuPanel.transform, false);

            var closeBtnImage = closeBtnObj.AddComponent<Image>();
            closeBtnImage.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

            var closeBtn = closeBtnObj.AddComponent<Button>();
            closeBtn.targetGraphic = closeBtnImage;

            var closeRT = closeBtnObj.GetComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(0.8f, 0.05f);
            closeRT.anchorMax = new Vector2(0.95f, 0.15f);
            closeRT.anchoredPosition = Vector2.zero;
            closeRT.offsetMin = Vector2.zero;
            closeRT.offsetMax = Vector2.zero;

            var closeTextObj = new GameObject("CloseText");
            closeTextObj.transform.SetParent(closeBtnObj.transform, false);

            var closeText = closeTextObj.AddComponent<Text>();
            closeText.text = "Zamknij";
            closeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            closeText.fontSize = 16;
            closeText.color = Color.white;
            closeText.alignment = TextAnchor.MiddleCenter;

            var closeTextRT = closeTextObj.GetComponent<RectTransform>();
            closeTextRT.anchorMin = Vector2.zero;
            closeTextRT.anchorMax = Vector2.one;
            closeTextRT.offsetMin = Vector2.zero;
            closeTextRT.offsetMax = Vector2.zero;

            closeBtn.onClick.AddListener((UnityAction)new System.Action(CloseServerMenu));
        }

        private void CreateRefreshButton()
        {
            var refreshBtnObj = new GameObject("RefreshButton");
            refreshBtnObj.transform.SetParent(serverMenuPanel.transform, false);

            var refreshBtnImage = refreshBtnObj.AddComponent<Image>();
            refreshBtnImage.color = new Color(0.2f, 0.6f, 0.2f, 0.9f);

            var refreshBtn = refreshBtnObj.AddComponent<Button>();
            refreshBtn.targetGraphic = refreshBtnImage;

            var refreshRT = refreshBtnObj.GetComponent<RectTransform>();
            refreshRT.anchorMin = new Vector2(0.05f, 0.05f);
            refreshRT.anchorMax = new Vector2(0.2f, 0.15f);
            refreshRT.anchoredPosition = Vector2.zero;
            refreshRT.offsetMin = Vector2.zero;
            refreshRT.offsetMax = Vector2.zero;

            var refreshTextObj = new GameObject("RefreshText");
            refreshTextObj.transform.SetParent(refreshBtnObj.transform, false);

            var refreshText = refreshTextObj.AddComponent<Text>();
            refreshText.text = "Odśwież";
            refreshText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            refreshText.fontSize = 16;
            refreshText.color = Color.white;
            refreshText.alignment = TextAnchor.MiddleCenter;

            var refreshTextRT = refreshTextObj.GetComponent<RectTransform>();
            refreshTextRT.anchorMin = Vector2.zero;
            refreshTextRT.anchorMax = Vector2.one;
            refreshTextRT.offsetMin = Vector2.zero;
            refreshTextRT.offsetMax = Vector2.zero;

            refreshBtn.onClick.AddListener((UnityAction)new System.Action(RefreshServerList));
        }

        private void ConnectToServer(ServerInfo server)
        {
            RLog.Msg($"[KelvinLink] Łączenie z serwerem: {server.Name} ({server.IP}:{server.Port})");

            // Tutaj dodasz logikę łączenia z serwerem
            // Na przykład: NetworkManager, Mirror, Unity Netcode, etc.

            CloseServerMenu();
        }

        private void CloseServerMenu()
        {
            RLog.Msg("[KelvinLink] Zamykanie menu serwerów");

            if (serverMenuPanel != null)
            {
                serverMenuPanel.SetActive(false);
            }
        }

        private void RefreshServerList()
        {
            RLog.Msg("[KelvinLink] Odświeżanie listy serwerów");

            // Tutaj dodasz logikę odświeżania serwerów
            // Na przykład: zapytanie do master servera, ping serwerów, etc.

            // Przykład symulacji odświeżenia pingów
            foreach (var server in availableServers)
            {
                server.Ping = UnityEngine.Random.Range(20, 300);
            }

            // Odtwórz menu z nowymi danymi
            if (serverMenuPanel != null)
            {
                UnityEngine.Object.Destroy(serverMenuPanel);
                serverMenuPanel = null;
                helperComponent.StartCoroutine(CreateServerMenuCoroutine());
            }
        }
    }

    [System.Serializable]
    public class ServerInfo
    {
        public string Name;
        public string IP;
        public int Port;
        public string Players;
        public int Ping;
    }

    public class KelvinLinkHelper : MonoBehaviour { }
}